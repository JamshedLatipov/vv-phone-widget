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
        private DispatcherTimer? _autoOnlineTimer;
        private DateTime? _breakEndTime;
        private int _pollTickCounter;
        private bool _isFetching;

        public event Action<StatusState>? StateChanged;

        public StatusState CurrentState { get; private set; } = new StatusState();
        public DateTime? BreakEndTime => _breakEndTime;

        public StatusService()
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };
            _httpClient = new HttpClient(handler);

            _autoOnlineTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _autoOnlineTimer.Tick += OnTimerTick;
        }

        public void StartPolling()
        {
            _ = FetchStateAsync();
            // 1s timer drives both the break auto-online countdown and a ~20s
            // periodic re-fetch (so live supervisor-pause + cross-UI status
            // changes are reflected without a dedicated socket).
            _autoOnlineTimer?.Start();
        }

        private async void OnTimerTick(object? sender, EventArgs e)
        {
            // Break auto-online countdown.
            if (_breakEndTime.HasValue)
            {
                if (DateTime.Now >= _breakEndTime.Value)
                {
                    AppLogger.Log("StatusService", "Timer expired. Setting status back to online.");
                    _breakEndTime = null;
                    await SetStateAsync(null, null);
                }
            }

            // Periodic re-fetch every ~20 ticks (~20s).
            _pollTickCounter++;
            if (_pollTickCounter >= 20)
            {
                _pollTickCounter = 0;
                await FetchStateAsync();
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
                AppLogger.Log("StatusService", $"Fetching state from: {url}");

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.AccessToken);

                var response = await _httpClient.SendAsync(request);
                AppLogger.Log("StatusService", $"Fetch response status code: {response.StatusCode}");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    AppLogger.Log("StatusService", $"Fetch response body: {content}");

                    var data = JsonSerializer.Deserialize<StatusState>(content);
                    if (data != null)
                    {
                        CurrentState = data;
                        Dispatcher.UIThread.Post(() => StateChanged?.Invoke(CurrentState));
                    }
                }
                else
                {
                    var errBody = await response.Content.ReadAsStringAsync();
                    AppLogger.Log("StatusService", $"Fetch state failed. Body: {errBody}");
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

                var response = await _httpClient.SendAsync(request);
                AppLogger.Log("StatusService", $"Set state response status code: {response.StatusCode}");

                if (response.IsSuccessStatusCode)
                {
                    AppLogger.Log("StatusService", "State successfully updated on server.");

                    var content = await response.Content.ReadAsStringAsync();
                    AppLogger.Log("StatusService", $"Set state response body: {content}");

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
                        _autoOnlineTimer?.Start();
                        AppLogger.Log("StatusService", $"Started auto-online timer for {durationMinutes.Value} minutes.");
                    }
                    else
                    {
                        _breakEndTime = null;
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

        public void Dispose()
        {
            _autoOnlineTimer?.Stop();
            _httpClient.Dispose();
        }
    }
}
