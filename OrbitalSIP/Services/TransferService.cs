using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OrbitalSIP.Models;

namespace OrbitalSIP.Services
{
    /// <summary>
    /// The answer to a transfer-targets load. <c>Forbidden</c> is kept apart from
    /// a plain <c>Error</c> so the caller can tell "the backend refuses to answer"
    /// (403 for a missing permission, 404 for a backend build old enough to lack
    /// the endpoint) from "the request failed" — the former can only be resolved
    /// by falling back to manual dialing, and retrying it changes nothing.
    /// </summary>
    public sealed record TransferTargetsResponse(TransferTargets? Targets, bool Forbidden, string? Error);

    /// <summary>
    /// Moves an in-progress call by asking the backend, instead of sending a SIP
    /// REFER directly. A queue name is not a dialable number in any dialplan
    /// context, so a REFER aimed at one goes nowhere — only the backend's
    /// per-organization context can hand it to <c>Queue()</c>. The backend is
    /// also the only side able to check the chosen target belongs to the
    /// caller's organization, so both target kinds go through it, not just
    /// queues.
    /// </summary>
    public class TransferService : IDisposable
    {
        /// <summary>
        /// The transfer panel opens over a live call, so HttpClient's 100-second
        /// default would read to the operator as a frozen softphone. Ten seconds
        /// is enough for the backend to answer and short enough that an
        /// unreachable backend surfaces as an error instead.
        /// </summary>
        public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

        private readonly HttpClient _httpClient;
        private readonly Func<SipSettings>? _settingsProvider;
        private readonly bool _ownsHttpClient;

        public TransferService()
        {
            // A client of its own, over BackendHttp's shared connection pool: this
            // is what lets RequestTimeout apply without shortening the timeout on
            // BackendHttp.Client (shared by every other service) and without
            // opening a second set of sockets to the same host. Owning this
            // instance is safe to dispose — BackendHttp.CreateClient hands back a
            // fresh HttpClient whose Dispose releases only that instance, not the
            // shared handler underneath it.
            _httpClient = BackendHttp.CreateClient(RequestTimeout);
            _ownsHttpClient = true;
        }

        public TransferService(HttpClient httpClient, Func<SipSettings> settingsProvider, bool ownsHttpClient = false)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
            _ownsHttpClient = ownsHttpClient;
        }

        /// <summary>
        /// The operators and queues the current operator may transfer to.
        /// <paramref name="cancellationToken"/> aside, 403 and 404 both come back
        /// as <see cref="TransferTargetsResponse.Forbidden"/>: 403 means the role
        /// lacks the permission, 404 means the backend build predates the
        /// endpoint, and retrying fixes neither — the caller should fall back to
        /// manual dialing rather than offer a retry button.
        /// </summary>
        public async Task<TransferTargetsResponse> GetTargetsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var settings = _settingsProvider?.Invoke() ?? App.SipService?.CurrentSettings ?? SipSettings.Load();
                var backendUrl = settings.BackendUrl?.TrimEnd('/');

                if (string.IsNullOrEmpty(backendUrl) || string.IsNullOrEmpty(settings.AccessToken))
                    return new TransferTargetsResponse(null, Forbidden: false, Error: "not-configured");

