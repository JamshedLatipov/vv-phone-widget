using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using OrbitalSIP.Models;

namespace OrbitalSIP.Services
{
    /// <summary>
    /// CRM task API client. Mirrors LeadService: bearer-authed calls to the
    /// backend derived from the current SIP settings. Used to create a task
    /// straight off an active call, and to list the operator's own tasks.
    /// </summary>
    public class TaskService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly Func<SipSettings> _settingsProvider;
        private readonly bool _ownsHttpClient;

        private static readonly JsonSerializerOptions _writeOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private static readonly JsonSerializerOptions _readOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public TaskService()
            : this(BackendHttp.Client, () => App.SipService?.CurrentSettings ?? SipSettings.Load(), ownsHttpClient: false)
        {
        }

        /// <summary>
        /// Injectable overload, same shape as SmsService and ScriptService already use, so
        /// the request URLs and failure handling below are reachable from tests.
        /// </summary>
        public TaskService(HttpClient httpClient, Func<SipSettings> settingsProvider, bool ownsHttpClient = false)
        {
            _httpClient = httpClient;
            _settingsProvider = settingsProvider;
            _ownsHttpClient = ownsHttpClient;
        }

        /// <summary>
        /// Latched by the first 403 from the tasks API.
        ///
        /// tasks:read is a separate ability from the tasks:create this widget already
        /// relies on, so a role without it is a live possibility rather than a hypothesis.
        /// The screen shows "no access" on this instead of an empty list, and the badge
        /// poll stops for the session — a permission that is missing now will still be
        /// missing in two minutes.
        /// </summary>
        public bool TasksForbidden { get; private set; }

        /// <summary>Creates a task via POST /api/tasks. Returns true on 2xx.</summary>
        public async Task<bool> CreateTaskAsync(CreateTaskRequest task)
        {
            var response = await SendAsync(HttpMethod.Post, "/api/tasks", task);
            return response is not null;
        }

        /// <summary>
        /// Fetches active task types via GET /api/task-types for the picker.
        /// Returns an empty list on any failure (the dialog stays usable without types).
        /// </summary>
        public async Task<List<TaskTypeItem>> GetTaskTypesAsync()
        {
            var items = await SendAsync<List<TaskTypeItem>>(HttpMethod.Get, "/api/task-types");
            if (items == null) return new List<TaskTypeItem>();

            return items
                .Where(t => t.IsActive)
                .OrderBy(t => t.SortOrder)
                .ThenBy(t => t.Name)
                .ToList();
        }

        /// <summary>
        /// The operator's own tasks. <paramref name="status"/> is passed straight through
        /// to the backend filter; null asks for everything.
        ///
        /// Note the caller's burden: "open" is pending + in_progress, which the backend
        /// treats as disjoint sets, so listing open tasks means calling this twice.
        /// </summary>
        public async Task<TaskListResponse?> GetMyTasksAsync(string? status, CancellationToken ct = default)
        {
            var assignee = AssignedToId();
            if (assignee == null) return null;

            var path = $"/api/tasks?assignedToId={assignee}&page=1&limit=50";
            if (!string.IsNullOrWhiteSpace(status))
                path += $"&status={Uri.EscapeDataString(status)}";

            return await SendAsync<TaskListResponse>(HttpMethod.Get, path, ct: ct);
        }

        /// <summary>Counters behind the Tasks badge.</summary>
        public async Task<TaskStats?> GetMyStatsAsync(CancellationToken ct = default)
        {
            var assignee = AssignedToId();
            if (assignee == null) return null;

            return await SendAsync<TaskStats>(HttpMethod.Get, $"/api/tasks/stats?assigneeId={assignee}", ct: ct);
        }

        /// <summary>Moves one task to a new status via PATCH /api/tasks/{id}.</summary>
        public async Task<bool> SetStatusAsync(int taskId, string status)
        {
            var response = await SendAsync(HttpMethod.Patch, $"/api/tasks/{taskId}", new { status });
            return response is not null;
        }

        /// <summary>The numeric user id the tasks API assigns by, or null if the JWT has none.</summary>
        private int? AssignedToId()
        {
            var sub = _settingsProvider()?.DecodedToken?.Sub;
            if (int.TryParse(sub, out var userId)) return userId;

            AppLogger.Log("TaskService",
                $"No assignee — JWT sub is not a user id (sub: {(sub == null ? "<absent>" : $"'{sub}'")}).");
            return null;
        }

        /// <summary>
        /// The part every call used to repeat: settings, base URL, bearer header, status
        /// check, log, notifier. Two copies of it were tolerable; five were not.
        ///
        /// Returns null for every failure. Callers that need to tell a missing permission
        /// apart from a dead backend read <see cref="TasksForbidden"/>.
        /// </summary>
        private async Task<string?> SendAsync(HttpMethod method, string path, object? body = null,
                                              CancellationToken ct = default)
        {
            var url = string.Empty;
            try
            {
                var settings = _settingsProvider();
                var backendUrl = settings?.BackendUrl?.TrimEnd('/');

                if (string.IsNullOrEmpty(backendUrl) || string.IsNullOrEmpty(settings?.AccessToken))
                    return null;

                url = backendUrl + path;

                using var request = new HttpRequestMessage(method, url);
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.AccessToken);
                if (body != null)
                    request.Content = JsonContent.Create(body, options: _writeOptions);

                using var response = await _httpClient.SendAsync(request, ct);

                if (response.IsSuccessStatusCode)
                    return await response.Content.ReadAsStringAsync(ct);

                if (response.StatusCode == HttpStatusCode.Forbidden)
                    TasksForbidden = true;

                var errorBody = await response.Content.ReadAsStringAsync(ct);
                AppLogger.Log("TaskService", $"{method} {path} failed. Status: {response.StatusCode}. Body: {errorBody}");
                HttpErrorNotifier.NotifyHttpError("TaskService", url, response.StatusCode, errorBody);
                return null;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // The caller cancelled — that is not a failure to report. Without this the
                // generic catch below logs a stack trace and raises a banner, so switching
                // the tasks filter would tell the operator their own tap had gone wrong.
                throw;
            }
            catch (Exception ex)
            {
                var details = $"Error on {method} {path}: {ex.GetType().Name}: {ex.Message}";
                if (ex.InnerException != null)
                    details += $" | Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
                details += $" | StackTrace: {ex.StackTrace}";
                AppLogger.Log("TaskService", details);
                HttpErrorNotifier.NotifyException("TaskService", ex);
                return null;
            }
        }

        /// <summary>
        /// Same as above, then deserialized. A body that parses to nothing is a failure,
        /// not an empty result: an empty 200 used to surface as "you have no tasks".
        /// </summary>
        private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body = null,
                                            CancellationToken ct = default) where T : class
        {
            var raw = await SendAsync(method, path, body, ct);
            if (string.IsNullOrWhiteSpace(raw)) return null;

            try
            {
                return JsonSerializer.Deserialize<T>(raw, _readOptions);
            }
            catch (JsonException ex)
            {
                AppLogger.Log("TaskService", $"Could not read the {method} {path} response: {ex.Message}");
                return null;
            }
        }

        public void Dispose()
        {
            if (_ownsHttpClient) _httpClient.Dispose();
        }
    }
}
