using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using System.Diagnostics;
using OrbitalSIP.Services;

namespace OrbitalSIP
{
    public partial class MainWindow : Window
    {
        private enum PreferredMode { Widget, Panel }

        private const double WidgetSize     = 96;
        private const double ExpandedWidth  = 320;
        private const double ExpandedHeight = 600;
        private const double IncomingWidth  = 436;
        private const double IncomingHeight = 132;
        private const double AnimDurationMs = 280;

        private PreferredMode _preferredMode = PreferredMode.Widget;

        private int  _anchorX, _anchorY;
        private bool _isExpanded;

        private DispatcherTimer? _animTimer;
        private Stopwatch? _animStopwatch;
        private double _animProgress;
        private double _fromW, _fromH, _toW, _toH;
        private object? _pendingContent;
        private Action? _onAnimComplete;

        public MainWindow()
        {
            InitializeComponent();

            var workArea = Screens?.Primary?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);

            // Wire SIP events
            var sip = App.SipService;
            sip.IncomingCallReceived += callerId =>
                Dispatcher.UIThread.InvokeAsync(() => ShowIncomingCall(callerId));
            sip.CallStateChanged += state =>
                Dispatcher.UIThread.InvokeAsync(() => OnCallStateChanged(state));

            this.SystemDecorations = SystemDecorations.None;
            this.PointerPressed   += MainWindow_PointerPressed;
            this.PointerReleased  += MainWindow_PointerReleased;
            this.DoubleTapped     += (_, __) => ExpandOnDoubleTap();
            this.Closing += (s, e) => { e.Cancel = true; this.Hide(); };

            // Initial view
            var settings = SipSettings.Load();
            if (string.IsNullOrEmpty(sip.CurrentSettings.Username) || string.IsNullOrEmpty(sip.CurrentSettings.Password))
            {
                // Show Login centered
                _isExpanded = true;
                Width = ExpandedWidth;
                Height = ExpandedHeight;

                var left = workArea.X + (workArea.Width - (int)ExpandedWidth) / 2;
                var top  = workArea.Y + (workArea.Height - (int)ExpandedHeight) / 2;
                Position = new PixelPoint(left, top);

                _anchorX = left + (int)ExpandedWidth;
                _anchorY = top  + (int)ExpandedHeight;

                ShowLogin();
            }
            else
            {
                // Show Widget at bottom right
                var left = workArea.Right - (int)WidgetSize - 24;
                var top  = workArea.Bottom - (int)WidgetSize - 48;
                Position = new PixelPoint(left, top);

                _anchorX = left + (int)WidgetSize;
                _anchorY = top  + (int)WidgetSize;

                sip.Start(settings);
                SetMainContent(new Views.WidgetView());
            }
        }

