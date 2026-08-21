using Avalonia.Threading;
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using OrbitalSIP.Models;
using OrbitalSIP.Views;

namespace OrbitalSIP.Services
{
    /// <summary>
    /// Keeps the bottom-nav badge numbers fresh, and owns the only timer that does so.
    ///
    /// The timer cannot live in BottomNavControl: MainWindow rebuilds the whole screen —
    /// and with it a new control — on every navigation, so a timer there would restart on
    /// every tab press. The same asymmetry already froze OperatorStatsControl's two-minute
    /// refresh roughly 280 ms into the screen-swap animation.
    ///
    /// It also serves OperatorStatsControl, which used to poll the same URL on its own
    /// schedule. One request now, not two.
    /// </summary>
    public sealed class NavBadgeService : IDisposable
    {
        private static readonly TimeSpan Healthy = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan MaxInterval = TimeSpan.FromMinutes(10);

        private readonly NavBadgeState _state = new();
        private readonly HttpClient _httpClient = BackendHttp.Client;
        private DispatcherTimer? _timer;
        private int _consecutiveFailures;

        /// <summary>Raised on the UI thread whenever a number changed.</summary>
        public event Action? Changed;

        /// <summary>Latest operator stats, for whoever wants more than a badge out of them.</summary>
        public OperatorStats? OperatorStats { get; private set; }

        public void Start()
        {
            if (_timer != null) return;

            _timer = new DispatcherTimer { Interval = Healthy };
            _timer.Tick += async (_, __) => await PollAsync();
            _timer.Start();

            _ = PollAsync();
        }

        public void Stop()
        {
            _timer?.Stop();
            _timer = null;

            // The streak describes how badly the backend is answering *this* session, so it
            // ends with the session — same reason StatusService.StopPolling calls
            // ResetBackoff. Carried over, a streak run up while an expired token was still
            // being polled would meet the next operator's first poll already at the ten-minute
            // cap, and one unlucky failure there would leave their badges stale for ten
            // minutes of a session whose backend is fine.
            _consecutiveFailures = 0;
        }

        /// <summary>Polls right now — after ticking a task off, or after a call ends.</summary>
        public Task RefreshNowAsync() => PollAsync();

        public void MarkRecentsSeen()
        {
            _state.MarkRecentsSeen();
            Raise();
        }

        /// <summary>Pushes the current numbers into a freshly built bar.</summary>
        public void ApplyTo(BottomNavControl nav)
        {
            nav.SetBadge(NavTab.Tasks, _state.OpenTasks, _state.HasOverdueTasks);
            nav.SetBadge(NavTab.Recents, _state.NewMissed, alert: false);
        }

        private async Task PollAsync()
        {
            var ok = true;

            // Ask TaskService rather than keeping a latch of our own. Its TasksForbidden is
            // scoped to the access token that drew the 403, so it clears itself when the
            // session changes — and a local bool here would outlive that, which is the
            // exact bug the token scoping was added to fix, just one layer up: we would
            // stop calling GetMyStatsAsync, so the token behind the latch would never be
            // re-examined, and an operator who does have tasks:read would keep seeing a
            // dead badge until the app restarted.
            if (!App.TaskService.TasksForbidden)
            {
                var stats = await App.TaskService.GetMyStatsAsync();
                if (stats != null)
                {
                    _state.SetTasks(stats.Pending, stats.InProgress, stats.Overdue);
                }
                else if (App.TaskService.TasksForbidden)
                {
                    // A permission this role does not have will not appear in two minutes,
                    // so stop asking until the session changes.
                    _state.SetTasks(0, 0, 0);
                    AppLogger.Log("NavBadges", "Tasks polling paused: the backend refused tasks:read.");
                }
                else
                {
                    ok = false;
                }
            }

            var missed = await LoadMissedCallsAsync();
            if (missed.HasValue) _state.SetMissed(missed.Value);
            else ok = false;

            // Failures are logged, never raised as a banner. A badge is not worth
            // interrupting a call over, and a backend down all shift would otherwise put
            // one on screen every two minutes.
            //
            // Silence has to be asked for, not assumed: the banner is raised inside
            // TaskService.SendAsync, synchronously, so there is nothing here to intercept.
            // GetMyStatsAsync passes notifyErrors: false for that reason, and
            // LoadMissedCallsAsync below — which does not go through TaskService — keeps
            // the same rule by hand, logging and nothing more.
            _consecutiveFailures = ok ? 0 : _consecutiveFailures + 1;
            if (_timer != null)
                _timer.Interval = PollBackoff.Next(_consecutiveFailures, Healthy, MaxInterval);

            Raise();
        }

        /// <summary>
        /// The same endpoint OperatorStatsControl used to call on its own timer. Returns
        /// null on any failure, so the badge keeps its last known value: a stale number is
        /// a smaller lie than a zero.
        /// </summary>
        private async Task<int?> LoadMissedCallsAsync()
        {
            try
            {
                var settings = App.SipService?.CurrentSettings ?? SipSettings.Load();
                var operatorId = settings.DecodedToken?.Operator?.Username ?? settings.Username;
                var backendUrl = settings.BackendUrl?.TrimEnd('/');

                if (string.IsNullOrEmpty(operatorId) || string.IsNullOrEmpty(backendUrl))
                    return null;

                var url = $"{backendUrl}/api/contact-center/operators/{Uri.EscapeDataString(operatorId)}/details?range=today";

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (!string.IsNullOrEmpty(settings.AccessToken))
                    request.Headers.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.AccessToken);

                using var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    AppLogger.Log("NavBadges", $"Operator details failed: {response.StatusCode}");
                    return null;
                }

                var data = await response.Content.ReadFromJsonAsync<OperatorDetailsResponse>();
                if (data?.Stats == null) return null;

                OperatorStats = data.Stats;
                return data.Stats.MissedCalls;
            }
            catch (Exception ex)
            {
                AppLogger.Log("NavBadges", $"Operator details error: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        private void Raise() => Dispatcher.UIThread.InvokeAsync(() => Changed?.Invoke());

        public void Dispose() => Stop();
    }
}
