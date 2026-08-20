using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace OrbitalSIP.Services;

/// <summary>
/// Keeps the operator's access token alive for the length of a shift.
///
/// The backend issues a 2-day access token and a 30-day refresh token, and used to
/// hand both to a client that kept only the first: <c>refresh_token</c> was parsed at
/// login and dropped on the floor. Nothing anywhere inspected a 401 either. So once the
/// access token aged out — which happens to any widget left running over a long
/// weekend — every backend call started failing, StatusService kept polling into it
/// every 20 seconds and raising a banner each time, and the only way back was to quit
/// and log in again (credentials are deliberately never persisted).
///
/// Refresh is proactive, driven by the token's own <c>exp</c> claim rather than by
/// catching 401s, which keeps <see cref="AuthRefreshHandler"/> out of the business of
/// buffering and replaying request bodies.
/// </summary>
public static class BackendAuth
{
    /// <summary>
    /// One refresh at a time. Every service polls independently, so a token that expires
    /// mid-shift is noticed by several requests at once; without this they would each
    /// spend the refresh token, and rotation means all but the winner would be spending
    /// one the backend has already invalidated.
    /// </summary>
    private static readonly SemaphoreSlim RefreshGate = new(1, 1);

    /// <summary>
    /// Floor between refresh attempts.
    ///
    /// <see cref="AccessTokenLifetime.IsSpent"/> answers "yes" for a token whose expiry
    /// cannot be read, which is the right default — but if the backend ever issues a
    /// token this client cannot decode, "spent" never becomes "fresh" and the gate does
    /// not help: each request would simply take its turn and fire its own refresh POST.
    /// This bounds that to one attempt per interval.
    /// </summary>
    private static readonly TimeSpan MinRefreshInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Deadline for the refresh call itself. Deliberately its own, never the calling
    /// request's — see <see cref="RefreshAsync"/>.
    /// </summary>
    private static readonly TimeSpan RefreshTimeout = TimeSpan.FromSeconds(30);

    private static DateTimeOffset _lastRefreshAttempt = DateTimeOffset.MinValue;
    private static int _sessionEndAnnounced;
    private static int _confirmingUnauthorized;

    /// <summary>
    /// The session cannot be recovered without the operator signing in again. Raised at
    /// most once until <see cref="BeginSession"/> resets it, so a burst of concurrent
    /// 401s produces one prompt rather than one per in-flight request.
    /// </summary>
    public static event Action? SessionExpired;

    /// <summary>Called after a successful sign-in, so a later expiry can be announced again.</summary>
    public static void BeginSession()
    {
        Interlocked.Exchange(ref _sessionEndAnnounced, 0);
        _lastRefreshAttempt = DateTimeOffset.MinValue;
    }

