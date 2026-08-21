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

        /// <summary>The access token that drew the 403 behind <see cref="TasksForbidden"/>, or null if none has yet.</summary>
        private string? _forbiddenForToken;

        /// <summary>
        /// True when the access token currently in play already drew a 403 from a call that
        /// reads the operator's tasks.
        ///
        /// From those calls only — see the latchForbidden opt-in on <see cref="SendAsync"/>.
        /// The readers of this flag are asking "may this operator see their tasks", and a
        /// 403 from anything else does not answer that question: tasks:update is a separate
        /// ability, so an operator who may read but not close a task used to tick one off,
        /// fail honestly, and then have the tasks screen replace a list it had just fetched
        /// successfully with "no access" — for the rest of the session, since the flag lives
        /// as long as the token. GET /api/task-types is a different resource with its own
        /// ability again, and never had any business setting this either.
        ///
        /// tasks:read is a separate ability from the tasks:create this widget already
        /// relies on, so a role without it is a live possibility rather than a hypothesis.
        /// The screen shows "no access" on this instead of an empty list, and the badge
        /// poll stops for the session — a permission that is missing now will still be
        /// missing in two minutes.
        ///
        /// Keyed on the token rather than a plain bool: TaskService is a process-lifetime
        /// singleton (see App.TaskService), but MainWindow.ShowLoginAfterSessionExpiry
        /// returns to the login screen without restarting the app — an expired token and a
        /// handoff to a different operator on a shared terminal both land there. A bool
        /// latch would survive both: an operator without tasks:read would trip it, log
        /// out, and the next operator — who might have the ability — would see "no access"
        /// until someone restarted the widget. Comparing against the session's current
        /// token means a new session always gets to try again.
        ///
        /// A token refresh also mints a new token, so this re-probes once per refresh even
        /// for an operator who genuinely lacks tasks:read. That is the right trade: one
        /// extra 403 an hour costs nothing, and it fails in the direction that
        /// self-corrects rather than the one that sticks.
        /// </summary>
        public bool TasksForbidden =>
            _forbiddenForToken != null && _forbiddenForToken == _settingsProvider()?.AccessToken;

        /// <summary>The access token whose missing assignee has already been logged.</summary>
        private string? _noAssigneeLoggedForToken;

        /// <summary>
        /// True when the JWT in play carries no user id the tasks API can be asked by, so
        /// there is nothing to query and no request to make.
        ///
        /// Neither a failure nor a transient one: the local HS256 login signs <c>sub</c> as
        /// a number, but a Zitadel SSO token carries an opaque string that
        /// <c>int.TryParse</c> will never accept, so every SSO operator is in this state
        /// for the whole session. <see cref="NavBadgeService"/> reads it to tell "I did not
        /// ask" apart from "I asked and it failed" — without that distinction it counted
        /// every poll as a backend failure and backed the missed-calls badge, whose
        /// endpoint was answering perfectly, off to a ten-minute refresh for the shift.
        ///
        /// Scoped to the token by way of <see cref="AssignedToId"/>'s log rather than
        /// latched, for the same reason as <see cref="TasksForbidden"/>: a new session gets
        /// to be a different person.
        /// </summary>
        public bool TasksUnassignable => AssignedToId() == null;

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

            return await SendAsync<TaskListResponse>(HttpMethod.Get, path, ct: ct, latchForbidden: true);
        }

        /// <summary>
        /// Counters behind the Tasks badge.
        ///
        /// Passes notifyErrors: false — NavBadgeService polls this every two minutes for a
        /// whole shift (Task 7), and StatusService's presence poll already showed what an
        /// unattended poll does to the failure banner otherwise: see its
        /// ShouldReportFailure, which exists because a struggling host used to draw three
        /// identical banners a minute for the rest of the shift. The log line still fires
        /// on every failure, so support can still see it — a badge count is just not worth
        /// interrupting whatever the operator is doing over.
        /// </summary>
        public async Task<TaskStats?> GetMyStatsAsync(CancellationToken ct = default)
        {
            var assignee = AssignedToId();
            if (assignee == null) return null;

            return await SendAsync<TaskStats>(HttpMethod.Get, $"/api/tasks/stats?assigneeId={assignee}",
                ct: ct, notifyErrors: false, latchForbidden: true);
        }

        /// <summary>
        /// Moves one task to a new status via PATCH /api/tasks/{id}.
        ///
        /// No CancellationToken, unlike the reads above: this fires once per tap as a
        /// direct user action, not a cancellable background load, so there is nothing
        /// in flight for a caller to cancel out from under.
        /// </summary>
        public async Task<bool> SetStatusAsync(int taskId, string status)
        {
            var response = await SendAsync(HttpMethod.Patch, $"/api/tasks/{taskId}", new { status });
            return response is not null;
        }

        /// <summary>The numeric user id the tasks API assigns by, or null if the JWT has none.</summary>
        private int? AssignedToId()
        {
            var settings = _settingsProvider();
            var sub = settings?.DecodedToken?.Sub;
            if (int.TryParse(sub, out var userId)) return userId;

            // Once per access token, not once per call. NavBadgeService reads
            // TasksUnassignable every two minutes for a whole shift, and an SSO sub never
            // starts parsing, so the unconditional log this replaced wrote the same line
            // some 240 times a day — for exactly the operators it could tell nothing new.
            if (_noAssigneeLoggedForToken != settings?.AccessToken)
            {
                _noAssigneeLoggedForToken = settings?.AccessToken;
                AppLogger.Log("TaskService",
                    $"No assignee — JWT sub is not a user id (sub: {(sub == null ? "<absent>" : $"'{sub}'")}).");
            }

            return null;
        }

        /// <summary>
        /// The part every call used to repeat: settings, base URL, bearer header, status
        /// check, log, notifier. Two copies of it were tolerable; five were not.
        ///
        /// Returns null for every failure. Callers that need to tell a missing permission
        /// apart from a dead backend read <see cref="TasksForbidden"/>.
        ///
        /// <paramref name="latchForbidden"/> is opt-in, and only the two calls that read the
        /// operator's tasks ask for it. A 403 from a write, or from another resource
        /// entirely, is not an answer to the question that flag is asked — see its own
        /// remarks for what conflating them cost.
        /// </summary>
        private async Task<string?> SendAsync(HttpMethod method, string path, object? body = null,
                                              CancellationToken ct = default, bool notifyErrors = true,
                                              bool latchForbidden = false)
        {
            try
            {
                var settings = _settingsProvider();
                var backendUrl = settings?.BackendUrl?.TrimEnd('/');

                if (string.IsNullOrEmpty(backendUrl) || string.IsNullOrEmpty(settings?.AccessToken))
                    return null;

                var url = backendUrl + path;

                using var request = new HttpRequestMessage(method, url);
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.AccessToken);
                if (body != null)
                    request.Content = JsonContent.Create(body, options: _writeOptions);

                using var response = await _httpClient.SendAsync(request, ct);

                if (response.IsSuccessStatusCode)
                    return await response.Content.ReadAsStringAsync(ct);

                if (latchForbidden && response.StatusCode == HttpStatusCode.Forbidden)
                    _forbiddenForToken = settings.AccessToken;

                var errorBody = await response.Content.ReadAsStringAsync(ct);
                AppLogger.Log("TaskService", $"{method} {path} failed. Status: {response.StatusCode}. Body: {errorBody}");
                if (notifyErrors)
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
                if (notifyErrors)
                    HttpErrorNotifier.NotifyException("TaskService", ex);
                return null;
            }
        }

        /// <summary>
        /// Same as above, then deserialized. A body that parses to nothing is a failure,
        /// not an empty result: an empty 200 used to surface as "you have no tasks".
        /// </summary>
        private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body = null,
                                            CancellationToken ct = default, bool notifyErrors = true,
                                            bool latchForbidden = false) where T : class
        {
            var raw = await SendAsync(method, path, body, ct, notifyErrors, latchForbidden);
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
