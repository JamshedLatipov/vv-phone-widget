using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using OrbitalSIP.Services;

namespace OrbitalSIP
{
    public partial class MainWindow : Window
    {
        private const double WidgetSize     = 96;
        private const double ExpandedWidth  = 320;
        private const double ExpandedHeight = 560;
        private const double IncomingWidth  = 436;
        private const double IncomingHeight = 132;
        private const double AnimDurationMs = 220;

        private int  _anchorX, _anchorY;
        private bool _isExpanded;

        private DispatcherTimer? _animTimer;
        private double _animProgress;
        private double _fromW, _fromH, _toW, _toH;
        private Action? _onAnimComplete;

        public MainWindow()
        {
            InitializeComponent();

            var workArea = Screens?.Primary?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
            var left = workArea.Right - (int)WidgetSize - 24;
            var top  = workArea.Bottom - (int)WidgetSize - 48;
            Position = new PixelPoint(left, top);

            _anchorX = left + (int)WidgetSize;
            _anchorY = top  + (int)WidgetSize;

            this.SystemDecorations = SystemDecorations.None;
            this.PointerPressed   += MainWindow_PointerPressed;
            this.PointerReleased  += MainWindow_PointerReleased;
            this.DoubleTapped     += (_, __) => ExpandOnDoubleTap();

            // Wire SIP events
            var sip = App.SipService;
            sip.IncomingCallReceived += callerId =>
                Dispatcher.UIThread.InvokeAsync(() => ShowIncomingCall(callerId));
            sip.CallStateChanged += state =>
                Dispatcher.UIThread.InvokeAsync(() => OnCallStateChanged(state));

            // Start SIP stack with saved settings
            var settings = SipSettings.Load();
            sip.Start(settings);
        }

        // ── Drag ──────────────────────────────────────────────────────
        private void MainWindow_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
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
            if (_isExpanded)
            {
                return;
            }

            ExpandWidget();
        }

        private void ExpandWidget()
        {
            _isExpanded = true;
            ShowDialer();
            _anchorX = Position.X + (int)Width;
            _anchorY = Position.Y + (int)Height;
            StartAnimation(Width, Height, ExpandedWidth, ExpandedHeight);
        }

        private void CollapseWidget()
        {
            _isExpanded = false;
            StartAnimation(Width, Height, WidgetSize, WidgetSize, onComplete: () =>
            {
                var host = this.FindControl<ContentControl>("Host");
                if (host != null) host.Content = new Views.WidgetView();
            });
        }

        // ── Dialer ────────────────────────────────────────────────────
        private void ShowDialer()
        {
            var host = this.FindControl<ContentControl>("Host");
            if (host == null) return;

            var dialer = new Views.ExpandedView();
            dialer.OnCloseRequested    += (_, __) => CollapseWidget();
            dialer.OnSettingsRequested += (_, __) => ShowSettings();
            dialer.OutgoingCallRequested += (_, number) => StartOutgoingCall(number);
            host.Content = dialer;
        }

        // ── Settings ──────────────────────────────────────────────────
        private void ShowSettings()
        {
            var host = this.FindControl<ContentControl>("Host");
            if (host == null) return;

            var settings = new Views.SettingsView();
            settings.OnBackRequested += (_, __) =>
            {
                // Re-start SIP with new settings after save
                App.SipService.Start(SipSettings.Load());
                ShowDialer();
            };
            host.Content = settings;
        }

        // ── Outgoing call ─────────────────────────────────────────────
        private async void StartOutgoingCall(string number)
        {
            var host = this.FindControl<ContentControl>("Host");
            if (host == null) return;

            var callView = new Views.ActiveCallView(number, isOutgoing: true);
            WireActiveCallView(callView);
            host.Content = callView;

            await App.SipService.CallAsync(number);
            // SipService.CallStateChanged will handle the "call ended" path
        }

        // ── Incoming call ─────────────────────────────────────────────
        private void ShowIncomingCall(string callerId)
        {
            // Expand window if collapsed
            if (!_isExpanded)
            {
                _isExpanded = true;
                _anchorX = Position.X + (int)Width;
                _anchorY = Position.Y + (int)Height;
                StartAnimation(Width, Height, ExpandedWidth, ExpandedHeight);
            }

            var host = this.FindControl<ContentControl>("Host");
            if (host == null) return;

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
                CollapseWidget();
            };

            host.Content = incoming;
            _anchorX = Position.X + (int)Width;
            _anchorY = Position.Y + (int)Height;
            StartAnimation(Width, Height, IncomingWidth, IncomingHeight);
        }

        private void ShowActiveCallView(string callerId)
        {
            var host = this.FindControl<ContentControl>("Host");
            if (host == null) return;

            var callView = new Views.ActiveCallView(callerId);
            WireActiveCallView(callView);
            host.Content = callView;
        }

        private void WireActiveCallView(Views.ActiveCallView callView)
        {
            callView.OnHangup += (_, __) =>
            {
                App.SipService.Hangup();
                CollapseWidget();
            };
            callView.OnMuteToggled += (_, muted) => App.SipService.SetMuted(muted);
            callView.OnKeypadRequested += (_, __) => ShowDialer();
        }

        // ── SIP state changes ─────────────────────────────────────────
        private void OnCallStateChanged(CallState state)
        {
            if (state == CallState.Idle && _isExpanded)
            {
                // Call ended remotely — return to dialer
                ShowDialer();
            }
            else if (state == CallState.Active)
            {
                // Outbound call was answered — mark connected in ActiveCallView
                var host = this.FindControl<ContentControl>("Host");
                if (host?.Content is Views.ActiveCallView av)
                    av.MarkConnected();
            }
        }

        // ── Resize animation ──────────────────────────────────────────
        private void StartAnimation(double fromW, double fromH,
                                    double toW,   double toH,
                                    Action? onComplete = null)
        {
            _animTimer?.Stop();
            _fromW = fromW; _fromH = fromH;
            _toW   = toW;   _toH   = toH;
            _animProgress   = 0;
            _onAnimComplete = onComplete;

            _animTimer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(16),
                DispatcherPriority.Normal,
                OnAnimTick);
            _animTimer.Start();
        }

        private void OnAnimTick(object? sender, EventArgs e)
        {
            _animProgress += 16.0 / AnimDurationMs;
            if (_animProgress >= 1.0) { _animProgress = 1.0; _animTimer!.Stop(); _animTimer = null; }

            var t = 1.0 - Math.Pow(1.0 - _animProgress, 3);
            var w = _fromW + (_toW - _fromW) * t;
            var h = _fromH + (_toH - _fromH) * t;

            Position = new PixelPoint(_anchorX - (int)w, _anchorY - (int)h);
            Width  = w;
            Height = h;

            if (_animProgress >= 1.0)
                _onAnimComplete?.Invoke();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
    }
}