    /// <summary>
    /// Returns the token a request should carry, or null to leave whatever the caller
    /// already put on the request alone.
    /// </summary>
    public static async Task<string?> EnsureFreshAccessTokenAsync(CancellationToken cancellationToken)
    {
        var settings = App.SipService?.CurrentSettings;
        if (settings == null || string.IsNullOrEmpty(settings.RefreshToken))
            return null;

        if (!IsSpent(settings.DecodedToken))
            return null;

        await RefreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Re-read inside the gate: whoever held it before us has very likely just
            // refreshed, and this request only needs the result.
            if (!IsSpent(settings.DecodedToken))
                return string.IsNullOrEmpty(settings.AccessToken) ? null : settings.AccessToken;

            if (DateTimeOffset.UtcNow - _lastRefreshAttempt < MinRefreshInterval)
                return null;   // tried recently and the token still reads as spent

            return await RefreshAsync(settings).ConfigureAwait(false);
        }
        finally
        {
            RefreshGate.Release();
        }
    }

    /// <summary>
    /// A 401 came back for a request that carried a bearer token.
    ///
    /// Deliberately NOT treated as proof the session is over. The request may have gone
    /// out with a token another thread rotated out from under it between building the
    /// header and sending, or the proactive refresh may have failed transiently a moment
    /// earlier and let a known-stale token through. Bouncing the operator to the login
    /// screen on either of those would cost them a shift's worth of state for nothing.
    ///
    /// So this confirms instead: force a refresh, and let the backend decide. Only a
    /// refresh the backend itself rejects ends the session — that path lives in
    /// <see cref="RefreshAsync"/>.
    /// </summary>
    public static void NotifyUnauthorized() => _ = ConfirmUnauthorizedAsync();

    private static async Task ConfirmUnauthorizedAsync()
    {
        var settings = App.SipService?.CurrentSettings;
        if (settings == null) return;

        if (string.IsNullOrEmpty(settings.RefreshToken))
        {
            // Nothing left to recover the session with.
            AppLogger.Log("BackendAuth", "Backend returned 401 and there is no refresh token. Session is over; re-login required.");
            EndSession(settings);
            return;
        }

        // One confirmation at a time — a dead token 401s every poller at once.
        if (Interlocked.Exchange(ref _confirmingUnauthorized, 1) != 0) return;

        try
        {
            await RefreshGate.WaitAsync().ConfigureAwait(false);
            try
            {
                AppLogger.Log("BackendAuth", "Backend returned 401. Forcing a refresh to find out whether the session is really over.");
                var token = await RefreshAsync(settings).ConfigureAwait(false);

                if (token != null)
                    AppLogger.Log("BackendAuth", "Refresh succeeded — the 401 was a stale token, not a dead session.");
            }
            finally
            {
                RefreshGate.Release();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log("BackendAuth", $"Confirming the 401 threw ({ex.GetType().Name}: {ex.Message}). Leaving the session alone.");
        }
        finally
        {
            Interlocked.Exchange(ref _confirmingUnauthorized, 0);
        }
    }

    private static bool IsSpent(JwtPayload? payload) =>
        Models.AccessTokenLifetime.IsSpent(payload?.Exp);

    /// <summary>
    /// Spends the refresh token for a new pair. Call only while holding
    /// <see cref="RefreshGate"/>.
    /// </summary>
    private static async Task<string?> RefreshAsync(SipSettings settings)
    {
        _lastRefreshAttempt = DateTimeOffset.UtcNow;

        var backendUrl = settings.BackendUrl?.TrimEnd('/');
        if (string.IsNullOrEmpty(backendUrl))
            return null;

        // The refresh runs on its own deadline, never the calling request's. Refreshing
        // rotates the token on the backend, so a caller that times out or gets cancelled
        // mid-flight would abandon a rotation the server has already committed — leaving
        // this client holding a refresh token that no longer works and killing the session
        // for every other caller, over one unrelated cancellation.
        using var deadline = new CancellationTokenSource(RefreshTimeout);

        try
        {
            AppLogger.Log("BackendAuth", "Refreshing the access token.");

            // RawClient, not Client: going out through the refresh handler would have
            // this call ask itself for a fresh token.
            using var response = await BackendHttp.RawClient
                .PostAsJsonAsync($"{backendUrl}/api/auth/refresh",
                                 new RefreshRequest { RefreshToken = settings.RefreshToken },
                                 deadline.Token)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                // The backend answers 401 for an expired, revoked or already-rotated
                // refresh token, and for an account that has since been disabled. None of
                // those are retryable, and this is the only place that ends the session.
                AppLogger.Log("BackendAuth", "Refresh token rejected (401). Session is over; re-login required.");
                EndSession(settings);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                // A transient failure — backend restarting, network down. The token is
                // left alone so a later request tries again rather than logging the
                // operator out over a blip.
                AppLogger.Log("BackendAuth", $"Refresh failed with HTTP {(int)response.StatusCode}. Leaving the session alone; will retry.");
                return null;
            }

            var tokens = await response.Content
                .ReadFromJsonAsync<RefreshResponse>(cancellationToken: deadline.Token)
                .ConfigureAwait(false);

            if (tokens == null || string.IsNullOrEmpty(tokens.AccessToken))
            {
                AppLogger.Log("BackendAuth", "Refresh returned no access token.");
                return null;
            }

            // The rotated refresh token is stored FIRST: the moment the backend answered,
            // the one we sent is spent, so losing the new one here would make this the
            // last successful refresh of the session.
            if (!string.IsNullOrEmpty(tokens.RefreshToken))
                settings.RefreshToken = tokens.RefreshToken;

            settings.AccessToken  = tokens.AccessToken;
            settings.DecodedToken = JwtDecoder.Decode(tokens.AccessToken);

            AppLogger.Log("BackendAuth", "Access token refreshed.");
            return tokens.AccessToken;
        }
        catch (Exception ex)
        {
            // Same reasoning as the non-2xx branch: a network fault is not a logout.
            AppLogger.Log("BackendAuth", $"Refresh threw ({ex.GetType().Name}: {ex.Message}). Leaving the session alone; will retry.");
            return null;
        }
    }

    private static void EndSession(SipSettings settings)
    {
        settings.AccessToken  = "";
        settings.RefreshToken = "";
        settings.DecodedToken = null;

        if (Interlocked.Exchange(ref _sessionEndAnnounced, 1) == 0)
            SessionExpired?.Invoke();
    }

    private sealed class RefreshRequest
    {
        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; } = "";
    }

    private sealed class RefreshResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = "";

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; } = "";
    }
}
