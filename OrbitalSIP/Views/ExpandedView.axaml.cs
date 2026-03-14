using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using System.Threading.Tasks;
using OrbitalSIP.Services;
using OrbitalSIP.Models;
using OrbitalSIP.ViewModels;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Json;
using Avalonia.Interactivity;

namespace OrbitalSIP.Views
{
    public partial class ExpandedView : UserControl
    {

        private DispatcherTimer? _cdrTimer;
        private static readonly HttpClient _httpClient;
        public ObservableCollection<CdrItemViewModel> CdrItems { get; } = new ObservableCollection<CdrItemViewModel>();

        static ExpandedView()
        {
            var handler = new HttpClientHandler();
#if DEBUG
            handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
#endif
            _httpClient = new HttpClient(handler);
        }

        public ExpandedView()
        {
            InitializeComponent();
            WireButtons();
            DataContext = this;

            var refreshBtn = this.FindControl<Button>("RefreshCdrBtn");
            if (refreshBtn != null) refreshBtn.Click += async (_, __) => await LoadCallHistoryAsync();

            _cdrTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(2)
            };
            _cdrTimer.Tick += async (_, __) => await LoadCallHistoryAsync();
            _cdrTimer.Start();

            _ = LoadCallHistoryAsync();

        }


        protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _cdrTimer?.Stop();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void WireButtons()
        {
            // Header buttons
            var topBar = this.FindControl<TopBarControl>("TopBar");
            if (topBar != null) topBar.OnMinimizeRequested += (_, __) => OnCloseRequested?.Invoke(this, EventArgs.Empty);
            var bottomNav = this.FindControl<BottomNavControl>("BottomNav");
            if (bottomNav != null) bottomNav.OnSettingsRequested += (_, __) => OnSettingsRequested?.Invoke(this, EventArgs.Empty);
            bottomNav?.SetActiveTab("Dialer");
            BindAsync("CopyBtn", CopyDisplayedNumberAsync);

            // Backspace
            Bind("BackspaceBtn", () =>
            {
                var d = this.FindControl<TextBlock>("DisplayText");
                if (d != null && d.Text?.Length > 0)
                    d.Text = d.Text[..^1];
            });

            // Call button
            Bind("CallBtn", () =>
            {
                var d = this.FindControl<TextBlock>("DisplayText");
                var num = d?.Text?.Trim() ?? "";
                if (num.Length > 0)
                    OutgoingCallRequested?.Invoke(this, num);
            });

            // Dial pad digits
            var pad = this.FindControl<UniformGrid>("DialPad");
            if (pad == null) return;
            foreach (var child in pad.Children)
            {
                if (child is Button btn)
                {
                    var digit = btn.Tag?.ToString() ?? btn.Content?.ToString() ?? "";
                    btn.Click += (_, __) =>
                    {
                        var d = this.FindControl<TextBlock>("DisplayText");
                        if (d != null) d.Text = (d.Text ?? "") + digit;
                    };
                }
            }
        }

        private void Bind(string name, Action action)
        {
            var btn = this.FindControl<Button>(name);
            if (btn != null) btn.Click += (_, __) => action();
        }

        private void BindAsync(string name, Func<Task> action)
        {
            var btn = this.FindControl<Button>(name);
            if (btn != null) btn.Click += async (_, __) => await action();
        }

        private async Task CopyDisplayedNumberAsync()
        {
            var text = this.FindControl<TextBlock>("DisplayText")?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard == null)
            {
                return;
            }

            await topLevel.Clipboard.SetTextAsync(text);

            var button = this.FindControl<Button>("CopyBtn");
            if (button == null)
            {
                return;
            }

            var original = button.Content;
            button.Content = "Copied";
            await Task.Delay(1200);
            button.Content = original;
        }


        private async Task LoadCallHistoryAsync()
        {
            try
            {
                var settings = App.SipService?.CurrentSettings ?? SipSettings.Load();
                var operatorId = settings.DecodedToken?.Operator?.Username ?? settings.Username;
                var backendUrl = settings.BackendUrl?.TrimEnd('/');

                if (string.IsNullOrEmpty(operatorId) || string.IsNullOrEmpty(backendUrl))
                    return;

                var startOfToday = DateTime.UtcNow.Date.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                var endOfToday = DateTime.UtcNow.Date.AddDays(1).AddTicks(-1).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

                var url = $"{backendUrl}/api/cdr?page=1&limit=20&fromDate={startOfToday}&toDate={endOfToday}&operatorId={Uri.EscapeDataString(operatorId)}";

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (!string.IsNullOrEmpty(settings.AccessToken))
                {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.AccessToken);
                }

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var data = await response.Content.ReadFromJsonAsync<CdrResponse>();
                    if (data?.Data != null)
                    {
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            CdrItems.Clear();
                            foreach(var item in data.Data)
                            {
                                CdrItems.Add(new CdrItemViewModel(item, operatorId));
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ExpandedView] Error loading call history: {ex.Message}");
            }
        }

        private void OnCdrCallClicked(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string num)
            {
                var d = this.FindControl<TextBlock>("DisplayText");
                if (d != null)
                {
                    d.Text = num;
                }
                OutgoingCallRequested?.Invoke(this, num);
            }
        }

        // ── Events ────────────────────────────────────────────────────
        public event System.EventHandler?        OnCloseRequested;
        public event System.EventHandler?        OnSettingsRequested;
        /// <summary>Fired when the user presses the call button. Arg = dialled number.</summary>
        public event EventHandler<string>? OutgoingCallRequested;
    }
}
