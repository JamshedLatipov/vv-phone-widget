using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using OrbitalSIP.Models;
using System.Text.Json;

namespace OrbitalSIP.Services
{
    public class StatusService : IDisposable
    {
        private readonly HttpClient _httpClient;

        /// <summary>Periodic re-fetch, so a supervisor pause or a status change made elsewhere shows up without a socket.</summary>
        private DispatcherTimer? _pollTimer;

        /// <summary>
        /// Break countdown. Runs only while a break is actually ticking down — the two used to share
        /// one 1 s timer, which meant a wake-up every second for the whole shift to check a field
        /// that is null almost all of that time.
        /// </summary>
        private DispatcherTimer? _breakTimer;

        /// <summary>Poll interval while the backend is answering.</summary>
        private static readonly TimeSpan HealthyPollInterval = TimeSpan.FromSeconds(20);

        /// <summary>Ceiling for the backed-off interval while it is not.</summary>
        private static readonly TimeSpan MaxPollInterval = TimeSpan.FromMinutes(5);

        private DateTime? _breakEndTime;
        private bool _isFetching;

        /// <summary>
        /// Length of the current failure streak. A backend that is down — or a session
        /// that has expired — used to produce an error banner on every single poll, three
        /// a minute for the rest of the shift, all saying the same thing. Only the first
        /// failure of a streak is reported now, and the interval backs off so the widget
        /// stops hammering a host that is not answering.
        /// </summary>
        private int _consecutiveFailures;

        public event Action<StatusState>? StateChanged;

        public StatusState CurrentState { get; private set; } = new StatusState();
        public DateTime? BreakEndTime => _breakEndTime;

        public StatusService()
        {
            _httpClient = BackendHttp.Client;

            _pollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(20)
            };
            _pollTimer.Tick += OnPollTick;

            _breakTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _breakTimer.Tick += OnBreakTick;
        }

        public void StartPolling()
        {
            _ = FetchStateAsync();
            _pollTimer?.Start();
        }

        /// <summary>
        /// Stops polling — used when the session ends, so the login screen is not sitting
        /// behind a stream of 401s from a widget that has not noticed it is signed out.
        /// </summary>
        public void StopPolling()
        {
            _pollTimer?.Stop();
            _breakTimer?.Stop();
            ResetBackoff();
        }

        /// <summary>Reports the first failure of a streak only, and stretches the interval.</summary>
        private bool ShouldReportFailure()
        {
            _consecutiveFailures++;

            if (_pollTimer != null)
                _pollTimer.Interval = PollBackoff.Next(_consecutiveFailures, HealthyPollInterval, MaxPollInterval);

            return _consecutiveFailures == 1;
        }

        private void ResetBackoff()
        {
            if (_consecutiveFailures == 0) return;

            _consecutiveFailures = 0;
            if (_pollTimer != null) _pollTimer.Interval = HealthyPollInterval;
        }

        private async void OnPollTick(object? sender, EventArgs e) => await FetchStateAsync();

        private async void OnBreakTick(object? sender, EventArgs e)
        {
            if (!_breakEndTime.HasValue)
            {
                _breakTimer?.Stop();
                return;
            }

            if (DateTime.Now >= _breakEndTime.Value)
            {
                AppLogger.Log("StatusService", "Timer expired. Setting status back to online.");
                _breakEndTime = null;
                _breakTimer?.Stop();
                await SetStateAsync(null, null);
            }
        }

