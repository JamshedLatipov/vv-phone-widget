using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OrbitalSIP.Models;
using OrbitalSIP.Services;
using OrbitalSIP.ViewModels;

namespace OrbitalSIP.Views
{
    /// <summary>
    /// The operator's own tasks. Read-only apart from one action — tick a task off —
    /// because everything richer needs the CRM, and this panel is 320 pixels wide.
    /// </summary>
    public partial class TasksView : UserControl
    {
        private readonly ObservableCollection<TaskItemViewModel> _items = new();
        private bool _openOnly = true;

        /// <summary>
        /// The load still in flight, so a newer one can call it off.
        ///
        /// Not an _isLoading flag that turns the newer load away: the chips change
        /// <see cref="_openOnly"/> before they ask for a load, so a refused load leaves the
        /// All chip lit over a list of open tasks — the screen would be lying about what it
        /// is showing, and nothing would ever correct it.
        /// </summary>
        private CancellationTokenSource? _loadCts;

        /// <summary>
        /// Bumped every time the list is rebuilt from a response.
        ///
        /// Lets <see cref="OnTaskDoneClicked"/> tell "the list I took a row out of" from
        /// "a list that has been rebuilt since", which is what decides whether putting the
        /// row back is a restore or a duplicate.
        /// </summary>
        private int _listGeneration;

        public event EventHandler? OnCloseRequested;
        public event EventHandler? OnExitAppRequested;

