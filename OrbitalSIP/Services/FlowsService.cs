using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OrbitalSIP.Models;

namespace OrbitalSIP.Services
{
    public class FlowsService : IDisposable
    {
        // Default HttpClient.Timeout is 100s. The survey window sits over the
        // active call for its whole duration, so a slow backend used to read to
        // the operator as a frozen softphone — keep it short enough to surface
        // as an error instead.
        public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

        private readonly HttpClient _httpClient;

        public FlowsService()
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };
            _httpClient = new HttpClient(handler) { Timeout = RequestTimeout };
        }

        /// <summary>Test-only seam confirming the client actually carries RequestTimeout.</summary>
        public TimeSpan HttpClientTimeoutForTests => _httpClient.Timeout;

        /// <summary>
        /// True when the caller pulled the plug — the survey window closed — rather
        /// than the request timing out. Nothing to log or bannerise in that case.
        /// </summary>
        private static bool IsCallerCancellation(Exception ex, CancellationToken ct) =>
            ex is OperationCanceledException && ct.IsCancellationRequested;

        private (string backendUrl, string accessToken)? GetSettings()
        {
            var settings = App.SipService?.CurrentSettings ?? SipSettings.Load();
            var backendUrl = settings.BackendUrl?.TrimEnd('/');
            if (string.IsNullOrEmpty(backendUrl) || string.IsNullOrEmpty(settings.AccessToken))
                return null;
            return (backendUrl, settings.AccessToken);
        }

        private HttpRequestMessage AuthRequest(HttpMethod method, string url)
        {
            var req = new HttpRequestMessage(method, url);
            var settings = App.SipService?.CurrentSettings ?? SipSettings.Load();
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.AccessToken);
            return req;
        }

        public async Task<List<FlowDefinition>> ListFlowsAsync(CancellationToken ct = default)
        {
            try
            {
                var cfg = GetSettings();
                if (cfg == null) return new List<FlowDefinition>();

                var url = $"{cfg.Value.backendUrl}/api/flows";
                using var request = AuthRequest(HttpMethod.Get, url);
                var response = await _httpClient.SendAsync(request, ct);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(ct);
                    return JsonSerializer.Deserialize<List<FlowDefinition>>(content) ?? new List<FlowDefinition>();
                }

                var errorBody = await response.Content.ReadAsStringAsync(ct);
                AppLogger.Log("FlowsService", $"ListFlows failed. Status: {response.StatusCode}. Body: {errorBody}");
                HttpErrorNotifier.NotifyHttpError("FlowsService", url, response.StatusCode, errorBody);
            }
            catch (Exception ex)
            {
                if (IsCallerCancellation(ex, ct)) return new List<FlowDefinition>();
                AppLogger.Log("FlowsService", $"ListFlows error: {ex.GetType().Name}: {ex.Message}");
                HttpErrorNotifier.NotifyException("FlowsService", ex);
            }
            return new List<FlowDefinition>();
        }

        /// <summary>
        /// Flows bound to the campaign of an in-progress call to <paramref name="number"/>
        /// (the other party's phone). Used to auto-open the bound questionnaire when a
        /// campaign call is answered. Returns an empty list when nothing is bound.
        /// </summary>
        public async Task<List<FlowDefinition>> SuggestForNumberAsync(string number, CancellationToken ct = default)
        {
            try
            {
                var cfg = GetSettings();
                if (cfg == null || string.IsNullOrWhiteSpace(number)) return new List<FlowDefinition>();

                var url = $"{cfg.Value.backendUrl}/api/flows/suggest-for-number?number={Uri.EscapeDataString(number)}";
                using var request = AuthRequest(HttpMethod.Get, url);
                var response = await _httpClient.SendAsync(request, ct);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(ct);
                    return JsonSerializer.Deserialize<List<FlowDefinition>>(content) ?? new List<FlowDefinition>();
                }

                var errorBody = await response.Content.ReadAsStringAsync(ct);
                AppLogger.Log("FlowsService", $"SuggestForNumber failed. Status: {response.StatusCode}. Body: {errorBody}");
            }
            catch (Exception ex)
            {
                if (IsCallerCancellation(ex, ct)) return new List<FlowDefinition>();
                AppLogger.Log("FlowsService", $"SuggestForNumber error: {ex.GetType().Name}: {ex.Message}");
            }
            return new List<FlowDefinition>();
        }

        public async Task<List<FlowRun>> ListRunsAsync(string subjectId, CancellationToken ct = default)
        {
            try
            {
                var cfg = GetSettings();
                if (cfg == null) return new List<FlowRun>();

                var url = $"{cfg.Value.backendUrl}/api/flow-runs?subjectType=call&subjectId={Uri.EscapeDataString(subjectId)}";
                using var request = AuthRequest(HttpMethod.Get, url);
                var response = await _httpClient.SendAsync(request, ct);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(ct);
                    return JsonSerializer.Deserialize<List<FlowRun>>(content) ?? new List<FlowRun>();
                }

                var errorBody = await response.Content.ReadAsStringAsync(ct);
                AppLogger.Log("FlowsService", $"ListRuns failed. Status: {response.StatusCode}. Body: {errorBody}");
            }
            catch (Exception ex)
            {
                if (IsCallerCancellation(ex, ct)) return new List<FlowRun>();
                AppLogger.Log("FlowsService", $"ListRuns error: {ex.GetType().Name}: {ex.Message}");
            }
            return new List<FlowRun>();
        }

        public async Task<StartRunResponse?> StartRunAsync(string flowId, string subjectId, string? contactId = null, string? phone = null, CancellationToken ct = default)
        {
            try
            {
                var cfg = GetSettings();
                if (cfg == null) return null;

                var url = $"{cfg.Value.backendUrl}/api/flows/{Uri.EscapeDataString(flowId)}/runs";
                using var request = AuthRequest(HttpMethod.Post, url);

                // phone lets the backend resolve a contact (and populate {{contact.*}}
                // template vars) when contactId is unknown - true for campaign calls,
                // where we only ever have the dialed number, not a CRM contactId.
                var body = new { subjectType = "call", subjectId, contactId, phone };
                request.Content = JsonContent.Create(body);

                var response = await _httpClient.SendAsync(request, ct);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(ct);
                    return JsonSerializer.Deserialize<StartRunResponse>(content);
                }

                var errorBody = await response.Content.ReadAsStringAsync(ct);
                AppLogger.Log("FlowsService", $"StartRun failed. Status: {response.StatusCode}. Body: {errorBody}");
                HttpErrorNotifier.NotifyHttpError("FlowsService", url, response.StatusCode, errorBody);
            }
            catch (Exception ex)
            {
                if (IsCallerCancellation(ex, ct)) return null;
                AppLogger.Log("FlowsService", $"StartRun error: {ex.GetType().Name}: {ex.Message}");
                HttpErrorNotifier.NotifyException("FlowsService", ex);
            }
            return null;
        }

        /// <returns>null means 409 — caller should reload state via GetRunStateAsync</returns>
        public async Task<AnswerResponse?> AnswerAsync(string runId, string nodeKey, string? value, string? comment = null, CancellationToken ct = default)
        {
            try
            {
                var cfg = GetSettings();
                if (cfg == null) return null;

                var url = $"{cfg.Value.backendUrl}/api/flow-runs/{Uri.EscapeDataString(runId)}/answers";
                using var request = AuthRequest(HttpMethod.Post, url);

                var body = new { nodeKey, value, comment };
                request.Content = JsonContent.Create(body);

                var response = await _httpClient.SendAsync(request, ct);

                if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
                {
                    AppLogger.Log("FlowsService", "Answer 409 conflict — caller should reload state");
                    return null; // marker: caller reloads via GetRunStateAsync
                }

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(ct);
                    return JsonSerializer.Deserialize<AnswerResponse>(content);
                }

                var errorBody = await response.Content.ReadAsStringAsync(ct);
                AppLogger.Log("FlowsService", $"Answer failed. Status: {response.StatusCode}. Body: {errorBody}");
                HttpErrorNotifier.NotifyHttpError("FlowsService", url, response.StatusCode, errorBody);
            }
            catch (Exception ex)
            {
                if (IsCallerCancellation(ex, ct)) return null;
                AppLogger.Log("FlowsService", $"Answer error: {ex.GetType().Name}: {ex.Message}");
                HttpErrorNotifier.NotifyException("FlowsService", ex);
            }
            return null;
        }

        public async Task<string?> BackAsync(string runId, CancellationToken ct = default)
        {
            try
            {
                var cfg = GetSettings();
                if (cfg == null) return null;

                var url = $"{cfg.Value.backendUrl}/api/flow-runs/{Uri.EscapeDataString(runId)}/back";
                using var request = AuthRequest(HttpMethod.Post, url);
                request.Content = JsonContent.Create(new { });

                var response = await _httpClient.SendAsync(request, ct);
                var body = await response.Content.ReadAsStringAsync(ct);
                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("currentNodeKey", out var el))
                        return el.GetString();
                    AppLogger.Log("FlowsService", "Back: 2xx but no currentNodeKey");
                    return null;
                }

                AppLogger.Log("FlowsService", $"Back failed. Status: {response.StatusCode}. Body: {body}");
            }
            catch (Exception ex)
            {
                if (IsCallerCancellation(ex, ct)) return null;
                AppLogger.Log("FlowsService", $"Back error: {ex.GetType().Name}: {ex.Message}");
            }
            return null;
        }

        public async Task<bool> AbandonAsync(string runId, string? reason = null, CancellationToken ct = default)
        {
            try
            {
                var cfg = GetSettings();
                if (cfg == null) return false;

                var url = $"{cfg.Value.backendUrl}/api/flow-runs/{Uri.EscapeDataString(runId)}/abandon";
                using var request = AuthRequest(HttpMethod.Post, url);
                request.Content = JsonContent.Create(new { reason });

                var response = await _httpClient.SendAsync(request, ct);
                if (response.IsSuccessStatusCode)
                    return true;

                var errorBody = await response.Content.ReadAsStringAsync(ct);
                AppLogger.Log("FlowsService", $"Abandon failed. Status: {response.StatusCode}. Body: {errorBody}");
            }
            catch (Exception ex)
            {
                if (IsCallerCancellation(ex, ct)) return false;
                AppLogger.Log("FlowsService", $"Abandon error: {ex.GetType().Name}: {ex.Message}");
            }
            return false;
        }

        public async Task<RunStateResponse?> GetRunStateAsync(string runId, CancellationToken ct = default)
        {
            try
            {
                var cfg = GetSettings();
                if (cfg == null) return null;

                var url = $"{cfg.Value.backendUrl}/api/flow-runs/{Uri.EscapeDataString(runId)}";
                using var request = AuthRequest(HttpMethod.Get, url);

                var response = await _httpClient.SendAsync(request, ct);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(ct);
                    return JsonSerializer.Deserialize<RunStateResponse>(content);
                }

                var errorBody = await response.Content.ReadAsStringAsync(ct);
                AppLogger.Log("FlowsService", $"GetRunState failed. Status: {response.StatusCode}. Body: {errorBody}");
            }
            catch (Exception ex)
            {
                if (IsCallerCancellation(ex, ct)) return null;
                AppLogger.Log("FlowsService", $"GetRunState error: {ex.GetType().Name}: {ex.Message}");
            }
            return null;
        }

        public async Task<string?> GetChannelUniqueIdAsync(string phoneNumber, CancellationToken ct = default)
        {
            try
            {
                var cfg = GetSettings();
                if (cfg == null) return null;

                var url = $"{cfg.Value.backendUrl}/api/cdr/channel-uniqueid?callerNumber={Uri.EscapeDataString(phoneNumber)}";
                using var request = AuthRequest(HttpMethod.Get, url);

                var response = await _httpClient.SendAsync(request, ct);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(content);
                    if (doc.RootElement.TryGetProperty("uniqueid", out var el))
                        return el.GetString();
                }

                var errorBody = await response.Content.ReadAsStringAsync(ct);
                AppLogger.Log("FlowsService", $"GetChannelUniqueId failed. Status: {response.StatusCode}. Body: {errorBody}");
            }
            catch (Exception ex)
            {
                if (IsCallerCancellation(ex, ct)) return null;
                AppLogger.Log("FlowsService", $"GetChannelUniqueId error: {ex.GetType().Name}: {ex.Message}");
            }
            return null;
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
