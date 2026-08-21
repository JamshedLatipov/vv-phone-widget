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

        /// <summary>Which filter the rows currently on screen were fetched for.</summary>
        private bool _shownOpenOnly = true;

        /// <summary>
        /// True when a detach cancelled a load that had not finished, so the re-attach can
        /// ask for it again. See <see cref="OnDetachedFromVisualTree"/>.
        /// </summary>
        private bool _reloadOnAttach;

        /// <summary>
        /// The load still in flight, so a newer one can call it off.
        ///
        /// Not an _isLoading flag that turns the newer load away. The chips light before
        /// the load they ask for has answered, so a load dropped on the floor would leave
        /// the All chip lit over the open list with nothing to put it right: no request was
        /// made, so no answer and no failure is coming, and the operator's last tap simply
        /// did not happen. Cancelling instead means the newest tap always wins, and a load
        /// that fails hands the chip back through <see cref="RevertFilterChip"/> — between
        /// them, the chip describes the rows.
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

        /// <summary>
        /// Calls off a load the operator has walked away from.
        ///
        /// Navigating away discards this screen, so a response still in flight would be
        /// parsed, merged and drawn into a list nobody can see — one orphaned round trip
        /// per visit. Cancelling is silent by design, so nothing is said about it.
        ///
        /// The animated content swap moves this same instance from the overlay to the
        /// host, which detaches and re-attaches it — on a screen that is about to come
        /// straight back, and typically while the constructor's own load is still in
        /// flight. Remembering that the cancel happened is what stops that swap from
        /// leaving the screen permanently empty.
        /// </summary>
        protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);

            _reloadOnAttach = _loadCts != null;
            _loadCts?.Cancel();
        }

        protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            if (!_reloadOnAttach) return;

            _reloadOnAttach = false;
            _ = LoadAsync();
        }

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
            DrawFilterChips();

            _ = LoadAsync();
        }

        private void DrawFilterChips()
        {
            this.FindControl<Button>("OpenFilterBtn")?.Classes.Set("selected", _openOnly);
            this.FindControl<Button>("AllFilterBtn")?.Classes.Set("selected", !_openOnly);
        }

        /// <summary>
        /// Hands the chip back to the filter the rows on screen belong to.
        ///
        /// The chip lights on the tap rather than on the answer, because one that waited
        /// for the network would read as a dead button. When the load it asked for fails
        /// the rows stay — deliberately, they are the only tasks anyone has — so without
        /// this the All chip would sit lit over the open list, describing a set of tasks
        /// that is not on screen and is not coming.
        /// </summary>
        private void RevertFilterChip()
        {
            if (_openOnly == _shownOpenOnly) return;

            _openOnly = _shownOpenOnly;
            DrawFilterChips();
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

                    Apply(state, tasks, openOnly, now);
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
        private void Apply(TaskListState state, IReadOnlyList<TaskItem> tasks, bool openOnly, DateTimeOffset now)
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
            _shownOpenOnly = openOnly;

            // A load that answered supersedes everything either label was carrying: both
            // of them described a state of affairs this answer has just replaced.
            ShowError(Note.None);
            ShowMessage(state switch
            {
                TaskListState.Refused => Note.NoAccess,
                TaskListState.Empty   => Note.Empty,
                _                     => Note.None,
            });
        }

        /// <summary>
        /// Says a load did not answer, in the place that suits what is on screen: centred
        /// when there is nothing else there, and under the rows when there are rows, since
        /// the centred label lies across them.
        /// </summary>
        private void Fail()
        {
            RevertFilterChip();

            if (_items.Count == 0) { ShowError(Note.None); ShowMessage(Note.LoadFailed); }
            else { ShowMessage(Note.None); ShowError(Note.LoadFailed); }
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

        /// <summary>
        /// Which sentence a label is carrying, so that taking one down can be about the
        /// sentence rather than about the label. Both labels are shared — the bottom one
        /// carries a failed load and a failed tap — and clearing by control rather than by
        /// meaning wiped whatever the other feature had put there.
        /// </summary>
        private enum Note
        {
            None,
            Empty,
            NoAccess,
            LoadFailed,
            DoneFailed,
        }

        /// <summary>What the centred label is saying. Only ever set over an empty list.</summary>
        private Note _message = Note.None;

        /// <summary>What the bottom label is saying.</summary>
        private Note _error = Note.None;

        private static string TextOf(Note note) => note switch
        {
            Note.Empty      => I18nService.Instance.Get("TasksEmpty"),
            Note.NoAccess   => I18nService.Instance.Get("TasksNoAccess"),
            Note.LoadFailed => I18nService.Instance.Get("TasksLoadFailed"),
            Note.DoneFailed => I18nService.Instance.Get("TaskDoneFailed"),
            _               => string.Empty,
        };

        /// <summary>What the list is doing when it has nothing to show: empty, or refused.</summary>
        private void ShowMessage(Note note)
        {
            _message = note;

            var label = this.FindControl<TextBlock>("TasksMessageLabel");
            if (label == null) return;

            label.Text = TextOf(note);
            label.IsVisible = note != Note.None;
        }

        /// <summary>
        /// Something that did not work just now — a load, or a tap — which is a different
        /// thing from what <see cref="ShowMessage"/> says about the list itself, and can be
        /// on screen at the same time as a list that is perfectly good.
        /// </summary>
        private void ShowError(Note note)
        {
            _error = note;

            var label = this.FindControl<TextBlock>("TasksErrorLabel");
            if (label == null) return;

            label.Text = TextOf(note);
            label.IsVisible = note != Note.None;
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

            // Only a previous tap's failure, never a load's. That one is still true — the
            // last load really did fail — and if this tap succeeds nothing would ever put
            // it back.
            if (_error == Note.DoneFailed) ShowError(Note.None);

            _items.RemoveAt(index);
            if (_items.Count == 0) ShowMessage(Note.Empty);

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

                    // The centred label only ever belongs over an empty list, and the list
                    // is not empty any more — so it goes, whether it was the "no tasks"
                    // this tap put there or a load that failed underneath it since. The
                    // failure below replaces it either way, and both would be saying the
                    // same thing about the same backend.
                    ShowMessage(Note.None);
                }

                ShowError(Note.DoneFailed);
            });
        }
    }
}
