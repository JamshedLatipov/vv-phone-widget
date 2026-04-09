using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using OrbitalSIP.Models;

namespace OrbitalSIP.Services
{
    public class LeadService : IDisposable
    {
        private readonly HttpClient _httpClient;

        public LeadService()
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };
            _httpClient = new HttpClient(handler);
        }

        public async Task<bool> CreateLeadAsync(CreateLeadRequest lead)
        {
            try
            {
                var settings = App.SipService?.CurrentSettings ?? SipSettings.Load();
                var backendUrl = settings.BackendUrl?.TrimEnd('/');

                if (string.IsNullOrEmpty(backendUrl) || string.IsNullOrEmpty(settings.AccessToken))
                    return false;

                var url = $"{backendUrl}/api/leads";

                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.AccessToken);
                request.Content = JsonContent.Create(lead);

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
                else
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    AppLogger.Log("LeadService", $"Create lead failed. Status: {response.StatusCode}. Body: {errorBody}");
                    HttpErrorNotifier.NotifyHttpError("LeadService", url, response.StatusCode, errorBody);
                    return false;
                }
            }
            catch (Exception ex)
            {
                var details = $"Error creating lead: {ex.GetType().Name}: {ex.Message}";
                if (ex.InnerException != null)
                    details += $" | Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
                details += $" | StackTrace: {ex.StackTrace}";
                AppLogger.Log("LeadService", details);
                HttpErrorNotifier.NotifyException("LeadService", ex);
                return false;
            }
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
