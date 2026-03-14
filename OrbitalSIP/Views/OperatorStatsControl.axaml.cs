using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia;
using Avalonia.Threading;
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using OrbitalSIP.Models;
using OrbitalSIP.Services;

namespace OrbitalSIP.Views
{
    public partial class OperatorStatsControl : UserControl
    {
        private DispatcherTimer? _timer;
        private static readonly HttpClient _httpClient;

        static OperatorStatsControl()
        {
            var handler = new HttpClientHandler();
#if DEBUG
            handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
#endif
            _httpClient = new HttpClient(handler);
        }
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

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(2)
            };
            _timer.Tick += async (_, __) => await LoadStatsAsync();
            _timer.Start();

            // Initial load
            _ = LoadStatsAsync();
        }

        protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _timer?.Stop();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void ToggleExpanded()
        {
            _isExpanded = !_isExpanded;
            var content = this.FindControl<Border>("ExpandedContent");
            var icon = this.FindControl<Avalonia.Controls.PathIcon>("ExpanderIcon");

            if (content != null)
                content.IsVisible = _isExpanded;

            if (icon != null)
                icon.Data = Avalonia.Media.Geometry.Parse(_isExpanded ? "M7,14L12,9L17,14H7Z" : "M7,10L12,15L17,10H7Z");
        }

        public async Task LoadStatsAsync()
        {
            try
            {
                var settings = App.SipService?.CurrentSettings ?? SipSettings.Load();
                var operatorId = settings.DecodedToken?.Operator?.Username ?? settings.Username;
                var backendUrl = settings.BackendUrl?.TrimEnd('/');

                if (string.IsNullOrEmpty(operatorId) || string.IsNullOrEmpty(backendUrl))
                    return;

                var url = $"{backendUrl}/api/contact-center/operators/{Uri.EscapeDataString(operatorId)}/details?range=today";

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (!string.IsNullOrEmpty(settings.AccessToken))
                {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.AccessToken);
                }

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var data = await response.Content.ReadFromJsonAsync<OperatorDetailsResponse>();
                    if (data?.Stats != null)
                    {
                        await Dispatcher.UIThread.InvokeAsync(() => UpdateUI(data.Stats));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OperatorStatsControl] Error loading stats: {ex.Message}");
            }
        }

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

            // Update Summary
            var summaryTxt = this.FindControl<TextBlock>("SummaryText");
            if (summaryTxt != null)
                summaryTxt.Text = $"{answered} / {total} вх. {incoming}";

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
