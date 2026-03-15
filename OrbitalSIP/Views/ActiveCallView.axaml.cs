using System;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using System.Threading.Tasks;
using Material.Icons;
using Material.Icons.Avalonia;
using OrbitalSIP.Services;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;

namespace OrbitalSIP.Views
{
    public partial class ActiveCallView : UserControl
    {
        private DispatcherTimer? _timer;
        private TimeSpan _elapsed = TimeSpan.Zero;
        public TimeSpan Elapsed => _elapsed;
        private bool _muted;
        private bool _onHold;

        public ActiveCallView()
            : this("Unknown", false)
        {
        }

        public ActiveCallView(string callerId, bool isOutgoing = false, TimeSpan? initialElapsed = null)
        {
            InitializeComponent();

            var callerLabel  = this.FindControl<TextBlock>("CallerLabel");
            var callerNumberLabel = this.FindControl<TextBlock>("CallerNumberLabel");
            var statusLabel  = this.FindControl<TextBlock>("StatusLabel");
            if (callerLabel != null) callerLabel.Text = callerId;
            if (callerNumberLabel != null) callerNumberLabel.Text = callerId;
            if (statusLabel != null) statusLabel.Text = isOutgoing ? Services.I18nService.Instance.Get("Calling") : Services.I18nService.Instance.Get("InCall");

            WireButtons();
            if (initialElapsed.HasValue) _elapsed = initialElapsed.Value;
            SetStatus(App.SipService.IsOnHold);
            UpdateTimeUI();
            StartTimer();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        // ── Timer ─────────────────────────────────────────────────────
        private void StartTimer()
        {
            _timer = new DispatcherTimer(
                TimeSpan.FromSeconds(1),
                DispatcherPriority.Render,
                OnTick);
            _timer.Start();
        }

        private void OnTick(object? sender, EventArgs e)
        {
            _elapsed = _elapsed.Add(TimeSpan.FromSeconds(1));
            UpdateTimeUI();
        }

        private void UpdateTimeUI()
        {
            var label = this.FindControl<TextBlock>("TimerLabel");
            var minutesLabel = this.FindControl<TextBlock>("TimerMinutesLabel");
            var secondsLabel = this.FindControl<TextBlock>("TimerSecondsLabel");
            var totalMinutes = (int)_elapsed.TotalMinutes;
            var seconds = _elapsed.Seconds;

            if (label != null)
                label.Text = _elapsed.TotalHours >= 1
                    ? _elapsed.ToString(@"h\:mm\:ss")
                    : _elapsed.ToString(@"mm\:ss");

            if (minutesLabel != null)
                minutesLabel.Text = totalMinutes.ToString("00");

            if (secondsLabel != null)
                secondsLabel.Text = seconds.ToString("00");
        }

        public void SetStatus(bool isOnHold)
        {
            var label = this.FindControl<TextBlock>("StatusLabel");
            var dot = this.FindControl<Ellipse>("StatusDot");
            if (label != null) label.Text = isOnHold ? Services.I18nService.Instance.Get("OnHold") : Services.I18nService.Instance.Get("InCall");
            if (dot != null) dot.Fill = new SolidColorBrush(isOnHold ? Color.Parse("#F59E0B") : Color.Parse("#3B82F6"));
        }

        public void MarkConnected()
        {
            var label = this.FindControl<TextBlock>("StatusLabel");
            if (label != null) label.Text = Services.I18nService.Instance.Get("InCall");
        }

        // ── Buttons ───────────────────────────────────────────────────
        private void WireButtons()
        {
            var hangup = this.FindControl<Button>("HangupBtn");
            if (hangup != null)
                hangup.Click += (_, __) =>
                {
                    _timer?.Stop();
                    OnHangup?.Invoke(this, EventArgs.Empty);
                };

            var mute = this.FindControl<Button>("MuteBtn");
            if (mute != null)
                mute.Click += (_, __) => ToggleMute();

            var hold = this.FindControl<Button>("HoldBtn");
            if (hold != null)
                hold.Click += (_, __) => ToggleHold();

            var transfer = this.FindControl<Button>("TransferBtn");
            if (transfer != null)
                transfer.Click += (_, __) => ShowTransferPanel();

            var transferConfirm = this.FindControl<Button>("TransferConfirmBtn");
            if (transferConfirm != null)
                transferConfirm.Click += (_, __) => ConfirmTransfer();

            var keypad = this.FindControl<Button>("KeypadBtn");
            if (keypad != null)
                keypad.Click += (_, __) => OnKeypadRequested?.Invoke(this, EventArgs.Empty);

            var scriptBtn = this.FindControl<Button>("ScriptBtn");
            if (scriptBtn != null)
                scriptBtn.Click += async (_, __) => await ShowScriptsDialog();

            var leadBtn = this.FindControl<Button>("CreateLeadBtn");
            if (leadBtn != null)
                leadBtn.Click += async (_, __) => await CreateLeadAsync();

            var topBar = this.FindControl<TopBarControl>("TopBar");
            if (topBar != null)
                topBar.OnMinimizeRequested += (_, __) => OnMinimizeRequested?.Invoke(this, EventArgs.Empty);
                topBar.OnAvatarClicked += (_, __) => OnAvatarClicked?.Invoke(this, EventArgs.Empty);

            var copy = this.FindControl<Button>("CopyCallerBtn");
            var bottomNav = this.FindControl<BottomNavControl>("BottomNav");
            if (bottomNav != null) bottomNav.OnSettingsRequested += (_, __) => OnSettingsRequested?.Invoke(this, EventArgs.Empty);
            if (copy != null)
                copy.Click += async (_, __) => await CopyCallerAsync();
        }

        private async Task CopyCallerAsync()
        {
            var caller = this.FindControl<TextBlock>("CallerLabel")?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(caller))
            {
                return;
            }

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard == null)
            {
                return;
            }

