using System;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using System.Threading.Tasks;
using Material.Icons;
using Material.Icons.Avalonia;
using OrbitalSIP.Models;
using OrbitalSIP.Services;

namespace OrbitalSIP.Views
{
    public partial class ActiveCallView : UserControl
    {
        private DispatcherTimer? _timer;
        private TimeSpan _elapsed = TimeSpan.Zero;
        public TimeSpan Elapsed => _elapsed;
        private bool _muted;
        private bool _onHold;
        private bool _leadCreated;
        private bool _callInfoLoaded;

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

            var callInfoBtn = this.FindControl<Button>("CallInfoBtn");
            if (callInfoBtn != null)
                callInfoBtn.Click += (_, __) => ToggleCallInfoPanel();

            var callInfoCloseBtn = this.FindControl<Button>("CallInfoCloseBtn");
            if (callInfoCloseBtn != null)
                callInfoCloseBtn.Click += (_, __) => HideCallInfoPanel();

            var topBar = this.FindControl<TopBarControl>("TopBar");
            if (topBar != null)
            {
                topBar.OnMinimizeRequested += (_, __) => OnMinimizeRequested?.Invoke(this, EventArgs.Empty);
                topBar.OnAvatarClicked += (_, __) => OnAvatarClicked?.Invoke(this, EventArgs.Empty);
                topBar.OnCloseRequested += (_, __) => OnExitAppRequested?.Invoke(this, EventArgs.Empty);
            }

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

            var icon  = this.FindControl<MaterialIcon>("MuteIcon");
            var label = this.FindControl<TextBlock>("MuteLabel");
            var btn   = this.FindControl<Button>("MuteBtn");

            if (icon  != null)
            {
                icon.Foreground = new SolidColorBrush(_muted ? Color.Parse("#FFFFFF") : Color.Parse("#DDE7F3"));
                icon.Kind = _muted ? MaterialIconKind.MicrophoneOff : MaterialIconKind.Microphone;
            }
            if (label != null) label.Text  = _muted ? "Unmute" : "Mute";
            if (btn   != null) btn.Background = new SolidColorBrush(_muted ? Color.Parse("#B91C1C") : Color.Parse("#1A2D42"));
        }

