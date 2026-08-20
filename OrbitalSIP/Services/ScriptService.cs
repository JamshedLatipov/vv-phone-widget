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
        private readonly Func<SipSettings>? _settingsProvider;
        private readonly bool _ownsHttpClient;

        public ScriptService()
        {
            _httpClient = BackendHttp.Client;
            _ownsHttpClient = false;
        }

        public ScriptService(HttpClient httpClient, Func<SipSettings> settingsProvider, bool ownsHttpClient = false)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
            _ownsHttpClient = ownsHttpClient;
        }

        public async Task<ScriptsResult> GetScriptsAsync()
        {
            try
            {
                var settings = App.SipService?.CurrentSettings ?? SipSettings.Load();
                var backendUrl = settings.BackendUrl?.TrimEnd('/');

                if (string.IsNullOrEmpty(backendUrl) || string.IsNullOrEmpty(settings.AccessToken))
                    return new ScriptsResult { Error = "not-configured" };

                var url = $"{backendUrl}/api/call-scripts?tree=true&active=true";

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.AccessToken);

                using var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var scripts = JsonSerializer.Deserialize<List<CallScript>>(content);
                    return new ScriptsResult { Scripts = scripts ?? new List<CallScript>() };
                }

                var errorBody = await response.Content.ReadAsStringAsync();
                AppLogger.Log("ScriptService", $"Fetch scripts failed. Status: {response.StatusCode}. Body: {errorBody}");
                HttpErrorNotifier.NotifyHttpError("ScriptService", url, response.StatusCode, errorBody);
                return new ScriptsResult { Error = $"HTTP {(int)response.StatusCode}" };
            }
            catch (Exception ex)
            {
                var details = $"Error fetching scripts: {ex.GetType().Name}: {ex.Message}";
                if (ex.InnerException != null)
                    details += $" | Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
                details += $" | StackTrace: {ex.StackTrace}";
                AppLogger.Log("ScriptService", details);
                HttpErrorNotifier.NotifyException("ScriptService", ex);
                return new ScriptsResult { Error = ex.Message };
            }
        }

        /// <param name="notifyErrors">
        /// False suppresses the error banner (the log still records everything).
        /// Used by the in-call comment box, where failing to resolve the channel
        /// only costs the call link — the comment itself still saves — so a toast
        /// would report an error for an action that succeeded, mid-call.
        /// </param>
        public async Task<string?> GetChannelUniqueIdAsync(string phoneNumber, bool notifyErrors = true)
            => await GetPrimaryLinkedIdAsync(phoneNumber, notifyFailure: notifyErrors);

        /// <summary>
        /// Resolves the active call's primary Asterisk linkedid. The server keeps the
        /// legacy JSON property name <c>uniqueid</c>, but its value is the primary
        /// linkedid selected for the authenticated operator's channel.
        /// </summary>
        public async Task<string?> GetPrimaryLinkedIdAsync(string phoneNumber, CancellationToken cancellationToken = default)
            => await GetPrimaryLinkedIdAsync(phoneNumber, notifyFailure: false, cancellationToken);

        private async Task<string?> GetPrimaryLinkedIdAsync(
            string phoneNumber,
            bool notifyFailure,
            CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var settings = _settingsProvider?.Invoke() ?? App.SipService?.CurrentSettings ?? SipSettings.Load();
                var backendUrl = settings.BackendUrl?.TrimEnd('/');

                if (string.IsNullOrEmpty(backendUrl) || string.IsNullOrEmpty(settings.AccessToken))
                    return null;

                var url = $"{backendUrl}/api/cdr/channel-uniqueid?callerNumber={Uri.EscapeDataString(phoneNumber)}";

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.AccessToken);

                using var response = await _httpClient.SendAsync(request, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    using var document = JsonDocument.Parse(content);
                    if (document.RootElement.TryGetProperty("uniqueid", out var uniqueIdElement))
                    {
                        var primaryLinkedId = uniqueIdElement.GetString();
                        return string.IsNullOrWhiteSpace(primaryLinkedId) ? null : primaryLinkedId;
                    }
                }

                var errorBody = await response.Content.ReadAsStringAsync();
                AppLogger.Log("ScriptService", $"Fetch channel unique ID failed. Status: {response.StatusCode}. Body: {errorBody}");
                if (notifyFailure)
                    HttpErrorNotifier.NotifyHttpError("ScriptService", url, response.StatusCode, errorBody);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var details = $"Error fetching channel unique ID: {ex.GetType().Name}: {ex.Message}";
                if (ex.InnerException != null)
                    details += $" | Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
                details += $" | StackTrace: {ex.StackTrace}";
                AppLogger.Log("ScriptService", details);
                if (notifyFailure)
                    HttpErrorNotifier.NotifyException("ScriptService", ex);
            }

            return null;
        }

        /// <summary>
        /// Ensures a CallLog row exists for the given Asterisk uniqueId and returns its id —
        /// the callLogId used to link a task (or lead) to the call. The backend upserts by
        /// asteriskUniqueId, so repeat calls return the same id. Only the uniqueId is sent, so
        /// this never overwrites a note/script already stored for the call. Null on failure.
        /// </summary>
        /// <param name="notifyErrors">False suppresses the error banner — see
        /// GetChannelUniqueIdAsync.</param>
        public async Task<string?> SaveCallLogAsync(string uniqueId, bool notifyErrors = true)
        {
            if (string.IsNullOrWhiteSpace(uniqueId))
                return null;

            try
            {
                var settings = App.SipService?.CurrentSettings ?? SipSettings.Load();
                var backendUrl = settings.BackendUrl?.TrimEnd('/');

                if (string.IsNullOrEmpty(backendUrl) || string.IsNullOrEmpty(settings.AccessToken))
                    return null;

                var url = $"{backendUrl}/api/cdr/log";

                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.AccessToken);
                // Send ONLY the uniqueId — omitting note/scriptBranch so an existing call log
                // is not blanked out (backend saveLog forwards only provided fields).
                request.Content = JsonContent.Create(new { asteriskUniqueId = uniqueId });

                using var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    using var document = JsonDocument.Parse(content);
                    if (document.RootElement.TryGetProperty("id", out var idElement) &&
                        idElement.ValueKind == JsonValueKind.String)
                    {
                        return idElement.GetString();
                    }
                    AppLogger.Log("ScriptService", $"Save call log: no id in response. Body: {content}");
                    return null;
                }

                var errorBody = await response.Content.ReadAsStringAsync();
                AppLogger.Log("ScriptService", $"Save call log failed. Status: {response.StatusCode}. Body: {errorBody}");
                if (notifyErrors)
                    HttpErrorNotifier.NotifyHttpError("ScriptService", url, response.StatusCode, errorBody);
                return null;
            }
            catch (Exception ex)
            {
                AppLogger.Log("ScriptService", $"Error saving call log: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Registers the selection against the CDR entry and, on success, marks the call as
        /// logged for the current operator. Shared by the active-call and history entry points.
        /// </summary>
        public async Task<bool> RegisterAndMarkAsync(string uniqueId, ScriptSelection selection)
        {
            bool success = await RegisterScriptAsync(uniqueId, selection.Script.Id!, selection.Note);
            if (!success)
                return false;

            var settings = App.SipService?.CurrentSettings ?? SipSettings.Load();
            var operatorId = settings.DecodedToken?.Operator?.Username ?? settings.Username;
            App.LoggedCallService.MarkCallAsLogged(uniqueId, operatorId);
            return true;
        }

        public async Task<bool> RegisterScriptAsync(string uniqueId, string scriptId, string note = "")
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
                    ScriptBranch = scriptId,
                    Note = note
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.AccessToken);
                request.Content = JsonContent.Create(payload);

                using var response = await _httpClient.SendAsync(request);
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
            if (_ownsHttpClient)
                _httpClient.Dispose();
        }
    }
}