            await topLevel.Clipboard.SetTextAsync(caller);

            var copyButton = this.FindControl<Button>("CopyCallerBtn");
            if (copyButton == null)
            {
                return;
            }

            if (copyButton.Content is MaterialIcon icon)
            {
                var originalKind = icon.Kind;
                icon.Kind = Material.Icons.MaterialIconKind.Check;
                await Task.Delay(1200);
                icon.Kind = originalKind;
            }
        }

        private void ToggleMute()
        {
            _muted = !_muted;
            OnMuteToggled?.Invoke(this, _muted);

            var icon  = this.FindControl<AvaloniaPath>("MuteIcon");
            var label = this.FindControl<TextBlock>("MuteLabel");
            var btn   = this.FindControl<Button>("MuteBtn");

            if (icon  != null) icon.Fill  = new SolidColorBrush(_muted ? Color.Parse("#FFFFFF") : Color.Parse("#DDE7F3"));
            if (label != null) label.Text  = _muted ? "Unmute" : "Mute";
            if (btn   != null) btn.Background = new SolidColorBrush(_muted ? Color.Parse("#B91C1C") : Color.Parse("#1A2D42"));
        }

        private async Task CreateLeadAsync()
        {
            Console.WriteLine("[CreateLeadAsync] Button clicked");
            var callerNumber = this.FindControl<TextBlock>("CallerNumberLabel")?.Text?.Trim() ?? string.Empty;
            Console.WriteLine($"[CreateLeadAsync] Extracted callerNumber: '{callerNumber}'");

            Console.WriteLine($"[CreateLeadAsync] Caller number: {callerNumber}");
            if (string.IsNullOrWhiteSpace(callerNumber))
            {
                Console.WriteLine("[CreateLeadAsync] Caller number is empty, aborting.");
                return;
            }

            var request = new Models.CreateLeadRequest
            {
                Name = callerNumber,
                Phone = callerNumber,
                Status = "new",
                Source = "phone",
                Priority = "low"
            };

            Console.WriteLine("[CreateLeadAsync] Sending request to LeadService...");
            bool success = await App.LeadService.CreateLeadAsync(request);
            Console.WriteLine($"[CreateLeadAsync] Request success: {success}");

            if (success)
            {
                var leadBtn = this.FindControl<Button>("CreateLeadBtn");
                if (leadBtn != null)
                {
                    // Find the icon within the StackPanel inside the Button
                    var stackPanel = leadBtn.Content as StackPanel;
                    if (stackPanel != null)
                    {
                        foreach (var child in stackPanel.Children)
                        {
                            if (child is Material.Icons.Avalonia.MaterialIcon icon)
                            {
                                var originalKind = icon.Kind;
                                icon.Kind = Material.Icons.MaterialIconKind.Check;
                                await Task.Delay(1200);
                                icon.Kind = originalKind;
                                break;
                            }
                        }
                    }
                }
            }
        }

        private async Task ShowScriptsDialog()
        {
            var topLevel = TopLevel.GetTopLevel(this) as Window;
            if (topLevel == null) return;

            var dialog = new ScriptsDialog();
            var result = await dialog.ShowDialog<Models.CallScript?>(topLevel);

            if (result != null)
            {
                var callerLabel = this.FindControl<TextBlock>("CallerNumberLabel");
                var number = callerLabel?.Text?.Trim() ?? "";
                var uniqueId = await App.ScriptService.GetChannelUniqueIdAsync(number);
                if (uniqueId != null)
                {
                    bool success = await App.ScriptService.RegisterScriptAsync(uniqueId, result.Id!);
                    if (success)
                    {
                        var settings = App.SipService?.CurrentSettings ?? SipSettings.Load();
                        var operatorId = settings.DecodedToken?.Operator?.Username ?? settings.Username;
                        App.LoggedCallService.MarkCallAsLogged(uniqueId, operatorId);
                    }
                }
            }
        }

        private void ToggleHold()
        {
            _onHold = !_onHold;
            OnHoldToggled?.Invoke(this, _onHold);

            var icon  = this.FindControl<AvaloniaPath>("HoldIcon");
            var label = this.FindControl<TextBlock>("HoldLabel");
            var btn   = this.FindControl<Button>("HoldBtn");

            if (label != null) label.Text = _onHold ? Services.I18nService.Instance.Get("Resume") : Services.I18nService.Instance.Get("Hold");
            if (btn   != null) btn.Background = new SolidColorBrush(_onHold ? Color.Parse("#B91C1C") : Color.Parse("#1E4270"));
        }

        private void ShowTransferPanel()
        {
            var panel = this.FindControl<Border>("TransferPanel");
            if (panel != null) panel.IsVisible = !panel.IsVisible;
        }

        private void ConfirmTransfer()
        {
            var box    = this.FindControl<TextBox>("TransferNumberBox");
            var number = box?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(number)) return;

            var panel = this.FindControl<Border>("TransferPanel");
            if (panel != null) panel.IsVisible = false;

            OnTransferRequested?.Invoke(this, number);
        }

        // ── Events ────────────────────────────────────────────────────
        public event EventHandler?        OnHangup;
        public event EventHandler<bool>?  OnMuteToggled;      // arg = isMuted
        public event EventHandler<bool>?  OnHoldToggled;      // arg = isOnHold
        public event EventHandler<string>? OnTransferRequested; // arg = destination
        public event EventHandler?        OnKeypadRequested;
        public event EventHandler?        OnMinimizeRequested;
        public event EventHandler?        OnSettingsRequested;
        public event EventHandler?        OnAvatarClicked;
        public event EventHandler?        OnRecentsRequested;
    }
}