        private async Task CreateLeadAsync()
        {
            AppLogger.Log("CreateLead", "Button clicked");
            if (_leadCreated)
            {
                AppLogger.Log("CreateLead", "Lead already created for this call. Aborting.");
                return;
            }

            var callerNumber = this.FindControl<TextBlock>("CallerNumberLabel")?.Text?.Trim() ?? string.Empty;
            AppLogger.Log("CreateLead", $"Extracted callerNumber: '{callerNumber}'");

            AppLogger.Log("CreateLead", $"Caller number: {callerNumber}");
            if (string.IsNullOrWhiteSpace(callerNumber))
            {
                AppLogger.Log("CreateLead", "Caller number is empty, aborting.");
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

            AppLogger.Log("CreateLead", "Sending request to LeadService...");

            // Disable button visually while processing and after success
            var leadBtn = this.FindControl<Button>("CreateLeadBtn");
            if (leadBtn != null)
                leadBtn.IsEnabled = false;

            bool success = await App.LeadService.CreateLeadAsync(request);
            AppLogger.Log("CreateLead", $"Request success: {success}");

            if (success)
            {
                _leadCreated = true;
                if (leadBtn != null)
                {
                    leadBtn.Opacity = 0.5; // Visually indicate it's disabled permanently for this call
                    var stackPanel = leadBtn.Content as StackPanel;
                    if (stackPanel != null)
                    {
                        foreach (var child in stackPanel.Children)
                        {
                            if (child is Material.Icons.Avalonia.MaterialIcon icon)
                            {
                                icon.Kind = Material.Icons.MaterialIconKind.Check;
                                // Keep the checkmark permanently to show it was created
                                break;
                            }
                        }
                    }
                }
            }
            else
            {
                // Re-enable if failed so they can try again
                if (leadBtn != null)
                    leadBtn.IsEnabled = true;
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

        // ── Call Info Panel ───────────────────────────────────────────
        private void ToggleCallInfoPanel()
        {
            var panel = this.FindControl<Border>("CallInfoPanel");
            if (panel == null) return;

            if (panel.IsVisible)
            {
                HideCallInfoPanel();
            }
            else
            {
                panel.IsVisible = true;
                if (!_callInfoLoaded)
                    _ = LoadCallInfoAsync();
            }
        }

        private void HideCallInfoPanel()
        {
            var panel = this.FindControl<Border>("CallInfoPanel");
            if (panel != null) panel.IsVisible = false;
        }

        private async Task LoadCallInfoAsync()
        {
            var callerNumber = this.FindControl<TextBlock>("CallerNumberLabel")?.Text?.Trim() ?? string.Empty;
            AppLogger.Log("CallInfo", $"Loading call info for '{callerNumber}'");

            var response = await App.CallInfoService.GetCallInfoAsync(callerNumber);
            AppLogger.Log("CallInfo", $"Response: {(response == null ? "null" : $"{response.Sections.Count} sections")}");

            _callInfoLoaded = true;

            await Dispatcher.UIThread.InvokeAsync(() => RenderCallInfo(response));
        }

        private void RenderCallInfo(CallInfoResponse? response)
        {
            var loadingLabel  = this.FindControl<TextBlock>("CallInfoLoadingLabel");
            var emptyPanel    = this.FindControl<StackPanel>("CallInfoEmptyPanel");
            var sectionsPanel = this.FindControl<StackPanel>("CallInfoSectionsPanel");

            if (loadingLabel  != null) loadingLabel.IsVisible  = false;
            if (emptyPanel    == null || sectionsPanel == null) return;

            if (response == null || response.Sections.Count == 0)
            {
                emptyPanel.IsVisible = true;
                return;
            }

            bool hasAnyData = false;
            foreach (var section in response.Sections)
            {
                if (section.Ui == null || section.Ui.Fields.Count == 0) continue;

                var rows = new System.Collections.Generic.List<(string Label, string Value)>();
                foreach (var field in section.Ui.Fields)
                {
                    var value = Services.CallInfoService.ResolveField(section.Data, field.Key);
                    AppLogger.Log("CallInfo", $"  Field '{field.Key}' => '{value ?? "(null)"}'" );
                    if (!string.IsNullOrWhiteSpace(value))
                        rows.Add((field.Label, value));
                }

                if (rows.Count == 0) continue;
                hasAnyData = true;
                sectionsPanel.Children.Add(BuildCallInfoSectionCard(section.Ui.Title, rows));
            }

            if (hasAnyData)
                sectionsPanel.IsVisible = true;
            else
                emptyPanel.IsVisible = true;
        }

        private Border BuildCallInfoSectionCard(
            string title,
            System.Collections.Generic.List<(string Label, string Value)> rows)
        {
            var contentStack = new StackPanel { Spacing = 10 };

            contentStack.Children.Add(new TextBlock
            {
                Text          = title,
                FontSize      = 11,
                FontWeight    = FontWeight.Bold,
                Foreground    = new SolidColorBrush(Color.Parse("#60A5FA")),
                LetterSpacing = 0.8
            });

            contentStack.Children.Add(new Border
            {
                Height     = 1,
                Background = new SolidColorBrush(Color.Parse("#243348")),
                Margin     = new Avalonia.Thickness(0, 0, 0, 2)
            });

            foreach (var (label, value) in rows)
            {
                var rowGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

                var labelBlock = new TextBlock
                {
                    Text              = label,
                    FontSize          = 12,
                    Foreground        = new SolidColorBrush(Color.Parse("#6E859D")),
                    VerticalAlignment = VerticalAlignment.Top,
                    TextWrapping      = TextWrapping.Wrap
                };

                var copyIcon = new MaterialIcon
                {
                    Kind       = MaterialIconKind.ContentCopy,
                    Width      = 13,
                    Height     = 13,
                    Foreground = new SolidColorBrush(Color.Parse("#60A5FA"))
                };

                var copyBtn = new Button
                {
                    Content           = copyIcon,
                    Background        = Brushes.Transparent,
                    BorderThickness   = new Avalonia.Thickness(0),
                    Padding           = new Avalonia.Thickness(5, 0, 0, 0),
                    Opacity           = 0,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var valueBlock = new TextBlock
                {
                    Text              = value,
                    FontSize          = 12,
                    FontWeight        = FontWeight.Medium,
                    Foreground        = new SolidColorBrush(Color.Parse("#F8FAFC")),
                    TextWrapping      = TextWrapping.Wrap,
                    TextAlignment     = Avalonia.Media.TextAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top
                };

                var rightPanel = new StackPanel
                {
                    Orientation         = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                rightPanel.Children.Add(valueBlock);
                rightPanel.Children.Add(copyBtn);

                Grid.SetColumn(labelBlock, 0);
                Grid.SetColumn(rightPanel, 1);
                rowGrid.Children.Add(labelBlock);
                rowGrid.Children.Add(rightPanel);

                rowGrid.PointerEntered += (_, __) => copyBtn.Opacity = 1;
                rowGrid.PointerExited  += (_, __) => copyBtn.Opacity = 0;

                var capturedValue = value;
                var capturedIcon  = copyIcon;
                copyBtn.Click += async (_, __) =>
                {
                    var topLevel = TopLevel.GetTopLevel(this);
                    if (topLevel?.Clipboard == null) return;
                    await topLevel.Clipboard.SetTextAsync(capturedValue);
                    capturedIcon.Kind = MaterialIconKind.Check;
                    await Task.Delay(1000);
                    capturedIcon.Kind = MaterialIconKind.ContentCopy;
                };

                contentStack.Children.Add(rowGrid);
            }

            return new Border
            {
                Background   = new SolidColorBrush(Color.Parse("#1E293B")),
                CornerRadius = new Avalonia.CornerRadius(12),
                Padding      = new Avalonia.Thickness(16, 14),
                Child        = contentStack
            };
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
        public event EventHandler?        OnExitAppRequested;
    }
}
