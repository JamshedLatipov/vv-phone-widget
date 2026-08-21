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
                if (openOnly)
                {
                    var pending = await service.GetMyTasksAsync("pending", ct);
                    if (pending?.Data != null) tasks.AddRange(pending.Data);

                    // Only ask the second time if the first was not refused outright. A role
                    // without tasks:read draws a banner per request, and one missing
                    // permission is worth exactly one banner — the same reason
                    // NavBadgeService stops polling on TasksForbidden rather than asking
                    // again every two minutes.
                    if (!service.TasksForbidden)
                    {
                        var running = await service.GetMyTasksAsync("in_progress", ct);
                        if (running?.Data != null) tasks.AddRange(running.Data);
                    }

                    tasks = tasks
                        .GroupBy(t => t.Id)
                        .Select(g => g.First())
                        .OrderByDescending(t => t.CreatedAt ?? DateTimeOffset.MinValue)
                        .ToList();
                }
                else
                {
                    var all = await service.GetMyTasksAsync(null, ct);
                    if (all?.Data != null) tasks.AddRange(all.Data);
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    // Cancellation can arrive after the last response has already landed —
                    // a chip pressed while this was between its own await and its repaint —
                    // and InvokeAsync queues rather than running inline, which widens that
                    // window further. Without this check a superseded load repaints the
                    // list it was told to abandon, under the other chip.
                    if (ct.IsCancellationRequested) return;

                    _listGeneration++;
                    _items.Clear();
                    foreach (var task in tasks)
                        _items.Add(new TaskItemViewModel(task, now));

                    ShowActionError(null);
                    ShowMessage(tasks.Count > 0
                        ? null
                        : I18nService.Instance.Get(service.TasksForbidden ? "TasksNoAccess" : "TasksEmpty"));
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
                AppLogger.Log("TasksView", $"Error loading tasks: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (ReferenceEquals(_loadCts, cts)) _loadCts = null;
                cts.Dispose();
            }
        }

        /// <summary>What the list is doing when it has nothing to show: empty, or refused.</summary>
        private void ShowMessage(string? message)
        {
            var label = this.FindControl<TextBlock>("TasksMessageLabel");
            if (label == null) return;

            label.Text = message ?? string.Empty;
            label.IsVisible = message != null;
        }

        /// <summary>An action that did not work, which is a different thing from an empty list.</summary>
        private void ShowActionError(string? message)
        {
            var label = this.FindControl<TextBlock>("TaskActionErrorLabel");
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

            ShowActionError(null);
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

                ShowActionError(I18nService.Instance.Get("TaskDoneFailed"));
            });
        }
    }
}
