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
        /// Returns <see cref="TransferOutcome.ChannelUnresolved"/> without
        /// posting anything when the channel can't be found — the backend would
        /// have nothing to redirect, and posting anyway would only earn a
        /// refusal the operator cannot act on. A caller sees ChannelUnresolved
        /// as the signal to fall back to a SIP REFER for an extension target.
        /// </summary>
        public async Task<TransferResult> TransferAsync(
            TransferTargetKind kind,
            string target,
            string callerNumber,
            CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var settings = _settingsProvider?.Invoke() ?? App.SipService?.CurrentSettings ?? SipSettings.Load();
                var backendUrl = settings.BackendUrl?.TrimEnd('/');

                if (string.IsNullOrEmpty(backendUrl) || string.IsNullOrEmpty(settings.AccessToken))
                    return new TransferResult(TransferOutcome.Failed, "not-configured");

                var channelId = await ResolveChannelIdAsync(backendUrl, settings.AccessToken, callerNumber, cancellationToken);
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

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
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
                throw;
            }
            catch (Exception ex)
            {
                AppLogger.Log("TransferService", $"Transfer error: {ex.GetType().Name}: {ex.Message}");
                return new TransferResult(TransferOutcome.Failed, ex.Message);
            }
        }

        private static string WireTargetKind(TransferTargetKind kind) => kind switch
        {
            TransferTargetKind.Queue => "queue",
            _ => "extension",
        };

        /// <summary>
        /// Resolves the live Asterisk channel id for the other party's number.
        /// Null on anything short of a clean 2xx carrying a non-empty
        /// <c>uniqueid</c> — <see cref="TransferAsync"/> treats null as
        /// ChannelUnresolved and never posts the transfer.
        /// </summary>
        private async Task<string?> ResolveChannelIdAsync(
            string backendUrl,
            string accessToken,
            string callerNumber,
            CancellationToken cancellationToken)
        {
            var url = $"{backendUrl}/api/cdr/channel-uniqueid?callerNumber={Uri.EscapeDataString(callerNumber)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                AppLogger.Log("TransferService", $"Resolve channel id failed. Status: {response.StatusCode}. Body: {body}");
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.TryGetProperty("uniqueid", out var uniqueIdElement))
                {
                    var uniqueId = uniqueIdElement.GetString();
                    return string.IsNullOrWhiteSpace(uniqueId) ? null : uniqueId;
                }
            }
            catch (JsonException ex)
            {
                AppLogger.Log("TransferService", $"Resolve channel id: unreadable response. {ex.Message}. Body: {body}");
            }

            return null;
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