        // ── Drag ──────────────────────────────────────────────────────
        private void MainWindow_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            // Traverse the visual tree from the source to see if we clicked an interactive control
            var visual = e.Source as Visual;
            while (visual != null && visual != this)
            {
                if (visual is Button || visual is TextBox || visual is ComboBox ||
                    visual is ListBoxItem || visual is ScrollBar || visual is ScrollViewer)
                {
                    return; // Interactive control reached, do not drag
                }
                visual = visual.GetVisualParent();
            }

            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        }
        private void MainWindow_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_animTimer == null)
            {
                _anchorX = Position.X + (int)Width;
                _anchorY = Position.Y + (int)Height;
            }
        }

        // ── View toggle ───────────────────────────────────────────────
        private void ToggleExpanded()
        {
            if (_isExpanded) CollapseWidget();
            else             ExpandWidget();
        }

        private void ExpandOnDoubleTap()
        {
            if (_isExpanded) return;
            ExpandWidget();
        }

        private void ExpandWidget()
        {
            _isExpanded = true;
            _preferredMode = PreferredMode.Panel;
            _anchorX = Position.X + (int)Width;
            _anchorY = Position.Y + (int)Height;
            StartAnimation(Width, Height, ExpandedWidth, ExpandedHeight, CreateDialerView());
        }

        private void CollapseWidget()
        {
            _isExpanded = false;
            _preferredMode = PreferredMode.Widget;
            StartAnimation(Width, Height, WidgetSize, WidgetSize, new Views.WidgetView());
        }

        private void ReturnToPreferredMode()
        {
            _anchorX = Position.X + (int)Width;
            _anchorY = Position.Y + (int)Height;

            if (_preferredMode == PreferredMode.Panel)
            {
                _isExpanded = true;
                StartAnimation(Width, Height, ExpandedWidth, ExpandedHeight, CreateDialerView());
            }
            else
            {
                _isExpanded = false;
                StartAnimation(Width, Height, WidgetSize, WidgetSize, new Views.WidgetView());
            }
        }

        // ── Login ─────────────────────────────────────────────────────
        private void ShowLogin()
        {
            var login = new Views.LoginView();
            login.OnLoginSuccess += (_, __) =>
            {
                _isExpanded = false;
                _preferredMode = PreferredMode.Widget;
                StartAnimation(Width, Height, WidgetSize, WidgetSize, new Views.WidgetView());
            };
            login.OnSettingsRequested += (_, __) => ShowSettings(isFromLogin: true);
            SetMainContent(login);
        }

        // ── Dialer ────────────────────────────────────────────────────
        private void ShowDialer()
        {
            SetMainContent(CreateDialerView());
        }

        // ── Settings ──────────────────────────────────────────────────
        private void ShowSettings(bool isFromLogin = false)
        {
            var settingsView = new Views.SettingsView();
            settingsView.OnBackRequested += (_, __) =>
            {
                var settings = SipSettings.Load();
                var current = App.SipService.CurrentSettings;
                if (!string.IsNullOrEmpty(current.Username))
                {
                    settings.Username = current.Username;
                    settings.Password = current.Password;
                }

                if (isFromLogin) ShowLogin();
                else
                {
                    App.SipService.Start(settings);
                    ShowDialer();
                }
            };
            SetMainContent(settingsView);
        }

        // ── Outgoing call ─────────────────────────────────────────────
        private async void StartOutgoingCall(string number)
        {
            var host = this.FindControl<ContentControl>("Host");
            if (host == null) return;

            var callView = new Views.ActiveCallView(number, isOutgoing: true);
            WireActiveCallView(callView);
            SetMainContent(callView);

            await App.SipService.CallAsync(number);
        }

        // ── Incoming call ─────────────────────────────────────────────
        private void ShowIncomingCall(string callerId)
        {
            var incoming = new Views.IncomingView();
            incoming.SetCaller(callerId);

            incoming.OnAnswer  += async (_, __) =>
            {
                await App.SipService.AnswerAsync();
                _anchorX = Position.X + (int)Width;
                _anchorY = Position.Y + (int)Height;
                StartAnimation(Width, Height, ExpandedWidth, ExpandedHeight);
                ShowActiveCallView(callerId);
            };
            incoming.OnDecline += (_, __) =>
            {
                App.SipService.Decline();
                ReturnToPreferredMode();
            };

            _anchorX = Position.X + (int)Width;
            _anchorY = Position.Y + (int)Height;
            if (!_isExpanded)
            {
                _isExpanded = true;
                StartAnimation(Width, Height, IncomingWidth, IncomingHeight, incoming);
                return;
            }

            SetMainContent(incoming);
            StartAnimation(Width, Height, IncomingWidth, IncomingHeight);
        }

        private void ShowActiveCallWidgetView(string callerId, TimeSpan elapsed)
        {
            var widget = new Views.ActiveCallWidgetView(callerId, elapsed, App.SipService.IsMuted, App.SipService.IsOnHold);
            WireActiveCallWidgetView(widget);

            _anchorX = Position.X + (int)Width;
            _anchorY = Position.Y + (int)Height;
            _isExpanded = true;
            StartAnimation(Width, Height, IncomingWidth, IncomingHeight, widget);
        }

        private void WireActiveCallWidgetView(Views.ActiveCallWidgetView widget)
        {
            widget.OnHangup += (_, __) =>
            {
                App.SipService.Hangup();
                ReturnToPreferredMode();
            };
            widget.OnMuteToggled += (_, muted) => App.SipService.SetMuted(muted);
            widget.OnHoldToggled += (_, __) => App.SipService.ToggleHold();
            widget.OnTransferRequested += (_, __) => ShowActiveCallView(App.SipService.ActiveCallerId, widget.Elapsed);
            widget.OnExpandRequested += (_, __) => ShowActiveCallView(App.SipService.ActiveCallerId, widget.Elapsed);
        }

        private void ShowActiveCallView(string callerId, TimeSpan? elapsed = null)
        {
            var callView = new Views.ActiveCallView(callerId, initialElapsed: elapsed);
            WireActiveCallView(callView);

            if (Math.Abs(Width - ExpandedWidth) > 1 || Math.Abs(Height - ExpandedHeight) > 1)
            {
                _anchorX = Position.X + (int)Width;
                _anchorY = Position.Y + (int)Height;
                StartAnimation(Width, Height, ExpandedWidth, ExpandedHeight, callView);
            }
            else SetMainContent(callView);
        }

        private void WireActiveCallView(Views.ActiveCallView callView)
        {
            callView.OnHangup += (_, __) =>
            {
                App.SipService.Hangup();
                ReturnToPreferredMode();
            };
            callView.OnMinimizeRequested += (_, __) => ShowActiveCallWidgetView(App.SipService.ActiveCallerId, callView.Elapsed);
            callView.OnMuteToggled += (_, muted) => App.SipService.SetMuted(muted);
            callView.OnHoldToggled += (_, __) => App.SipService.ToggleHold();
            callView.OnTransferRequested += async (_, dest) => await App.SipService.BlindTransferAsync(dest);
            callView.OnKeypadRequested += (_, __) => ShowDialer();
            callView.OnSettingsRequested += (_, __) => ShowSettings();
        }

        // ── SIP state changes ─────────────────────────────────────────
        private void OnCallStateChanged(CallState state)
        {
            if (state == CallState.Idle && _isExpanded)
            {
                var host = this.FindControl<ContentControl>("Host");
                if (!(host?.Content is Views.LoginView) && !(host?.Content is Views.SettingsView))
                {
                    ReturnToPreferredMode();
                }
            }
            else if (state == CallState.Active || state == CallState.OnHold)
            {
                var host = this.FindControl<ContentControl>("Host");
                bool isOnHold = (state == CallState.OnHold);
                if (host?.Content is Views.ActiveCallView av) { av.MarkConnected(); av.SetStatus(isOnHold); }
                else if (host?.Content is Views.ActiveCallWidgetView awv) awv.SetStatus(isOnHold);
            }
        }

        // ── Resize animation ──────────────────────────────────────────
        private void StartAnimation(double fromW, double fromH,
                                    double toW,   double toH,
                                    object? nextContent = null,
                                    Action? onComplete = null)
        {
            _animTimer?.Stop();
            _fromW = fromW; _fromH = fromH;
            _toW   = toW;   _toH   = toH;
            _animProgress   = 0;
            _pendingContent = nextContent;
            _onAnimComplete = onComplete;
            _animStopwatch = Stopwatch.StartNew();

            var overlay = this.FindControl<ContentControl>("OverlayHost");
            var host = this.FindControl<ContentControl>("Host");
            if (overlay != null) { overlay.Content = nextContent; overlay.Opacity = 0; overlay.IsVisible = true; }
            if (host != null) host.Opacity = 1;

            _animTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Normal, OnAnimTick);
            _animTimer.Start();
        }

        private void OnAnimTick(object? sender, EventArgs e)
        {
            if (_animStopwatch == null) return;

            _animProgress = Math.Clamp(_animStopwatch.Elapsed.TotalMilliseconds / AnimDurationMs, 0.0, 1.0);
            if (_animProgress >= 1.0) { _animTimer!.Stop(); _animTimer = null; _animStopwatch = null; }

            var t = EaseOutCubic(_animProgress);
            var w = _fromW + (_toW - _fromW) * t;
            var h = _fromH + (_toH - _fromH) * t;

            var host = this.FindControl<ContentControl>("Host");
            var overlay = this.FindControl<ContentControl>("OverlayHost");
            if (host != null && overlay?.Content != null)
            {
                var fadeProgress = Math.Clamp((_animProgress - 0.08) / 0.62, 0.0, 1.0);
                var fade = EaseInOutCubic(fadeProgress);
                host.Opacity = 1.0 - fade;
                overlay.Opacity = fade;
            }

            Position = new PixelPoint((int)Math.Round(_anchorX - w), (int)Math.Round(_anchorY - h));
            Width  = w; Height = h;

            if (_animProgress >= 1.0) { CompleteAnimatedContentSwap(); _onAnimComplete?.Invoke(); }
        }

        private static double EaseOutCubic(double value) => 1.0 - Math.Pow(1.0 - value, 3.0);
        private static double EaseInOutCubic(double value) =>
            value < 0.5 ? 4.0 * value * value * value : 1.0 - Math.Pow(-2.0 * value + 2.0, 3.0) / 2.0;

        private Views.ExpandedView CreateDialerView()
        {
            var dialer = new Views.ExpandedView();
            dialer.OnCloseRequested += (_, __) => CollapseWidget();
            dialer.OnSettingsRequested += (_, __) => ShowSettings();
            dialer.OutgoingCallRequested += (_, number) => StartOutgoingCall(number);
            return dialer;
        }

        private void SetMainContent(object content)
        {
            var host = this.FindControl<ContentControl>("Host");
            var overlay = this.FindControl<ContentControl>("OverlayHost");
            if (overlay != null && ReferenceEquals(overlay.Content, content)) overlay.Content = null;
            if (host != null) { host.Content = content; host.Opacity = 1; }
            if (overlay != null) { overlay.Content = null; overlay.Opacity = 0; }
            _pendingContent = null;
        }

        private void CompleteAnimatedContentSwap()
        {
            var host = this.FindControl<ContentControl>("Host");
            var overlay = this.FindControl<ContentControl>("OverlayHost");
            object? nextContent = overlay?.Content;
            if (overlay != null) overlay.Content = null;
            if (host != null && nextContent != null) { host.Content = nextContent; host.Opacity = 1; }
            else if (host != null) host.Opacity = 1;
            if (overlay != null) { overlay.Opacity = 0; overlay.IsVisible = false; }
            _pendingContent = null;
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
    }
}
