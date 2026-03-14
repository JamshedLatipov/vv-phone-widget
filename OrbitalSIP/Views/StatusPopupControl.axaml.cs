using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;

namespace OrbitalSIP.Views
{
    public partial class StatusPopupControl : UserControl
    {
        public event EventHandler? OnCloseRequested;
        public event EventHandler<(string status, int duration)>? OnStatusUpdateRequested;

        public StatusPopupControl()
        {
            InitializeComponent();
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
        }

        private string GetSelectedStatus()
        {
            var availList = this.FindControl<ListBox>("AvailabilityList");
            var awayList = this.FindControl<ListBox>("AwayList");

            if (availList != null)
            {
                foreach (var item in availList.Items)
                {
                    if (item is ListBoxItem lbi && lbi.Content is RadioButton rb && rb.IsChecked == true)
                        return rb.Tag?.ToString() ?? "online";
                }
            }
            if (awayList != null)
            {
                foreach (var item in awayList.Items)
                {
                    if (item is ListBoxItem lbi && lbi.Content is RadioButton rb && rb.IsChecked == true)
                        return rb.Tag?.ToString() ?? "online";
                }
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

            var panel = this.FindControl<Border>("ActiveStatusPanel");
            var text = this.FindControl<TextBlock>("ActiveStatusText");

            if (state != null && state.Paused && panel != null && text != null)
            {
                panel.IsVisible = true;
                string reason = state.ReasonPaused ?? "Break";

                if (svc.BreakEndTime.HasValue)
                {
                    var left = svc.BreakEndTime.Value - DateTime.Now;
                    if (left.TotalSeconds > 0)
                        text.Text = $"{char.ToUpper(reason[0]) + reason.Substring(1)}: {left.Minutes:D2}:{left.Seconds:D2} left";
                    else
                        text.Text = $"{char.ToUpper(reason[0]) + reason.Substring(1)}";
                }
                else
                {
                    text.Text = $"{char.ToUpper(reason[0]) + reason.Substring(1)}";
                }
            }
            else if (panel != null)
            {
                panel.IsVisible = false;
            }
        }
    }
}