        public TasksView()
        {
            InitializeComponent();
            WireButtons();

            var list = this.FindControl<ItemsControl>("TaskItemsControl");
            if (list != null) list.ItemsSource = _items;

            _ = LoadAsync();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void WireButtons()
        {
            var topBar = this.FindControl<TopBarControl>("TopBar");
            if (topBar != null)
            {
                topBar.OnMinimizeRequested += (_, __) => OnCloseRequested?.Invoke(this, EventArgs.Empty);
                topBar.OnCloseRequested += (_, __) => OnExitAppRequested?.Invoke(this, EventArgs.Empty);
            }

            var refresh = this.FindControl<Button>("RefreshTasksBtn");
            if (refresh != null) refresh.Click += (_, __) => _ = LoadAsync();

            var openBtn = this.FindControl<Button>("OpenFilterBtn");
            if (openBtn != null) openBtn.Click += (_, __) => SetFilter(openOnly: true);

            var allBtn = this.FindControl<Button>("AllFilterBtn");
            if (allBtn != null) allBtn.Click += (_, __) => SetFilter(openOnly: false);
        }

        private void SetFilter(bool openOnly)
        {
            if (_openOnly == openOnly) return;
            _openOnly = openOnly;

            this.FindControl<Button>("OpenFilterBtn")?.Classes.Set("selected", openOnly);
            this.FindControl<Button>("AllFilterBtn")?.Classes.Set("selected", !openOnly);

            _ = LoadAsync();
        }

        /// <summary>
        /// Loads the current filter.
        ///
        /// "Open" is two requests, not one: the backend's pending filter is literally
        /// NOT IN ('in_progress', 'done', 'completed'), so asking for pending alone would
        /// hide every task the operator has already started — and disagree with the badge,
        /// which counts both.
        ///
        /// Whatever is still in flight is cancelled first. TaskService.SendAsync rethrows a
        /// caller's cancellation instead of reporting it, precisely so that this reads as
        /// "that load never happened" rather than as a failed request: the operator's own
        /// tap on the other chip must not raise a banner.
        /// </summary>
        private async Task LoadAsync()
        {
            var cts = new CancellationTokenSource();
            var superseded = _loadCts;
            _loadCts = cts;

            // After the field, never before: Cancel resumes the superseded load, and it
            // checks this same field to see whether it is still the current one.
            superseded?.Cancel();

            var ct = cts.Token;
            var service = App.TaskService;
            var now = DateTimeOffset.Now;
            var openOnly = _openOnly;

            try
            {
                var tasks = new List<TaskItem>();
                var first = TaskFetch.Skipped;
                var second = TaskFetch.Skipped;

                // Neither of these changes before the session does, so neither is worth a
                // request. Asking anyway would draw a fresh banner over an answer already
                // given — one per visit to the tab, since every press of it builds this
                // screen anew, and one more per filter switch, right next to a sentence
                // telling the operator they have no access. NavBadgeService stops polling
                // on the same signal for the same reason. Both are scoped to the access
                // token, so a refresh or a re-login gets to try again.
                if (!service.TasksForbidden && !service.TasksUnassignable)
                {
                    if (openOnly)
                    {
                        var pending = await service.GetMyTasksAsync("pending", ct);
                        first = FetchOf(pending);

                        // Only ask the second time if the first answered. Half a merge is
                        // not shown at all, so once the pending half is missing the
                        // in_progress half is a request whose answer is already destined
                        // for the bin — and one more banner over the same outage.
                        List<TaskItem>? running = null;
                        if (first == TaskFetch.Answered)
                        {
                            var response = await service.GetMyTasksAsync("in_progress", ct);
                            second = FetchOf(response);
                            running = response?.Data;
                        }

                        tasks = OpenTaskList.From(pending?.Data, running);
                    }
                    else
                    {
                        var all = await service.GetMyTasksAsync(null, ct);
                        first = FetchOf(all);

                        // Left in the backend's own order, unlike the open pair, which has
                        // to be merged before it means anything. Null rows are dropped for
                        // the same reason OpenTaskList.From drops them: "data": [null].
                        if (all?.Data != null)
                            tasks.AddRange(all.Data.Where(task => task is not null));
                    }
                }

                var state = TaskListOutcome.Of(new[] { first, second }, tasks.Count,
                                               service.TasksForbidden, service.TasksUnassignable);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    // Cancellation can arrive after the last response has already landed —
                    // a chip pressed while this was between its own await and its repaint —
                    // and InvokeAsync queues rather than running inline, which widens that
                    // window further. Without this check a superseded load repaints the
                    // list it was told to abandon, under the other chip.
                    if (ct.IsCancellationRequested) return;

                    Apply(state, tasks, now);
                });
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Superseded by a newer load. Nothing to show and nothing to log: the list
                // has not been touched, and the load that replaced this one will say what
                // is on screen.
            }
            catch (Exception ex)
            {
                // Whatever this was, the load did not answer, and the operator is owed the
                // same sentence as any other failure. Logged and otherwise silent, this was
                // a fourth state hiding behind the three above: stale rows, or a stale "no
                // tasks", with nothing on screen to say anything had happened.
                AppLogger.Log("TasksView", $"Error loading tasks: {ex.GetType().Name}: {ex.Message}");

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (ct.IsCancellationRequested) return;
                    Fail();
                });
            }
            finally
            {
                if (ReferenceEquals(_loadCts, cts)) _loadCts = null;
                cts.Dispose();
            }
        }

        /// <summary>
        /// What one request came back with. A response whose Data did not parse counts as
        /// a failure and not as an empty list — TaskService already reads an empty body
        /// that way, and "you have no tasks" is not a thing to say on a guess.
        /// </summary>
        private static TaskFetch FetchOf(TaskListResponse? response) =>
            response?.Data != null ? TaskFetch.Answered : TaskFetch.Failed;

        /// <summary>
        /// Puts one load's answer on screen: the rows, and the sentence that goes with
        /// them. Which answer it is was decided by <see cref="TaskListOutcome"/>, out where
        /// a test can reach it; this is only the mapping onto controls.
        /// </summary>
        private void Apply(TaskListState state, IReadOnlyList<TaskItem> tasks, DateTimeOffset now)
        {
            if (state == TaskListState.Failed)
            {
                // Nothing was learned, so nothing is claimed: whatever is on screen stays,
                // which is the trade NavBadgeService.LoadMissedCallsAsync already makes
                // with its counters — a stale list is a smaller lie than an empty one.
                Fail();
                return;
            }

            // Refused replaces the list rather than leaving stale rows under a sentence
            // saying the operator cannot see them.
            ReplaceItems(state == TaskListState.Refused ? null : tasks, now);
            ShowError(null);
            ShowMessage(state switch
            {
                TaskListState.Refused => I18nService.Instance.Get("TasksNoAccess"),
                TaskListState.Empty   => I18nService.Instance.Get("TasksEmpty"),
                _                     => null,
            });
        }

        /// <summary>
        /// Says a load did not answer, in the place that suits what is on screen: centred
        /// when there is nothing else there, and under the rows when there are rows, since
        /// the centred label lies across them.
        /// </summary>
        private void Fail()
        {
            var failed = I18nService.Instance.Get("TasksLoadFailed");

            if (_items.Count == 0) { ShowError(null); ShowMessage(failed); }
            else { ShowMessage(null); ShowError(failed); }
        }

        /// <summary>
        /// Rebuilds the list from a response, or empties it when there is no list to show.
        ///
        /// The one place rows are replaced, so that "the list was rebuilt" and "the
        /// generation moved" cannot come apart — <see cref="OnTaskDoneClicked"/> decides
        /// whether restoring a row is a restore or a duplicate by comparing them.
        /// </summary>
        private void ReplaceItems(IReadOnlyList<TaskItem>? tasks, DateTimeOffset now)
        {
            _listGeneration++;
            _items.Clear();

            if (tasks == null) return;
            foreach (var task in tasks)
                _items.Add(new TaskItemViewModel(task, now));
        }

        /// <summary>What the list is doing when it has nothing to show: empty, or refused.</summary>
        private void ShowMessage(string? message)
        {
            var label = this.FindControl<TextBlock>("TasksMessageLabel");
            if (label == null) return;

            label.Text = message ?? string.Empty;
            label.IsVisible = message != null;
        }

        /// <summary>
        /// Something that did not work just now — a load, or a tap — which is a different
        /// thing from what <see cref="ShowMessage"/> says about the list itself, and can be
        /// on screen at the same time as a list that is perfectly good.
        /// </summary>
        private void ShowError(string? message)
        {
            var label = this.FindControl<TextBlock>("TasksErrorLabel");
            if (label == null) return;

            label.Text = message ?? string.Empty;
            label.IsVisible = !string.IsNullOrWhiteSpace(message);
        }

        /// <summary>
        /// Ticks a task off. The row goes immediately and comes back if the PATCH fails —
        /// the operator is usually mid-call here, and waiting on a round trip to find out
        /// whether the tap registered is the wrong trade.
        /// </summary>
        private async void OnTaskDoneClicked(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: TaskItemViewModel row }) return;

            var index = _items.IndexOf(row);
            if (index < 0) return;

            var generation = _listGeneration;

            ShowError(null);
            _items.RemoveAt(index);
            if (_items.Count == 0) ShowMessage(I18nService.Instance.Get("TasksEmpty"));

            if (await App.TaskService.SetStatusAsync(row.Id, "done"))
            {
                await App.NavBadges.RefreshNowAsync();
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // Only back into the list it came out of. A load that finished while the
                // PATCH was in flight — a refresh, or the other chip — has already asked
                // the backend, which still has the task open, so the row is in the new list
                // already and putting it back would show it twice at a stale position.
                if (generation == _listGeneration)
                {
                    _items.Insert(Math.Min(index, _items.Count), row);
                    ShowMessage(null);
                }

                ShowError(I18nService.Instance.Get("TaskDoneFailed"));
            });
        }
    }
}
