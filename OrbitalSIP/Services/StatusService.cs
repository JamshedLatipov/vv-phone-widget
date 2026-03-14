using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using OrbitalSIP.Models;

namespace OrbitalSIP.Services
{
    public class StatusService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private DispatcherTimer? _autoOnlineTimer;
        private DateTime? _breakEndTime;

        public event Action<StatusState>? StateChanged;

        public StatusState CurrentState { get; private set; } = new StatusState();
        public DateTime? BreakEndTime => _breakEndTime;

        public StatusService()
        {
            var handler = new HttpClientHandler();
#if DEBUG
            handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
#endif
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
        }

        private async void OnTimerTick(object? sender, EventArgs e)
        {
            if (_breakEndTime.HasValue && DateTime.Now >= _breakEndTime.Value)
            {
                _breakEndTime = null;
                _autoOnlineTimer?.Stop();
                await SetStateAsync(false, null);
            }
        }

        public async Task FetchStateAsync()
        {
            try
            {
                var settings = App.SipService?.CurrentSettings ?? SipSettings.Load();
                var backendUrl = settings.BackendUrl?.TrimEnd('/');

                if (string.IsNullOrEmpty(backendUrl) || string.IsNullOrEmpty(settings.AccessToken))
                    return;

                var url = $"{backendUrl}/api/queue-members/my-state";

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.AccessToken);

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var data = await response.Content.ReadFromJsonAsync<StatusState>();
                    if (data != null)
                    {
                        CurrentState = data;
                        Dispatcher.UIThread.Post(() => StateChanged?.Invoke(CurrentState));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StatusService] Error fetching state: {ex.Message}");
            }
        }

        public async Task<bool> SetStateAsync(bool paused, string? reason, int? durationMinutes = null)
        {
            try
            {
                var settings = App.SipService?.CurrentSettings ?? SipSettings.Load();
                var backendUrl = settings.BackendUrl?.TrimEnd('/');

                if (string.IsNullOrEmpty(backendUrl) || string.IsNullOrEmpty(settings.AccessToken))
                    return false;

                var url = $"{backendUrl}/api/queue-members/0/pause";

                var body = new PauseRequest
                {
                    Paused = paused,
                    ReasonPaused = reason
                };

                using var request = new HttpRequestMessage(HttpMethod.Put, url);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.AccessToken);
                request.Content = JsonContent.Create(body);

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    CurrentState.Paused = paused;
                    CurrentState.ReasonPaused = reason;

                    if (durationMinutes.HasValue && durationMinutes.Value > 0)
                    {
                        _breakEndTime = DateTime.Now.AddMinutes(durationMinutes.Value);
                        _autoOnlineTimer?.Start();
                    }
                    else
                    {
                        _breakEndTime = null;
                        _autoOnlineTimer?.Stop();
                    }

                    Dispatcher.UIThread.Post(() => StateChanged?.Invoke(CurrentState));
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StatusService] Error setting state: {ex.Message}");
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