        public async Task FetchStateAsync()
        {
            if (_isFetching)
                return;
            _isFetching = true;
            try
            {
                var settings = App.SipService?.CurrentSettings ?? SipSettings.Load();
                var backendUrl = settings.BackendUrl?.TrimEnd('/');

                if (string.IsNullOrEmpty(backendUrl) || string.IsNullOrEmpty(settings.AccessToken))
                    return;

                var url = $"{backendUrl}/api/presence/me";

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.AccessToken);

                // Only failures are logged from here on. This runs three times a minute for
                // the whole shift, and logging the URL, the status and the full response
                // body each time pushed everything else out of a log that rotates at 4 MB —
                // while writing the operator's presence payload to disk over and over.
                using var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<StatusState>(content);

                    // Reset only once the response has actually been understood. Resetting
                    // on the status code alone meant a 200 carrying a body this client
                    // cannot parse threw into the catch below, reported a failure, and had
                    // its backoff wiped again on the very next poll — defeating both the
                    // backoff and the once-per-streak banner this exists to provide.
                    ResetBackoff();

                    if (data != null)
                    {
                        CurrentState = data;
                        Dispatcher.UIThread.Post(() => StateChanged?.Invoke(CurrentState));
                    }
                }
                else
                {
                    var errBody = await response.Content.ReadAsStringAsync();
                    AppLogger.Log("StatusService", $"Fetch state failed. Status: {(int)response.StatusCode}. Body: {errBody}");
                    if (ShouldReportFailure())
                        HttpErrorNotifier.NotifyHttpError("StatusService", url, response.StatusCode, errBody);
                }
            }
            catch (Exception ex)
            {
                var details = $"Error fetching state: {ex.GetType().Name}: {ex.Message}";
                if (ex.InnerException != null)
                    details += $" | Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
                details += $" | StackTrace: {ex.StackTrace}";
                AppLogger.Log("StatusService", details);
                if (ShouldReportFailure())
                    HttpErrorNotifier.NotifyException("StatusService", ex);
            }
            finally
            {
                _isFetching = false;
            }
        }

        public async Task<bool> SetStateAsync(string? manualStatus, string? reason, int? durationMinutes = null)
        {
            try
            {
                var settings = App.SipService?.CurrentSettings ?? SipSettings.Load();
                var backendUrl = settings.BackendUrl?.TrimEnd('/');

                if (string.IsNullOrEmpty(backendUrl) || string.IsNullOrEmpty(settings.AccessToken))
                {
                    AppLogger.Log("StatusService", "Cannot set state: BackendUrl or AccessToken is missing.");
                    return false;
                }

                var url = $"{backendUrl}/api/presence/me";

                var body = new SetPresenceRequest
                {
                    ManualStatus = manualStatus,
                    Reason = reason
                };

                AppLogger.Log("StatusService", $"Setting state to URL: {url}");
                AppLogger.Log("StatusService", $"Payload: ManualStatus={manualStatus ?? "null"}, Reason={reason ?? "null"}, DurationMinutes={durationMinutes?.ToString() ?? "null"}");

                using var request = new HttpRequestMessage(HttpMethod.Put, url);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.AccessToken);
                request.Content = JsonContent.Create(body);

                using var response = await _httpClient.SendAsync(request);
                AppLogger.Log("StatusService", $"Set state response status code: {response.StatusCode}");

                if (response.IsSuccessStatusCode)
                {
                    AppLogger.Log("StatusService", "State successfully updated on server.");

                    var content = await response.Content.ReadAsStringAsync();

                    // Trust the server's returned shape (it enforces supervisor-pause).
                    var data = JsonSerializer.Deserialize<StatusState>(content);
                    if (data != null)
                    {
                        CurrentState = data;
                    }
                    else
                    {
                        CurrentState.ManualStatus = manualStatus;
                        CurrentState.ManualReason = reason;
                    }

                    if (durationMinutes.HasValue && durationMinutes.Value > 0)
                    {
                        _breakEndTime = DateTime.Now.AddMinutes(durationMinutes.Value);
                        _breakTimer?.Start();
                        AppLogger.Log("StatusService", $"Started auto-online timer for {durationMinutes.Value} minutes.");
                    }
                    else
                    {
                        _breakEndTime = null;
                        _breakTimer?.Stop();
                        AppLogger.Log("StatusService", "Auto-online timer cleared.");
                    }

                    Dispatcher.UIThread.Post(() => StateChanged?.Invoke(CurrentState));
                    return true;
                }
                else
                {
                     var errBody = await response.Content.ReadAsStringAsync();
                     AppLogger.Log("StatusService", $"Set state failed. Body: {errBody}");
                     HttpErrorNotifier.NotifyHttpError("StatusService", url, response.StatusCode, errBody);
                }
            }
            catch (Exception ex)
            {
                var details = $"Error setting state: {ex.GetType().Name}: {ex.Message}";
                if (ex.InnerException != null)
                    details += $" | Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
                details += $" | StackTrace: {ex.StackTrace}";
                AppLogger.Log("StatusService", details);
                HttpErrorNotifier.NotifyException("StatusService", ex);
            }
            return false;
        }

        public void Dispose() => StopPolling();
    }
}
