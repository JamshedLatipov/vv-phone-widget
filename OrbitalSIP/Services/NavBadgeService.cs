using Avalonia.Threading;
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using OrbitalSIP.Models;

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
    ///
    /// Reports numbers and knows nothing about the bar that draws them. It used to hand
    /// them to a BottomNavControl itself, which made this the only type under Services or
    /// Models that referenced Views — and, because that was the sole way out, made the
    /// numbers unassertable from a test. MainWindow.RefreshChrome pushes them now, beside
    /// the other pieces of state it already pushes into a freshly built bar.
    /// </summary>
    public sealed class NavBadgeService : IDisposable
    {
        private static readonly TimeSpan Healthy = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan MaxInterval = TimeSpan.FromMinutes(10);

        private readonly NavBadgeState _state = new();
        private readonly HttpClient _httpClient;
        private readonly Func<SipSettings> _settingsProvider;
        private readonly Func<TaskService> _taskServiceProvider;
        private readonly SemaphoreSlim _pollGate = new(1, 1);
        private DispatcherTimer? _timer;
        private int _consecutiveFailures;
        private TimeSpan _interval = Healthy;

        /// <summary>
        /// Which session's polls count. Bumped by <see cref="Stop"/>, captured by every
        /// poll as it starts, and checked again after each await before anything is
        /// written — because a poll already past the gate does not stop when the session
        /// does, and its writes would land in the next one.
        /// </summary>
        private int _generation;

        public NavBadgeService()
            : this(BackendHttp.Client,
                   () => App.SipService?.CurrentSettings ?? SipSettings.Load(),
                   () => App.TaskService)
        {
        }

        /// <summary>
        /// Injectable overload, same shape TaskService, SmsService and ScriptService
        /// already use, so a poll can be driven from a test without an App or a window.
        ///
        /// The task service arrives as a factory rather than an instance because both live
        /// in App's static field list: taking App.TaskService in this constructor would
        /// read a field that happens to be initialised earlier in declaration order today,
        /// and would silently become null the day someone reordered them.
        /// </summary>
        public NavBadgeService(HttpClient httpClient,
                               Func<SipSettings> settingsProvider,
                               Func<TaskService> taskServiceProvider)
        {
            _httpClient = httpClient;
            _settingsProvider = settingsProvider;
            _taskServiceProvider = taskServiceProvider;
        }

        /// <summary>
        /// Raised on the UI thread at the end of every poll, and when Recents is opened.
        /// Unconditional: a poll where both fetches failed raises it too, having changed
        /// nothing. Subscribers redraw from the numbers below rather than from an argument,
        /// so a redundant raise costs a repaint of values that did not move.
        /// </summary>
        public event Action? Changed;

        /// <summary>Latest operator stats, for whoever wants more than a badge out of them.</summary>
        public OperatorStats? OperatorStats { get; private set; }

        /// <summary>Open tasks — pending plus in progress. The Tasks badge.</summary>
        public int OpenTasks => _state.OpenTasks;

        /// <summary>True when one of those open tasks is past due, which reddens the badge.</summary>
        public bool HasOverdueTasks => _state.HasOverdueTasks;

        /// <summary>Missed calls not looked at since Recents was last opened.</summary>
        public int NewMissed => _state.NewMissed;

        /// <summary>
        /// How far apart the polls currently are — <see cref="Healthy"/> until the backend
        /// starts failing, then stretched by <see cref="PollBackoff"/>. Public because the
        /// backoff is otherwise invisible from outside, and "the badge quietly dropped to a
        /// ten-minute refresh" is precisely the failure that hid inside it.
        /// </summary>
        public TimeSpan PollInterval => _interval;

        public void Start()
        {
            if (_timer != null) return;

            _timer = new DispatcherTimer { Interval = _interval };
            _timer.Tick += async (_, __) => await PollAsync();
            _timer.Start();

            _ = PollAsync();
        }

        public void Stop()
        {
            _timer?.Stop();
            _timer = null;

            // Stopping the timer stops the *next* poll, not the one already running: a slow
            // backend makes that window seconds wide, and the poll on the other side of it
            // would then write the outgoing operator's missed total, re-open the streak this
            // method just reset, and set the interval on a _timer that by then belongs to
            // the next session. The reset below says the streak ends with the session; this
            // is what makes that true of the poll in flight as well.
            _generation++;

            // The streak describes how badly the backend is answering *this* session, so it
            // ends with the session — same reason StatusService.StopPolling calls
            // ResetBackoff. Carried over, a streak run up while an expired token was still
            // being polled would meet the next operator's first poll already at the ten-minute
            // cap, and one unlucky failure there would leave their badges stale for ten
            // minutes of a session whose backend is fine.
            _consecutiveFailures = 0;
            _interval = Healthy;
        }

        /// <summary>Polls right now — after ticking a task off, or after a call ends.</summary>
        public Task RefreshNowAsync() => PollAsync();

        public void MarkRecentsSeen()
        {
            _state.MarkRecentsSeen();
            Raise();
        }

        private async Task PollAsync()
        {
            // Serialised, never dropped. RefreshNowAsync exists so the numbers move the
            // moment a task is ticked off, so a caller that arrives while the timer's poll
            // is in flight has to wait its turn — an _isPolling flag that turned it away
            // would make that call silently do nothing. Two polls at once could also land
            // their responses out of order, and an older "missed today" total arriving
            // after a newer one reads as a midnight rollover: the watermark goes to zero
            // and every call of the day shows as new. Contention is rare at two-minute
            // intervals, and waiting for the other poll costs nothing.
            // Captured before the wait, not after: a poll queued behind another one can sit
            // here across the whole handover, and it belongs to the session it was asked
            // for rather than the one it happens to wake up in.
            var generation = _generation;

            await _pollGate.WaitAsync();
            try
            {
                await PollOnceAsync(generation);
            }
            finally
            {
                _pollGate.Release();
            }
        }

        /// <summary>Nothing this poll learned belongs to the session now in play.</summary>
        private bool Superseded(int generation) => generation != _generation;

        private async Task PollOnceAsync(int generation)
        {
            if (Superseded(generation)) return;

            var ok = true;

            // Both halves below read the session, and there is an await between them. Read
            // once so they cannot end up describing two different sessions — which is what
            // OperatorIdOf's comment promises, and what two independent reads would quietly
            // stop being true.
            var settings = _settingsProvider();
            var tasks = _taskServiceProvider();

            // Before anything else writes to _state, not after: a handover on a shared
            // terminal keeps the same process, and the numbers this poll is about to fetch
            // belong to whoever is signed in now.
            //
            // OperatorStats goes with them. It is a field here rather than part of the
            // state, but its lifetime is the same session, and left behind it would hand
            // the incoming operator the outgoing one's call figures the moment they
            // expanded the dialer. Cleared on the answer SetOperator already gives rather
            // than on a comparison of our own, so there is one definition of "the operator
            // changed" and no second copy to drift against it.
            if (_state.SetOperator(OperatorIdOf(settings)))
                OperatorStats = null;

            if (tasks.TasksUnassignable)
            {
                // Nothing to ask: the JWT carries no user id the tasks API can be queried
                // by, and it will not grow one before the session changes. Counted as a
                // failure — which is what treating the null answer as one did — it would
                // climb the backoff on every poll and drag the missed-calls half, whose
                // endpoint is answering perfectly, down to a ten-minute refresh for the
                // whole shift. Every SSO operator is in this state permanently, because
                // their token's sub is an opaque string. So: badge cleared, ok untouched,
                // and TaskService logs it once per token rather than once per poll.
                _state.SetTasks(0, 0, 0);
            }
            // Ask TaskService rather than keeping a latch of our own. Its TasksForbidden is
            // scoped to the access token that drew the 403, so it clears itself when the
            // session changes — and a local bool here would outlive that, which is the
            // exact bug the token scoping was added to fix, just one layer up: we would
            // stop calling GetMyStatsAsync, so the token behind the latch would never be
            // re-examined, and an operator who does have tasks:read would keep seeing a
            // dead badge until the app restarted.
            else if (!tasks.TasksForbidden)
            {
                var stats = await tasks.GetMyStatsAsync();
                if (Superseded(generation)) return;

                if (stats != null)
                {
                    _state.SetTasks(stats.Pending, stats.InProgress, stats.Overdue);
                }
                else if (tasks.TasksForbidden)
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

            var operatorStats = await LoadMissedCallsAsync(settings);
            if (Superseded(generation)) return;

            if (operatorStats != null)
            {
                OperatorStats = operatorStats;
                _state.SetMissed(operatorStats.MissedCalls);
            }
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
            _interval = PollBackoff.Next(_consecutiveFailures, Healthy, MaxInterval);
            if (_timer != null)
                _timer.Interval = _interval;

            Raise();
        }

        /// <summary>
        /// The same endpoint OperatorStatsControl used to call on its own timer. Returns
        /// null on any failure, so the badge keeps its last known value: a stale number is
        /// a smaller lie than a zero.
        ///
        /// Hands the figures back rather than assigning OperatorStats itself, so every
        /// write this poll makes happens in one place the caller can gate on the session —
        /// this method is on the far side of an await, and the session can end across it.
        /// </summary>
        private async Task<OperatorStats?> LoadMissedCallsAsync(SipSettings settings)
        {
            try
            {
                var operatorId = OperatorIdOf(settings);
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
                return data?.Stats;
            }
            catch (Exception ex)
            {
                AppLogger.Log("NavBadges", $"Operator details error: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Whose numbers these are. The same value the request URL is built from, so the
        /// state and the fetch can never disagree about who was asked about.
        /// </summary>
        private static string? OperatorIdOf(SipSettings settings) =>
            settings.DecodedToken?.Operator?.Username ?? settings.Username;

        private void Raise() => Dispatcher.UIThread.InvokeAsync(() => Changed?.Invoke());

        /// <summary>
        /// Stops the timer, and deliberately leaves the gate alone. A poll may be holding
        /// it — a slow backend makes that window seconds wide — and its finally would then
        /// call Release on a disposed semaphore, throwing ObjectDisposedException into an
        /// async void timer handler with nobody to catch it. SemaphoreSlim only owns a
        /// disposable resource once AvailableWaitHandle has been asked for, which nothing
        /// here does, so there is nothing being leaked by not disposing it.
        /// </summary>
        public void Dispose() => Stop();
    }
}
