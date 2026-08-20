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
        private static readonly HttpClient _httpClient = Services.BackendHttp.Client;
        private SmsComposeDialog? _historySmsDialog;
        private bool _historySmsLaunchInProgress;
        private bool _isDetached;
        public ObservableCollection<CdrItemViewModel> CdrItems { get; } = new ObservableCollection<CdrItemViewModel>();

        public event EventHandler? OnCloseRequested;
        public event EventHandler? OnSettingsRequested;
        public event EventHandler? OnDialerRequested;
        public event EventHandler<string>? OutgoingCallRequested;
        public event EventHandler? OnExitAppRequested;

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
            _isDetached = true;
            base.OnDetachedFromVisualTree(e);
            _cdrTimer?.Stop();
            var dialog = _historySmsDialog;
            _historySmsDialog = null;
            dialog?.Close(false);
        }

        protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _isDetached = false;
            _cdrTimer?.Start();
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

                // The operator's local day, expressed as UTC instants — not the UTC day.
                // See CallHistoryWindow for what the UTC day cost the night shift.
                var (startOfToday, endOfToday) = CallHistoryWindow.ForLocalDay(DateTimeOffset.Now);

                var url = $"{backendUrl}/api/cdr?page=1&limit=20&fromDate={startOfToday}&toDate={endOfToday}&operatorId={Uri.EscapeDataString(operatorId)}";

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (!string.IsNullOrEmpty(settings.AccessToken))
                {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.AccessToken);
                }

                using var response = await _httpClient.SendAsync(request);
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

        private async void OnCdrCopyClicked(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string num)
            {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard != null)
                    await clipboard.SetTextAsync(num);
            }
        }

        private void OnCdrScriptClicked(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is CdrItemViewModel vm)
            {
                var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(this) as Avalonia.Controls.Window;
                if (topLevel == null) return;

                ScriptsWindowLauncher.Open(topLevel, selection => _ = RegisterScriptAsync(vm, selection));
            }
        }

        private async Task RegisterScriptAsync(CdrItemViewModel vm, ScriptSelection selection)
        {
            if (string.IsNullOrEmpty(vm.Entry.UniqueId)) return;

            if (await App.ScriptService.RegisterAndMarkAsync(vm.Entry.UniqueId, selection))
                _ = LoadCallHistoryAsync();
        }

        private void OnCdrSmsClicked(object? sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: CdrItemViewModel vm } smsButton)
                ShowHistorySmsComposeDialog(vm, smsButton);
        }

        private void ShowHistorySmsComposeDialog(CdrItemViewModel vm, Button smsButton)
        {
            if (_isDetached || _historySmsLaunchInProgress || _historySmsDialog is not null)
                return;

            if (!HistoryCallSmsContext.TryCreate(vm.Entry, vm.DisplayNumber, out var context) || context is null)
            {
                SetSmsComposeError(I18nService.Instance.Get("SmsHistoryCallUnavailable"));
                return;
            }

            var owner = TopLevel.GetTopLevel(this) as Window;
            if (owner is null)
                return;

            _historySmsLaunchInProgress = true;
            smsButton.IsEnabled = false;
            SetSmsComposeError(null);
            var shown = false;

            try
            {
                var dialog = new SmsComposeDialog(context.Source, context.LockedDisplayNumber);
                _historySmsDialog = dialog;
                dialog.Closed += (_, _) =>
                {
                    if (ReferenceEquals(_historySmsDialog, dialog))
                        _historySmsDialog = null;
                    if (!_isDetached)
                        smsButton.IsEnabled = true;
                };
                dialog.Show(owner);
                shown = true;
            }
            catch (Exception ex)
            {
                if (!_isDetached)
                {
                    AppLogger.Log("HistoryCallSms", $"Failed to open SMS compose: {ex.GetType().Name}");
                    SetSmsComposeError(I18nService.Instance.Get("SmsHistoryCallUnavailable"));
                }
            }
            finally
            {
                // Show returns as soon as the window is up, so this flag only ever covered
                // getting it there; from here on _historySmsDialog is what refuses a second
                // window, and its Closed handler gives the button back.
                _historySmsLaunchInProgress = false;
                if (!shown)
                {
                    _historySmsDialog = null;
                    if (!_isDetached)
                        smsButton.IsEnabled = true;
                }
            }
        }

        private void SetSmsComposeError(string? message)
        {
            var errorLabel = this.FindControl<TextBlock>("SmsComposeErrorLabel");
            if (errorLabel is null) return;

            errorLabel.Text = message ?? string.Empty;
            errorLabel.IsVisible = !string.IsNullOrWhiteSpace(message);
        }
    }
}
