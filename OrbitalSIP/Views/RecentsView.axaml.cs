using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
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
    public partial class RecentsView : UserControl
    {
        private DispatcherTimer? _cdrTimer;
        private static readonly HttpClient _httpClient;
        public ObservableCollection<CdrItemViewModel> CdrItems { get; } = new ObservableCollection<CdrItemViewModel>();

        public event EventHandler? OnCloseRequested;
        public event EventHandler? OnSettingsRequested;
        public event EventHandler? OnDialerRequested;
        public event EventHandler<string>? OutgoingCallRequested;
        public event EventHandler? OnExitAppRequested;

        static RecentsView()
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };
            _httpClient = new HttpClient(handler);
        }

        public RecentsView()
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
            var topBar = this.FindControl<TopBarControl>("TopBar");
            if (topBar != null) { topBar.OnMinimizeRequested += (_, __) => OnCloseRequested?.Invoke(this, EventArgs.Empty); topBar.OnCloseRequested += (_, __) => OnExitAppRequested?.Invoke(this, EventArgs.Empty); }

            var bottomNav = this.FindControl<BottomNavControl>("BottomNav");
            if (bottomNav != null)
            {
                bottomNav.OnSettingsRequested += (_, __) => OnSettingsRequested?.Invoke(this, EventArgs.Empty);
                bottomNav.OnDialerRequested += (_, __) => OnDialerRequested?.Invoke(this, EventArgs.Empty);
                bottomNav.SetActiveTab("Recents");
            }
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

                            var ic = this.FindControl<ItemsControl>("CdrItemsControl");
                            if (ic != null)
                            {
                                ic.ItemsSource = CdrItems;
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("RecentsView", $"Error loading call history: {ex.Message}");
            }
        }

        private void OnCdrCallClicked(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string num)
            {
                OutgoingCallRequested?.Invoke(this, num);
            }
        }

        private async void OnCdrScriptClicked(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is CdrItemViewModel vm)
            {
                var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(this) as Avalonia.Controls.Window;
                if (topLevel == null) return;

                var dialog = new ScriptsDialog();
                var result = await dialog.ShowDialog<CallScript?>(topLevel);

                if (result != null && !string.IsNullOrEmpty(vm.Entry.UniqueId))
                {
                    bool success = await App.ScriptService.RegisterScriptAsync(vm.Entry.UniqueId, result.Id!);
                    if (success)
                    {
                        var settings = App.SipService?.CurrentSettings ?? SipSettings.Load();
                        var operatorId = settings.DecodedToken?.Operator?.Username ?? settings.Username;
                        App.LoggedCallService.MarkCallAsLogged(vm.Entry.UniqueId, operatorId);
                        _ = LoadCallHistoryAsync();
                    }
                }
            }
        }
    }
}