                var url = $"{backendUrl}/api/calls/transfer-targets";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.AccessToken);

                using var response = await _httpClient.SendAsync(request, cancellationToken);

                // 403 and 404 both mean "this widget gets no list, and nothing it
                // does changes that": 403 is a missing permission, 404 is a
                // backend build old enough to not have the endpoint yet. Neither
                // is fixed by asking again, so both map to Forbidden rather than
                // the retryable Error state.
                if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
                    return new TransferTargetsResponse(null, Forbidden: true, Error: null);

                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    AppLogger.Log("TransferService", $"GetTargets failed. Status: {response.StatusCode}. Body: {body}");
                    return new TransferTargetsResponse(null, Forbidden: false, Error: response.StatusCode.ToString());
                }

                var targets = ParseTargets(body);
                if (targets == null)
                {
                    AppLogger.Log("TransferService", $"GetTargets: unreadable response body: {body}");
                    return new TransferTargetsResponse(null, Forbidden: false, Error: "unreadable-response");
                }

                return new TransferTargetsResponse(targets, Forbidden: false, Error: null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLogger.Log("TransferService", $"GetTargets error: {ex.GetType().Name}: {ex.Message}");
                return new TransferTargetsResponse(null, Forbidden: false, Error: ex.Message);
            }
        }

        /// <summary>
        /// Moves the call: resolves the live Asterisk channel id for the other
        /// party's number, then asks the backend to redirect that channel.
        ///
        /// Every failure lands in one of two buckets, and the split is
        /// deliberate: "could not ask" — no backend is configured, the
        /// channel lookup timed out, its connection failed, or its response
        /// was broken or unreadable — reports
        /// <see cref="TransferOutcome.ChannelUnresolved"/>, the one outcome
        /// that unlocks the SIP REFER fallback for an extension target (see
        /// <c>TransferOutcome</c> in TransferModels.cs). "Was told no" — a
        /// non-2xx from the transfer POST itself, or a 200 carrying
        /// <c>{ok:false}</c> — reports <see cref="TransferOutcome.Failed"/>
        /// instead, because the backend actively answered that request and a
        /// REFER sent behind its back could race a transfer it already
        /// refused, or one it already performed.
        /// </summary>
        public async Task<TransferResult> TransferAsync(
            TransferTargetKind kind,
            string target,
            string callerNumber,
            CancellationToken cancellationToken = default)
        {
            // One budget for the whole sequence, not one per request: two
            // independent RequestTimeout windows back to back could leave a
            // live call silent for twenty-plus seconds. Linked so the
            // caller's own token (the transfer panel closing) still cancels
            // immediately, on top of our own deadline.
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(RequestTimeout);
            var ct = deadline.Token;

            try
            {
                ct.ThrowIfCancellationRequested();
                var settings = _settingsProvider?.Invoke() ?? App.SipService?.CurrentSettings ?? SipSettings.Load();
                var backendUrl = settings.BackendUrl?.TrimEnd('/');

                if (string.IsNullOrEmpty(backendUrl) || string.IsNullOrEmpty(settings.AccessToken))
                {
                    // No backend to ask at all — REFER needs no backend, so
                    // this is squarely "could not ask", not a refusal.
                    AppLogger.Log("TransferService", "Transfer skipped: no backend configured.");
                    return new TransferResult(TransferOutcome.ChannelUnresolved, null);
                }

                var channelId = await ResolveChannelIdAsync(backendUrl, settings.AccessToken, callerNumber, ct);
                if (string.IsNullOrWhiteSpace(channelId))
                    return new TransferResult(TransferOutcome.ChannelUnresolved, null);

                var url = $"{backendUrl}/api/calls/transfer";
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.AccessToken);
                request.Content = JsonContent.Create(new
                {
                    channelId,
                    target,
                    targetKind = WireTargetKind(kind),
                    type = "blind",
                });

                using var response = await _httpClient.SendAsync(request, ct);
                var body = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    // The backend answered, just not with success — asked and
                    // something broke on its side. That is "was told no" in
                    // spirit, not "could not ask", so this stays Failed
                    // rather than triggering a REFER that might race
                    // whatever the backend did before it errored.
                    AppLogger.Log("TransferService", $"Transfer failed. Status: {response.StatusCode}. Body: {body}");
                    return new TransferResult(TransferOutcome.Failed, response.StatusCode.ToString());
                }

                // A refusal arrives as HTTP 200 with {ok:false} — the status code
                // alone cannot tell success from a refusal, so it always goes
                // through the body parser rather than trusting IsSuccessStatusCode.
                return ParseTransferResult(body);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The caller pulled the plug (e.g. the panel closed) — propagate
                // so the awaiting code sees a real cancellation, not a fabricated result.
                throw;
            }
            catch (OperationCanceledException)
            {
                // cancellationToken (the caller's) is not cancelled here, so only
                // our own deadline could have fired this: the backend did not
                // answer inside RequestTimeout. That is "could not ask", the same
                // bucket as every other way the channel lookup can come up empty.
                AppLogger.Log("TransferService", $"Transfer timed out after {RequestTimeout}.");
                return new TransferResult(TransferOutcome.ChannelUnresolved, null);
            }
            catch (Exception ex)
            {
                AppLogger.Log("TransferService", $"Transfer error: {ex.GetType().Name}: {ex.Message}");
                return new TransferResult(TransferOutcome.Failed, ex.Message);
            }
        }

        private static string WireTargetKind(TransferTargetKind kind) => kind switch
        {
            TransferTargetKind.Extension => "extension",
            TransferTargetKind.Queue => "queue",
            // A third kind must not fall silently into "extension" and
            // transfer to the wrong sort of place — fail loudly here instead.
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown transfer target kind."),
        };

        /// <summary>
        /// Resolves the live Asterisk channel id for the other party's number.
        /// Null on anything short of a clean 2xx carrying a <c>uniqueid</c>
        /// string — <see cref="TransferAsync"/> treats null as
        /// ChannelUnresolved and never posts the transfer. That covers a
        /// non-2xx status, a body that isn't JSON, a <c>uniqueid</c> sent as
        /// something other than a string, and a thrown network exception:
        /// this lookup is read-only, so none of those leave any doubt about
        /// whether a transfer was attempted — there is simply no channel id
        /// to post with, and "could not ask" is correct for all of them.
        /// </summary>
        private async Task<string?> ResolveChannelIdAsync(
            string backendUrl,
            string accessToken,
            string callerNumber,
            CancellationToken cancellationToken)
        {
            string? body = null;
            try
            {
                var url = $"{backendUrl}/api/cdr/channel-uniqueid?callerNumber={Uri.EscapeDataString(callerNumber)}";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    AppLogger.Log("TransferService", $"Resolve channel id failed. Status: {response.StatusCode}. Body: {body}");
                    return null;
                }

                using var document = JsonDocument.Parse(body);
                if (document.RootElement.TryGetProperty("uniqueid", out var uniqueIdElement) &&
                    uniqueIdElement.ValueKind == JsonValueKind.String)
                {
                    var uniqueId = uniqueIdElement.GetString();
                    return string.IsNullOrWhiteSpace(uniqueId) ? null : uniqueId;
                }

                return null;
            }
            catch (OperationCanceledException)
            {
                // Could be the caller's token or our shared deadline — this
                // method has no way to tell them apart, so it leaves that
                // decision to TransferAsync's own catch clauses.
                throw;
            }
            catch (JsonException ex)
            {
                AppLogger.Log("TransferService", $"Resolve channel id: unreadable response. {ex.Message}. Body: {body}");
                return null;
            }
            catch (Exception ex)
            {
                // A thrown network exception (DNS, connection refused, TLS...) —
                // the lookup could not be completed, exactly as "could not
                // ask" as a 5xx or a malformed body.
                AppLogger.Log("TransferService", $"Resolve channel id error: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>Null means "could not read the response" — never "no targets".</summary>
        public static TransferTargets? ParseTargets(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<TransferTargets>(json);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// The backend answers a refusal as HTTP 200 with <c>{ok:false,error}</c>
        /// — the status code alone cannot separate that from success, so every
        /// caller runs the body through this instead of trusting a 2xx.
        /// </summary>
        public static TransferResult ParseTransferResult(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                var ok = document.RootElement.TryGetProperty("ok", out var okElement)
                    && okElement.ValueKind == JsonValueKind.True;

                if (ok)
                    return new TransferResult(TransferOutcome.Ok, null);

                var error = document.RootElement.TryGetProperty("error", out var errorElement)
                    ? errorElement.GetString()
                    : null;
                return new TransferResult(TransferOutcome.Failed, error);
            }
            catch (JsonException)
            {
                return new TransferResult(TransferOutcome.Failed, null);
            }
        }

        public void Dispose()
        {
            if (_ownsHttpClient)
                _httpClient.Dispose();
        }
    }
}
