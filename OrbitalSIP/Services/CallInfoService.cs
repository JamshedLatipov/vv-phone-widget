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
            _httpClient = BackendHttp.Client;
        }

        public async Task<CallInfoResponse?> GetCallInfoAsync(string phoneNumber)
        {
            try
            {
                var settings = App.SipService?.CurrentSettings ?? SipSettings.Load();
                var backendUrl = settings.BackendUrl?.TrimEnd('/');

                AppLogger.Log("CallInfoService", $"GetCallInfoAsync called. Phone={Models.LogRedaction.Phone(phoneNumber)} HasBackendUrl={!string.IsNullOrEmpty(backendUrl)} HasToken={!string.IsNullOrEmpty(settings.AccessToken)}");

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

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.AccessToken);

                using var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    // The body is the caller's whole CRM profile. It used to be written to
                    // app.log verbatim on every answered call, along with the URL that
                    // carries their number — the single largest source of PII on disk.
                    var rawBody = await response.Content.ReadAsStringAsync();

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
                    HttpErrorNotifier.NotifyHttpError("CallInfoService", url, response.StatusCode, errorBody);
                    return null;
                }
            }
            catch (Exception ex)
            {
                var details = $"Exception: {ex.GetType().Name}: {ex.Message}";
                if (ex.InnerException != null)
                    details += $" | Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
                details += $" | StackTrace: {ex.StackTrace}";
                AppLogger.Log("CallInfoService", details);
                HttpErrorNotifier.NotifyException("CallInfoService", ex);
                return null;
            }
        }

        /// <summary>The client is shared, so there is nothing here to release.</summary>
        public void Dispose()
        {
        }
    }
}
