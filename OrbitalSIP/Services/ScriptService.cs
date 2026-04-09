using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using OrbitalSIP.Models;

namespace OrbitalSIP.Services
{
    public class ScriptService : IDisposable
    {
        private readonly HttpClient _httpClient;

        public ScriptService()
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };
            _httpClient = new HttpClient(handler);
        }

        public async Task<List<CallScript>> GetScriptsAsync()
        {
            try
            {
                var settings = App.SipService?.CurrentSettings ?? SipSettings.Load();
                var backendUrl = settings.BackendUrl?.TrimEnd('/');

                if (string.IsNullOrEmpty(backendUrl) || string.IsNullOrEmpty(settings.AccessToken))
                    return new List<CallScript>();

                var url = $"{backendUrl}/api/call-scripts?tree=true&active=true";

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.AccessToken);

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var scripts = JsonSerializer.Deserialize<List<CallScript>>(content);
                    return scripts ?? new List<CallScript>();
                }

                var errorBody = await response.Content.ReadAsStringAsync();
                AppLogger.Log("ScriptService", $"Fetch scripts failed. Status: {response.StatusCode}. Body: {errorBody}");
                HttpErrorNotifier.NotifyHttpError("ScriptService", url, response.StatusCode, errorBody);
            }
            catch (Exception ex)
            {
                var details = $"Error fetching scripts: {ex.GetType().Name}: {ex.Message}";
                if (ex.InnerException != null)
                    details += $" | Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
                details += $" | StackTrace: {ex.StackTrace}";
                AppLogger.Log("ScriptService", details);
                HttpErrorNotifier.NotifyException("ScriptService", ex);
            }

            return new List<CallScript>();
        }

        public async Task<string?> GetChannelUniqueIdAsync(string phoneNumber)
        {
            try
            {
                var settings = App.SipService?.CurrentSettings ?? SipSettings.Load();
                var backendUrl = settings.BackendUrl?.TrimEnd('/');

                if (string.IsNullOrEmpty(backendUrl) || string.IsNullOrEmpty(settings.AccessToken))
                    return null;

                var url = $"{backendUrl}/api/cdr/channel-uniqueid?callerNumber={Uri.EscapeDataString(phoneNumber)}";

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.AccessToken);

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    using var document = JsonDocument.Parse(content);
                    if (document.RootElement.TryGetProperty("uniqueid", out var uniqueIdElement))
                    {
                        return uniqueIdElement.GetString();
                    }
                }

                var errorBody = await response.Content.ReadAsStringAsync();
                AppLogger.Log("ScriptService", $"Fetch channel unique ID failed. Status: {response.StatusCode}. Body: {errorBody}");
                HttpErrorNotifier.NotifyHttpError("ScriptService", url, response.StatusCode, errorBody);
            }
            catch (Exception ex)
            {
                var details = $"Error fetching channel unique ID: {ex.GetType().Name}: {ex.Message}";
                if (ex.InnerException != null)
                    details += $" | Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
                details += $" | StackTrace: {ex.StackTrace}";
                AppLogger.Log("ScriptService", details);
                HttpErrorNotifier.NotifyException("ScriptService", ex);
            }

            return null;
        }

        public async Task<bool> RegisterScriptAsync(string uniqueId, string scriptId)
        {
            try
            {
                var settings = App.SipService?.CurrentSettings ?? SipSettings.Load();
                var backendUrl = settings.BackendUrl?.TrimEnd('/');

                if (string.IsNullOrEmpty(backendUrl) || string.IsNullOrEmpty(settings.AccessToken))
                    return false;

                var url = $"{backendUrl}/api/cdr/log";

                var payload = new CdrLogRequest
                {
                    AsteriskUniqueId = uniqueId,
                    ScriptBranch = scriptId
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.AccessToken);
                request.Content = JsonContent.Create(payload);

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                    return true;

                var errorBody = await response.Content.ReadAsStringAsync();
                AppLogger.Log("ScriptService", $"Register script failed. Status: {response.StatusCode}. Body: {errorBody}");
                HttpErrorNotifier.NotifyHttpError("ScriptService", url, response.StatusCode, errorBody);
                return false;
            }
            catch (Exception ex)
            {
                var details = $"Error registering script: {ex.GetType().Name}: {ex.Message}";
                if (ex.InnerException != null)
                    details += $" | Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
                details += $" | StackTrace: {ex.StackTrace}";
                AppLogger.Log("ScriptService", details);
                HttpErrorNotifier.NotifyException("ScriptService", ex);
            }

            return false;
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
