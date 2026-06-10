using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using OrbitalSIP.Models;

namespace OrbitalSIP.Services
{
    public class FlowsService : IDisposable
    {
        private readonly HttpClient _httpClient;

        public FlowsService()
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };
            _httpClient = new HttpClient(handler);
        }

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

        public async Task<List<FlowDefinition>> ListFlowsAsync()
        {
            try
            {
                var cfg = GetSettings();
                if (cfg == null) return new List<FlowDefinition>();

                var url = $"{cfg.Value.backendUrl}/api/flows";
                using var request = AuthRequest(HttpMethod.Get, url);
                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<List<FlowDefinition>>(content) ?? new List<FlowDefinition>();
                }

                var errorBody = await response.Content.ReadAsStringAsync();
                AppLogger.Log("FlowsService", $"ListFlows failed. Status: {response.StatusCode}. Body: {errorBody}");
                HttpErrorNotifier.NotifyHttpError("FlowsService", url, response.StatusCode, errorBody);
            }
            catch (Exception ex)
            {
                AppLogger.Log("FlowsService", $"ListFlows error: {ex.GetType().Name}: {ex.Message}");
                HttpErrorNotifier.NotifyException("FlowsService", ex);
            }
            return new List<FlowDefinition>();
        }

        public async Task<List<FlowRun>> ListRunsAsync(string subjectId)
        {
            try
            {
                var cfg = GetSettings();
                if (cfg == null) return new List<FlowRun>();

                var url = $"{cfg.Value.backendUrl}/api/flow-runs?subjectType=call&subjectId={Uri.EscapeDataString(subjectId)}";
                using var request = AuthRequest(HttpMethod.Get, url);
                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<List<FlowRun>>(content) ?? new List<FlowRun>();
                }

                var errorBody = await response.Content.ReadAsStringAsync();
                AppLogger.Log("FlowsService", $"ListRuns failed. Status: {response.StatusCode}. Body: {errorBody}");
            }
            catch (Exception ex)
            {
                AppLogger.Log("FlowsService", $"ListRuns error: {ex.GetType().Name}: {ex.Message}");
            }
            return new List<FlowRun>();
        }

        public async Task<StartRunResponse?> StartRunAsync(string flowId, string subjectId, string? contactId = null)
        {
            try
            {
                var cfg = GetSettings();
                if (cfg == null) return null;

                var url = $"{cfg.Value.backendUrl}/api/flows/{Uri.EscapeDataString(flowId)}/runs";
                using var request = AuthRequest(HttpMethod.Post, url);

                var body = new { subjectType = "call", subjectId, contactId };
                request.Content = JsonContent.Create(body);

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<StartRunResponse>(content);
                }

                var errorBody = await response.Content.ReadAsStringAsync();
                AppLogger.Log("FlowsService", $"StartRun failed. Status: {response.StatusCode}. Body: {errorBody}");
                HttpErrorNotifier.NotifyHttpError("FlowsService", url, response.StatusCode, errorBody);
            }
            catch (Exception ex)
            {
                AppLogger.Log("FlowsService", $"StartRun error: {ex.GetType().Name}: {ex.Message}");
                HttpErrorNotifier.NotifyException("FlowsService", ex);
            }
            return null;
        }

        /// <returns>null means 409 — caller should reload state via GetRunStateAsync</returns>
        public async Task<AnswerResponse?> AnswerAsync(string runId, string nodeKey, string? value, string? comment = null)
        {
            try
            {
                var cfg = GetSettings();
                if (cfg == null) return null;

                var url = $"{cfg.Value.backendUrl}/api/flow-runs/{Uri.EscapeDataString(runId)}/answers";
                using var request = AuthRequest(HttpMethod.Post, url);

                var body = new { nodeKey, value, comment };
                request.Content = JsonContent.Create(body);

                var response = await _httpClient.SendAsync(request);

                if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
                {
                    AppLogger.Log("FlowsService", "Answer 409 conflict — caller should reload state");
                    return null; // marker: caller reloads via GetRunStateAsync
                }

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<AnswerResponse>(content);
                }

                var errorBody = await response.Content.ReadAsStringAsync();
                AppLogger.Log("FlowsService", $"Answer failed. Status: {response.StatusCode}. Body: {errorBody}");
                HttpErrorNotifier.NotifyHttpError("FlowsService", url, response.StatusCode, errorBody);
            }
            catch (Exception ex)
            {
                AppLogger.Log("FlowsService", $"Answer error: {ex.GetType().Name}: {ex.Message}");
                HttpErrorNotifier.NotifyException("FlowsService", ex);
            }
            return null;
        }

        public async Task<string?> BackAsync(string runId)
        {
            try
            {
                var cfg = GetSettings();
                if (cfg == null) return null;

                var url = $"{cfg.Value.backendUrl}/api/flow-runs/{Uri.EscapeDataString(runId)}/back";
                using var request = AuthRequest(HttpMethod.Post, url);
                request.Content = JsonContent.Create(new { });

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(content);
                    if (doc.RootElement.TryGetProperty("currentNodeKey", out var el))
                        return el.GetString();
                }

                var errorBody = await response.Content.ReadAsStringAsync();
                AppLogger.Log("FlowsService", $"Back failed. Status: {response.StatusCode}. Body: {errorBody}");
            }
            catch (Exception ex)
            {
                AppLogger.Log("FlowsService", $"Back error: {ex.GetType().Name}: {ex.Message}");
            }
            return null;
        }

        public async Task<bool> AbandonAsync(string runId, string? reason = null)
        {
            try
            {
                var cfg = GetSettings();
                if (cfg == null) return false;

                var url = $"{cfg.Value.backendUrl}/api/flow-runs/{Uri.EscapeDataString(runId)}/abandon";
                using var request = AuthRequest(HttpMethod.Post, url);
                request.Content = JsonContent.Create(new { reason });

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                    return true;

                var errorBody = await response.Content.ReadAsStringAsync();
                AppLogger.Log("FlowsService", $"Abandon failed. Status: {response.StatusCode}. Body: {errorBody}");
            }
            catch (Exception ex)
            {
                AppLogger.Log("FlowsService", $"Abandon error: {ex.GetType().Name}: {ex.Message}");
            }
            return false;
        }

        public async Task<RunStateResponse?> GetRunStateAsync(string runId)
        {
            try
            {
                var cfg = GetSettings();
                if (cfg == null) return null;

                var url = $"{cfg.Value.backendUrl}/api/flow-runs/{Uri.EscapeDataString(runId)}";
                using var request = AuthRequest(HttpMethod.Get, url);

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<RunStateResponse>(content);
                }

                var errorBody = await response.Content.ReadAsStringAsync();
                AppLogger.Log("FlowsService", $"GetRunState failed. Status: {response.StatusCode}. Body: {errorBody}");
            }
            catch (Exception ex)
            {
                AppLogger.Log("FlowsService", $"GetRunState error: {ex.GetType().Name}: {ex.Message}");
            }
            return null;
        }

        public async Task<string?> GetChannelUniqueIdAsync(string phoneNumber)
        {
            try
            {
                var cfg = GetSettings();
                if (cfg == null) return null;

                var url = $"{cfg.Value.backendUrl}/api/cdr/channel-uniqueid?callerNumber={Uri.EscapeDataString(phoneNumber)}";
                using var request = AuthRequest(HttpMethod.Get, url);

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(content);
                    if (doc.RootElement.TryGetProperty("uniqueid", out var el))
                        return el.GetString();
                }

                var errorBody = await response.Content.ReadAsStringAsync();
                AppLogger.Log("FlowsService", $"GetChannelUniqueId failed. Status: {response.StatusCode}. Body: {errorBody}");
            }
            catch (Exception ex)
            {
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
