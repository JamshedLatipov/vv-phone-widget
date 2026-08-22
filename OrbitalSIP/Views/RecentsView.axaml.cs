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

        public event EventHandler<string>? OutgoingCallRequested;

        public RecentsView()
        {
            InitializeComponent();
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

                            // Putting the list in front of the operator is what "seen"
                            // means — not the tab press that got them here. MainWindow
                            // marks seen in NavigateTo, which deliberately ignores a press
                            // on the tab already showing, so a call missed while they are
                            // standing on this screen used to light a badge that stayed
                            // lit until they left and came back. That badge then followed
                            // them to the next screen claiming they had not looked at a
                            // call this list had already shown them.
                            //
                            // Here rather than on NavBadgeService.Changed, which is the
                            // obvious place and the one that loops: marking seen raises
                            // Changed, and a handler that marks seen on Changed raises it
                            // again. This timer is not driven by the badge poll, so there
                            // is no cycle to break — and the two are allowed to disagree
                            // for one interval, which reads correctly: the badge stays lit
                            // exactly while there is a missed call this list has not caught
                            // up with yet.
                            //
                            // Only when these rows are actually in front of someone. A load
                            // can land after the screen has been swapped out, and the timer
                            // keeps running while the widget sits in the tray — Hide()
                            // leaves the visual tree attached, so _isDetached alone would
                            // let a hidden window mark calls seen every two minutes for the
                            // rest of the shift. Which way to fail is not a toss-up: a
                            // badge that lights when it need not is an annoyance, one that
                            // stays dark over calls nobody called back is the thing the
                            // badge exists to prevent, and NavBadgeState errs the same way
                            // everywhere else it has the choice.
                            if (!_isDetached && TopLevel.GetTopLevel(this) is Window { IsVisible: true })
                                App.NavBadges.MarkRecentsSeen();
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
