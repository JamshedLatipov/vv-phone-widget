using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia;
using Avalonia.Threading;
using System;
using System.Threading.Tasks;
using OrbitalSIP.Models;
using OrbitalSIP.Services;
using Material.Icons.Avalonia;

namespace OrbitalSIP.Views
{
    public partial class OperatorStatsControl : UserControl
    {
        private bool _isExpanded;

        public OperatorStatsControl()
        {
            InitializeComponent();

            var headerBtn = this.FindControl<Button>("HeaderButton");
            if (headerBtn != null)
                headerBtn.Click += (_, __) => ToggleExpanded();

            var refreshBtn = this.FindControl<Button>("RefreshBtn");
            if (refreshBtn != null)
                refreshBtn.Click += async (_, __) => await LoadStatsAsync();

            // Polling moved to NavBadgeService: it hits the same endpoint for the Recents
            // badge, and it survives the screen-swap animation that used to stop the timer
            // that lived here.
            App.NavBadges.Changed += OnBadgesChanged;
            if (App.NavBadges.OperatorStats is { } stats) UpdateUI(stats);
        }

        protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            App.NavBadges.Changed -= OnBadgesChanged;
        }

        /// <summary>
        /// The screen-swap animation parents this control into OverlayHost and then moves
        /// it to Host — a detach/attach pair — so the detach above is not the end of its
        /// life. Idempotent re-subscribe: -= on an absent handler is a no-op, += twice
        /// would repaint twice.
        /// </summary>
        protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            App.NavBadges.Changed -= OnBadgesChanged;
            App.NavBadges.Changed += OnBadgesChanged;
            if (App.NavBadges.OperatorStats is { } stats) UpdateUI(stats);
        }

        private void OnBadgesChanged()
        {
            if (App.NavBadges.OperatorStats is { } stats)
                Dispatcher.UIThread.InvokeAsync(() => UpdateUI(stats));
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void ToggleExpanded()
        {
            _isExpanded = !_isExpanded;
            var content = this.FindControl<Border>("ExpandedContent");
            var icon = this.FindControl<MaterialIcon>("ExpanderIcon");

            if (content != null)
                content.IsVisible = _isExpanded;

            if (icon != null)
                icon.Kind = _isExpanded ? Material.Icons.MaterialIconKind.ChevronUp : Material.Icons.MaterialIconKind.ChevronDown;
        }

        public Task LoadStatsAsync() => App.NavBadges.RefreshNowAsync();

        private void UpdateUI(OperatorStats stats)
        {
            var total = stats.TotalCalls;
            var answered = stats.AnsweredCalls;
            var missed = stats.MissedCalls;
            var outgoing = stats.OutgoingCalls;
            var incoming = stats.IncomingCalls;

            int efficiency = 0;
            if (incoming > 0)
                efficiency = (int)Math.Round((double)stats.IncomingAnswered / incoming * 100);

            var ratioBar = this.FindControl<Grid>("RatioBarGrid");
            if (ratioBar != null)
            {
                if (incoming > 0)
                {
                    double green = stats.IncomingAnswered;
                    double red = stats.MissedCalls;
                    double totalRatio = incoming;
                    double empty = Math.Max(0, totalRatio - green - red);
                    ratioBar.ColumnDefinitions = new ColumnDefinitions($"{green}*, {red}*, {empty}*");
                }
                else
                {
                    ratioBar.ColumnDefinitions = new ColumnDefinitions("0*, 0*, 1*");
                }
            }
            // Update Summary
            var summaryTxt = this.FindControl<TextBlock>("SummaryText");
            if (summaryTxt != null)
                summaryTxt.Text = $"{answered} / {total} " + Services.I18nService.Instance.Get("IncomingShort") + $" {incoming}";

            // Update Efficiency
            var effTxt = this.FindControl<TextBlock>("EfficiencyText");
            if (effTxt != null)
            {
                effTxt.Text = $"{efficiency}%";
                effTxt.Foreground = Avalonia.Media.SolidColorBrush.Parse(efficiency >= 50 ? "#22C55E" : (efficiency > 20 ? "#F59E0B" : "#EF4444"));
            }

            // Update Grid Items
            SetText("TotalCallsText", total.ToString());
            SetText("MissedCallsText", missed.ToString());
            SetText("OutgoingCallsText", outgoing.ToString());
            SetText("IncomingCallsText", incoming.ToString());
            SetText("IncomingAnsweredText", stats.IncomingAnswered.ToString());
            SetText("OutgoingAnsweredText", stats.OutgoingAnswered.ToString());

            SetText("AvgDurationText", FormatDuration(stats.AvgDuration));
            SetText("TalkTimeText", FormatDuration(stats.TotalTalkTime));
        }

        private void SetText(string controlName, string text)
        {
            var tb = this.FindControl<TextBlock>(controlName);
            if (tb != null)
                tb.Text = text;
        }

        private string FormatDuration(int seconds)
        {
            var ts = TimeSpan.FromSeconds(seconds);
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            return $"{ts.Minutes}:{ts.Seconds:D2}";
        }
    }
}
