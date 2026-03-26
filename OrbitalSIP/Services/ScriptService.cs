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
            }
            catch (Exception ex)
            {
                AppLogger.Log("ScriptService", $"Error fetching scripts: {ex.Message}");
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
            }
            catch (Exception ex)
            {
                AppLogger.Log("ScriptService", $"Error fetching channel unique ID: {ex.Message}");
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
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                AppLogger.Log("ScriptService", $"Error registering script: {ex.Message}");
            }

            return false;
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
