using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using OrbitalSIP.Models;

namespace OrbitalSIP.Views
{
    public partial class StatusPopupControl : UserControl
    {
        private readonly DispatcherTimer _liveTimer;

        public event EventHandler? OnCloseRequested;
        public event EventHandler<(string status, int duration)>? OnStatusUpdateRequested;

        public StatusPopupControl()
        {
            InitializeComponent();

            _liveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _liveTimer.Tick += (_, __) => UpdateActiveStatusText();

            AttachedToVisualTree += (_, __) => App.StatusService.StateChanged += OnStatusStateChanged;
            DetachedFromVisualTree += (_, __) =>
            {
                _liveTimer.Stop();
                App.StatusService.StateChanged -= OnStatusStateChanged;
            };

            WireButtons();
            UpdateUIFromCurrentState();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void WireButtons()
        {
            var cancelBtn = this.FindControl<Button>("CancelBtn");
            if (cancelBtn != null)
                cancelBtn.Click += (_, __) => OnCloseRequested?.Invoke(this, EventArgs.Empty);

            var updateBtn = this.FindControl<Button>("UpdateStatusBtn");
            if (updateBtn != null)
                updateBtn.Click += (_, __) =>
                {
                    var status = GetSelectedStatus();
                    var duration = GetSelectedDuration();
                    OnStatusUpdateRequested?.Invoke(this, (status, duration));
                };

            var endNowBtn = this.FindControl<Button>("EndNowBtn");
            if (endNowBtn != null)
                endNowBtn.Click += (_, __) =>
                {
                    OnStatusUpdateRequested?.Invoke(this, ("online", 0));
                };

            WireStatusCard("StatusOnline",   isAway: false);
            WireStatusCard("StatusOffline",  isAway: false);
            WireStatusCard("StatusBreak",    isAway: true);
            WireStatusCard("StatusMeeting",  isAway: true);
            WireStatusCard("StatusTraining", isAway: true);
            WireStatusCard("StatusDnd",      isAway: true);
            WireStatusCard("StatusPause",    isAway: true);
        }

        private void WireStatusCard(string name, bool isAway)
        {
            var rb = this.FindControl<RadioButton>(name);
            if (rb == null) return;
            rb.IsCheckedChanged += (_, __) =>
            {
                if (rb.IsChecked == true)
                {
                    var panel = this.FindControl<StackPanel>("DurationPanel");
                    if (panel != null) panel.IsVisible = isAway;
                }
            };
        }

        private string GetSelectedStatus()
        {
            string[] names = { "StatusOnline", "StatusOffline", "StatusBreak", "StatusMeeting", "StatusTraining", "StatusDnd", "StatusPause" };
            foreach (var name in names)
            {
                var rb = this.FindControl<RadioButton>(name);
                if (rb?.IsChecked == true)
                    return rb.Tag?.ToString() ?? "online";
            }
            return "online";
        }

        private int GetSelectedDuration()
        {
            var durGrid = this.FindControl<UniformGrid>("DurationGrid");
            if (durGrid != null)
            {
                foreach (var child in durGrid.Children)
                {
                    if (child is RadioButton rb && rb.IsChecked == true)
                    {
                        if (int.TryParse(rb.Tag?.ToString(), out int val))
                            return val;
                    }
                }
            }
            return 0;
        }

        private void UpdateUIFromCurrentState()
        {
            var svc = App.StatusService;
            var state = svc.CurrentState;

            // Supervisor-pause lock: the operator cannot change status while a
            // supervisor has force-paused them (the server enforces this too).
            ApplySupervisorLock(state != null && state.IsSupervisorPaused);

            var panel = this.FindControl<Border>("ActiveStatusPanel");
            var text  = this.FindControl<TextBlock>("ActiveStatusText");

            if (state != null && state.Paused && panel != null && text != null)
            {
                panel.IsVisible = true;
                UpdateActiveStatusText();

                if (svc.BreakEndTime.HasValue)
                    _liveTimer.Start();
                else
                    _liveTimer.Stop();

                // Pre-select the matching status button
                string radioName = (state.ReasonPaused?.ToLower() ?? "") switch
                {
                    "break"    => "StatusBreak",
                    "meeting"  => "StatusMeeting",
                    "training" => "StatusTraining",
                    "dnd"      => "StatusDnd",
                    "pause"    => "StatusPause",
                    "offline"  => "StatusOffline",
                    _          => "StatusOnline"
                };
                var rb = this.FindControl<RadioButton>(radioName);
                if (rb != null) rb.IsChecked = true;

                var durationPanel = this.FindControl<StackPanel>("DurationPanel");
                if (durationPanel != null)
                    durationPanel.IsVisible = radioName is "StatusBreak" or "StatusMeeting"
                        or "StatusTraining" or "StatusDnd" or "StatusPause";
            }
            else if (panel != null)
            {
                panel.IsVisible = false;
                _liveTimer.Stop();
            }
        }

        private void ApplySupervisorLock(bool locked)
        {
            var lockPanel = this.FindControl<Border>("SupervisorLockPanel");
            if (lockPanel != null)
                lockPanel.IsVisible = locked;

            var grid = this.FindControl<Grid>("StatusGrid");
            if (grid != null)
            {
                grid.IsEnabled = !locked;
                grid.Opacity = locked ? 0.4 : 1.0;
            }

            var updateBtn = this.FindControl<Button>("UpdateStatusBtn");
            if (updateBtn != null)
                updateBtn.IsEnabled = !locked;

            // The operator cannot lift a supervisor pause themselves.
            var endNowBtn = this.FindControl<Button>("EndNowBtn");
            if (endNowBtn != null)
                endNowBtn.IsVisible = !locked;

            // While supervisor-paused there is no operator-set duration to show.
            if (locked)
            {
                var durationPanel = this.FindControl<StackPanel>("DurationPanel");
                if (durationPanel != null)
                    durationPanel.IsVisible = false;
            }
        }

        private void OnStatusStateChanged(StatusState _)
        {
            UpdateUIFromCurrentState();
        }

        private void UpdateActiveStatusText()
        {
            var text = this.FindControl<TextBlock>("ActiveStatusText");
            if (text == null)
                return;

            var svc = App.StatusService;
            var state = svc.CurrentState;

            if (state != null && state.IsSupervisorPaused)
            {
                text.Text = Services.I18nService.Instance.Get("SupervisorPaused");
                return;
            }

            var label = PresenceLabel(state?.ReasonPaused);

            if (state?.Paused == true && svc.BreakEndTime.HasValue)
            {
                var left = svc.BreakEndTime.Value - DateTime.Now;
                if (left.TotalSeconds > 0)
                {
                    text.Text = $"{label}: {left.Minutes:D2}:{left.Seconds:D2}";
                    return;
                }
            }

            text.Text = label;
        }

        private static string PresenceLabel(string? status)
        {
            var i18n = Services.I18nService.Instance;
            return (status?.ToLower() ?? "") switch
            {
                "break"    => i18n.Get("Break"),
                "meeting"  => i18n.Get("Meeting"),
                "training" => i18n.Get("Training"),
                "dnd"      => i18n.Get("Dnd"),
                "pause"    => i18n.Get("Pause"),
                "offline"  => i18n.Get("Offline"),
                _          => i18n.Get("Online")
            };
        }
    }
}
