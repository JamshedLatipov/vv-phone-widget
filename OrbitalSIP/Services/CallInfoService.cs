using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using OrbitalSIP.Models;

namespace OrbitalSIP.Services
{
    public class CallInfoService : IDisposable
    {
        private readonly HttpClient _httpClient;

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public CallInfoService()
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };
            _httpClient = new HttpClient(handler);
        }

        public async Task<CallInfoResponse?> GetCallInfoAsync(string phoneNumber)
        {
            try
            {
                var settings = App.SipService?.CurrentSettings ?? SipSettings.Load();
                var backendUrl = settings.BackendUrl?.TrimEnd('/');

                AppLogger.Log("CallInfoService", $"GetCallInfoAsync called. Phone='{phoneNumber}' BackendUrl='{backendUrl}' HasToken={!string.IsNullOrEmpty(settings.AccessToken)}");

                if (string.IsNullOrEmpty(backendUrl))
                {
                    AppLogger.Log("CallInfoService", "Aborted: BackendUrl is empty.");
                    return null;
                }

                if (string.IsNullOrEmpty(settings.AccessToken))
                {
                    AppLogger.Log("CallInfoService", "Aborted: AccessToken is empty.");
                    return null;
                }

                var url = $"{backendUrl}/api/integrations/call-info/{Uri.EscapeDataString(phoneNumber)}";
                AppLogger.Log("CallInfoService", $"Requesting: GET {url}");

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.AccessToken);

                var response = await _httpClient.SendAsync(request);
                AppLogger.Log("CallInfoService", $"Response status: {(int)response.StatusCode} {response.StatusCode}");

                if (response.IsSuccessStatusCode)
                {
                    var rawBody = await response.Content.ReadAsStringAsync();
                    AppLogger.Log("CallInfoService", $"Raw response body: {rawBody}");

                    try
                    {
                        var result = System.Text.Json.JsonSerializer.Deserialize<CallInfoResponse>(rawBody, _jsonOptions);
                        AppLogger.Log("CallInfoService", $"Deserialized: Sections count={result?.Sections?.Count ?? -1}");
                        return result;
                    }
                    catch (Exception jsonEx)
                    {
                        AppLogger.Log("CallInfoService", $"JSON deserialization error: {jsonEx.Message}");
                        return null;
                    }
                }
                else
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    AppLogger.Log("CallInfoService", $"Failed. Status: {response.StatusCode}. Body: {errorBody}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("CallInfoService", $"Exception: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Resolves a dot-notation key (e.g. "tarif.name") from a JsonElement.
        /// Returns null if any segment is missing.
        /// </summary>
        public static string? ResolveField(JsonElement? data, string dotKey)
        {
            if (data == null || string.IsNullOrEmpty(dotKey))
                return null;

            var segments = dotKey.Split('.');
            JsonElement current = data.Value;

            foreach (var segment in segments)
            {
                if (current.ValueKind != JsonValueKind.Object)
                    return null;

                if (!current.TryGetProperty(segment, out var next))
                    return null;

                current = next;
            }

            return current.ValueKind switch
            {
                JsonValueKind.String => current.GetString(),
                JsonValueKind.Number => current.GetRawText(),
                JsonValueKind.True   => "Да",
                JsonValueKind.False  => "Нет",
                JsonValueKind.Null   => null,
                _                   => current.GetRawText()
            };
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
