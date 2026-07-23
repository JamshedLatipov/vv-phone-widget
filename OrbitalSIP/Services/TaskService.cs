using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using OrbitalSIP.Models;

namespace OrbitalSIP.Services
{
    /// <summary>
    /// CRM task API client. Mirrors LeadService: bearer-authed calls to the
    /// backend derived from the current SIP settings. Used to create a task
    /// straight off an active call.
    /// </summary>
    public class TaskService : IDisposable
    {
        private readonly HttpClient _httpClient;

        private static readonly JsonSerializerOptions _writeOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private static readonly JsonSerializerOptions _readOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public TaskService()
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };
            _httpClient = new HttpClient(handler);
        }

        /// <summary>Creates a task via POST /api/tasks. Returns true on 2xx.</summary>
        public async Task<bool> CreateTaskAsync(CreateTaskRequest task)
        {
            try
            {
                var settings = App.SipService?.CurrentSettings ?? SipSettings.Load();
                var backendUrl = settings.BackendUrl?.TrimEnd('/');

                if (string.IsNullOrEmpty(backendUrl) || string.IsNullOrEmpty(settings.AccessToken))
                    return false;

                var url = $"{backendUrl}/api/tasks";

                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.AccessToken);
                request.Content = JsonContent.Create(task, options: _writeOptions);

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                    return true;

                var errorBody = await response.Content.ReadAsStringAsync();
                AppLogger.Log("TaskService", $"Create task failed. Status: {response.StatusCode}. Body: {errorBody}");
                HttpErrorNotifier.NotifyHttpError("TaskService", url, response.StatusCode, errorBody);
                return false;
            }
            catch (Exception ex)
            {
                var details = $"Error creating task: {ex.GetType().Name}: {ex.Message}";
                if (ex.InnerException != null)
                    details += $" | Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
                details += $" | StackTrace: {ex.StackTrace}";
                AppLogger.Log("TaskService", details);
                HttpErrorNotifier.NotifyException("TaskService", ex);
                return false;
            }
        }

        /// <summary>
        /// Fetches active task types via GET /api/task-types for the picker.
        /// Returns an empty list on any failure (the dialog stays usable without types).
        /// </summary>
        public async Task<List<TaskTypeItem>> GetTaskTypesAsync()
        {
            try
            {
                var settings = App.SipService?.CurrentSettings ?? SipSettings.Load();
                var backendUrl = settings.BackendUrl?.TrimEnd('/');

                if (string.IsNullOrEmpty(backendUrl) || string.IsNullOrEmpty(settings.AccessToken))
                    return new List<TaskTypeItem>();

                var url = $"{backendUrl}/api/task-types";

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.AccessToken);

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var rawBody = await response.Content.ReadAsStringAsync();
                    var items = JsonSerializer.Deserialize<List<TaskTypeItem>>(rawBody, _readOptions)
                                ?? new List<TaskTypeItem>();
                    return items
                        .Where(t => t.IsActive)
                        .OrderBy(t => t.SortOrder)
                        .ThenBy(t => t.Name)
                        .ToList();
                }

                var errorBody = await response.Content.ReadAsStringAsync();
                AppLogger.Log("TaskService", $"Fetch task types failed. Status: {response.StatusCode}. Body: {errorBody}");
                HttpErrorNotifier.NotifyHttpError("TaskService", url, response.StatusCode, errorBody);
                return new List<TaskTypeItem>();
            }
            catch (Exception ex)
            {
                AppLogger.Log("TaskService", $"Error fetching task types: {ex.GetType().Name}: {ex.Message}");
                return new List<TaskTypeItem>();
            }
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
