using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using System.Diagnostics;
using OrbitalSIP.Models;
using OrbitalSIP.Services;

namespace OrbitalSIP
{
    public partial class MainWindow : Window
    {
        private enum PreferredMode { Widget, Panel }

        // Designed sizes, as laid out by the views themselves. Nothing reads these
        // directly — the scaled properties below are what the geometry is built from.
        private const double BaseWidgetSize     = 96;
        private const double BaseExpandedWidth  = 320;
        private const double BaseExpandedHeight = 600;
        private const double BaseIncomingWidth  = 436;
        private const double BaseIncomingHeight = 132;
        private const double AnimDurationMs     = 280;

        /// <summary>
        /// Factor the window geometry and the content transform are both built from. The
        /// two have to move together: the transform decides how large the views are drawn,
        /// and the window has to be exactly that large or the operator gets a widget with
        /// empty transparent margin, or one clipped at the edge. Resolved by
        /// <see cref="WidgetScale"/> from the saved setting and the screen.
        /// </summary>
        private double _uiScale = 1.0;

        private double WidgetSize     => BaseWidgetSize     * _uiScale;
        private double ExpandedWidth  => BaseExpandedWidth  * _uiScale;
        private double ExpandedHeight => BaseExpandedHeight * _uiScale;
        private double IncomingWidth  => BaseIncomingWidth  * _uiScale;
        private double IncomingHeight => BaseIncomingHeight * _uiScale;

        private PreferredMode _preferredMode = PreferredMode.Widget;

        private int  _anchorX, _anchorY;
        private bool _isExpanded;

        /// <summary>
        /// Frame interval of the resize animation. Every frame moves and resizes a transparent
        /// top-most window, which is the expensive part; at the old 8 ms it asked for 35 of those
        /// in 280 ms, well past what a 60 Hz screen can show and past what a slow machine can keep
        /// up with — the dropped frames were the stutter.
        /// </summary>
        private static readonly TimeSpan AnimFrameInterval = TimeSpan.FromMilliseconds(16);

        private DispatcherTimer? _animTimer;
        private Stopwatch? _animStopwatch;
        private double _animProgress;
        private double _fromW, _fromH, _toW, _toH;
        private object? _pendingContent;
        private Action? _onAnimComplete;

        /// <summary>Resolved once per animation; OnAnimTick used to look both up by name every frame.</summary>
        private ContentControl? _animHost;
        private ContentControl? _animOverlay;
        private readonly DispatcherTimer _httpErrorHideTimer;

        /// <summary>
        /// Which tab the bottom bar should read as current. Held here rather than asked of
        /// the control, because the control is rebuilt on every screen change and would
        /// have nothing to remember it with — but derived in <see cref="AttachNav"/> from
        /// the screen being installed, never assigned by whoever installed it.
        /// </summary>
        private NavTab _currentTab = NavTab.Dialer;

        /// <summary>
        /// True while Settings is open from the login screen. Nothing else is reachable
        /// without a session, so every tab press goes back to login instead. Set by
        /// <see cref="ShowSettings"/>, which is the only thing that knows the intent, and
        /// then narrowed by <see cref="AttachNav"/> to the screen it describes.
        /// </summary>
        private bool _settingsFromLogin;

        public MainWindow()
        {
            InitializeComponent();

            _httpErrorHideTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(6)
            };
            _httpErrorHideTimer.Tick += (_, __) => HideHttpError();
            HttpErrorNotifier.ErrorOccurred += OnHttpErrorOccurred;
            BackendAuth.SessionExpired += OnSessionExpired;

            // Read before any geometry below: the widget scale comes out of the same file
            // and every size on this screen is derived from it.
            var settings = SipSettings.Load();
            RefreshUiScale(settings.WidgetScalePercent, Screens?.Primary);

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
            this.KeyDown += MainWindow_KeyDown;

            // Wire global hotkeys (work even when app is not focused)
            App.GlobalHotkeys.MuteToggleRequested += (_, __) => DispatchHotkey(h => h.TriggerMute(), null);
            App.GlobalHotkeys.HoldToggleRequested += (_, __) => DispatchHotkey(h => h.TriggerHold(), null);
            App.GlobalHotkeys.HangupPressed       += (_, __) => DispatchHotkey(h => h.TriggerHangup(), iv => iv.TriggerDecline());
            App.GlobalHotkeys.AnswerPressed        += (_, __) => DispatchHotkey(null, iv => iv.TriggerAnswer());

            // Repaint the badges of whatever bar is on screen when a poll changes a number.
            // AttachNav covers the other direction — a bar built after the numbers arrived.
            // Never unhooked, and does not need to be: this window is built once and lives
            // as long as App.NavBadges does. Wired above the initial view below, which is
            // one of the two places the poll starts.
            App.NavBadges.Changed += () =>
            {
                var nav = CurrentNav();
                if (nav != null) App.NavBadges.ApplyTo(nav);
            };

            // Initial view
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
                _ = App.StatusService.SetStateAsync("offline", null);
                App.StatusService.StartPolling();
                App.NavBadges.Start();
                SetMainContent(new Views.WidgetView());
            }

            // Handle tel:/callto:/sip: links opened while the app is already running.
            Program.DialRequested += OnProtocolDialRequested;

            // Handle a tel: link that launched the app itself.
            if (!string.IsNullOrEmpty(Program.InitialDialNumber))
                Dispatcher.UIThread.Post(() => HandleProtocolDial(Program.InitialDialNumber!));
        }

        private void OnProtocolDialRequested(string number) =>
            Dispatcher.UIThread.Post(() => HandleProtocolDial(number));

        /// <summary>Brings the window forward and starts an outgoing call from a tel: link.</summary>
        private void HandleProtocolDial(string number)
        {
            if (string.IsNullOrWhiteSpace(number)) return;

            // Surface the window so the user sees what's happening.
            if (!IsVisible) Show();
            Activate();

            var sip = App.SipService;

            // Not signed in yet \u2014 just show the window; the user can log in and redial.
            if (string.IsNullOrEmpty(sip.CurrentSettings.Username) ||
                string.IsNullOrEmpty(sip.CurrentSettings.Password))
                return;

            // Busy on another call \u2014 don't interrupt it.
            if (sip.State != CallState.Idle) return;

            StartOutgoingCall(number);
        }

        protected override void OnClosed(System.EventArgs e)
        {
            Program.DialRequested -= OnProtocolDialRequested;
            HttpErrorNotifier.ErrorOccurred -= OnHttpErrorOccurred;
            BackendAuth.SessionExpired -= OnSessionExpired;
            _httpErrorHideTimer.Stop();
            base.OnClosed(e);
        }

        private void OnHttpErrorOccurred(string message)
        {
            Dispatcher.UIThread.Post(() => ShowHttpError(message));
        }

        /// <summary>
        /// Set when the session dies mid-call. The login screen has to wait for the call
        /// to end — replacing the active-call view with it would take the hangup, mute and
        /// hold buttons away from an operator who is still talking to someone.
        /// </summary>
        private bool _sessionExpiredPending;

        /// <summary>
        /// The session can no longer be renewed: the refresh token is spent, revoked, or
        /// the account was disabled. Everything behind this window is failing, so put the
        /// login screen back rather than leave the operator with a banner and no way
        /// forward short of restarting the app.
        /// </summary>
        private void OnSessionExpired() => Dispatcher.UIThread.Post(() =>
        {
            ShowHttpError(Services.I18nService.Instance.Get(
                "SessionExpired", "Сессия истекла. Войдите заново."));

            App.StatusService.StopPolling();

            // Here rather than in ShowLoginAfterSessionExpiry below, which the deferred
            // branch does not reach until the call ends: every poll in between goes out
            // with a token the backend has already disowned, and logs two failures for it.
            App.NavBadges.Stop();

            if (App.SipService.State != CallState.Idle)
            {
                _sessionExpiredPending = true;
                return;
            }

            ShowLoginAfterSessionExpiry();
        });

        private void ShowLoginAfterSessionExpiry()
        {
            _sessionExpiredPending = false;

            var host = this.FindControl<ContentControl>("Host");
            if (host?.Content is Views.LoginView) return;

            if (!IsVisible) Show();
            Activate();

            // A resize animation may still be running — the deferred path gets here from a
            // call ending, which is exactly when one starts. Its next tick would overwrite
            // the geometry set below and leave the login screen at widget size.
            _animTimer?.Stop();
            _animTimer = null;
            _animStopwatch = null;

            // Same geometry the constructor uses for a cold start with no credentials.
            var workArea = Screens?.Primary?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
            var left = workArea.X + (workArea.Width - (int)ExpandedWidth) / 2;
            var top  = workArea.Y + (workArea.Height - (int)ExpandedHeight) / 2;

            _isExpanded    = true;
            _preferredMode = PreferredMode.Widget;
            Position = new PixelPoint(left, top);
            Width    = ExpandedWidth;
            Height   = ExpandedHeight;
            _anchorX = left + (int)ExpandedWidth;
            _anchorY = top  + (int)ExpandedHeight;

            ShowLogin();
        }

        private void ShowHttpError(string message)
        {
            var banner = this.FindControl<Border>("HttpErrorBanner");
            var text = this.FindControl<TextBlock>("HttpErrorText");
            if (banner == null || text == null)
                return;

            text.Text = message;
            banner.IsVisible = true;

            _httpErrorHideTimer.Stop();
            _httpErrorHideTimer.Start();
        }

        private void HideHttpError()
        {
            _httpErrorHideTimer.Stop();

            var banner = this.FindControl<Border>("HttpErrorBanner");
            if (banner != null)
                banner.IsVisible = false;
        }

        // ── Global hotkeys ────────────────────────────────────────────
        // Ctrl+M  → mute / unmute during active call
        // Ctrl+H  → hold / resume during active call
        // Escape  → hangup (active call) or decline (incoming)
        // Enter   → answer incoming call

        /// <summary>Dispatches a hotkey action to whichever call-related view is active.</summary>
        private void DispatchHotkey(Action<Views.ActiveCallView>? onActive,
                                    Action<Views.IncomingView>?    onIncoming)
        {
            var host = this.FindControl<ContentControl>("Host");
            if (host == null) return;

            if (onActive  != null && host.Content is Views.ActiveCallView  cv) onActive(cv);
            if (onIncoming != null && host.Content is Views.IncomingView   iv) onIncoming(iv);
        }

        private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
        {
            var host = this.FindControl<ContentControl>("Host");
            if (host == null) return;

            if (host.Content is Views.ActiveCallView callView)
            {
                switch (e.Key)
                {
                    case Key.M when e.KeyModifiers == KeyModifiers.Control:
                        callView.TriggerMute();
                        e.Handled = true;
                        break;
                    case Key.H when e.KeyModifiers == KeyModifiers.Control:
                        callView.TriggerHold();
                        e.Handled = true;
                        break;
                    case Key.Escape:
                        callView.TriggerHangup();
                        e.Handled = true;
                        break;
                }
            }
            else if (host.Content is Views.IncomingView incomingView)
            {
                switch (e.Key)
                {
                    case Key.Enter:
                        incomingView.TriggerAnswer();
                        e.Handled = true;
                        break;
                    case Key.Escape:
                        incomingView.TriggerDecline();
                        e.Handled = true;
                        break;
                }
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
                    visual is ListBoxItem || visual is ScrollBar)
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
            HideStatusPopup();
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
                App.NavBadges.Start();
                _isExpanded = false;
                _preferredMode = PreferredMode.Widget;
                StartAnimation(Width, Height, WidgetSize, WidgetSize, new Views.WidgetView());
            };
            login.OnSettingsRequested += (_, __) => ShowSettings(isFromLogin: true);
            SetMainContent(login);
        }

        // ── Dialer ────────────────────────────────────────────────────

        private void ShowRecents()
        {
            var r = new Views.RecentsView();
            r.OnCloseRequested += (_, __) => ToggleExpanded();
            r.OnExitAppRequested += (_, __) => ShutdownApp();
            r.OutgoingCallRequested += (sender, num) => StartOutgoingCall(num);

            SetMainContent(r);
        }

        private void ShowTasks()
        {
            // TODO(task-8): replace with the real TasksView, and add its arm to the switch
            // in AttachNav. Until then this shows a dialer, and AttachNav lights the Dialer
            // tab because that is what is actually on screen — a lit Tasks tab over a
            // dialpad would be the lying highlight this rework exists to remove.
            SetMainContent(CreateDialerView());
        }

        private void ShowDialer()
        {
            if (App.SipService.State == CallState.Active || App.SipService.State == CallState.OnHold)
            {
                var elapsed = App.SipService.ActiveCallStartedAt.HasValue
                    ? DateTime.Now - App.SipService.ActiveCallStartedAt.Value
                    : TimeSpan.Zero;
                ShowActiveCallView(App.SipService.ActiveCallerId, elapsed);
            }
            else
            {
                SetMainContent(CreateDialerView());
            }
        }

        // ── Settings ──────────────────────────────────────────────────
        // ── Status Popup ──────────────────────────────────────────────
        private void ShowStatusPopup()
        {
            var host = this.FindControl<ContentControl>("PopupHost");
            if (host == null) return;

            var popup = new Views.StatusPopupControl();
            popup.OnCloseRequested += (_, __) => HideStatusPopup();
            popup.OnStatusUpdateRequested += async (_, args) =>
            {
                var (status, duration) = args;
                string? manualStatus = status == "online" ? null : status;

                await App.StatusService.SetStateAsync(manualStatus, null, duration);
                HideStatusPopup();
            };

            host.Content = popup;
            host.IsVisible = true;
            host.IsHitTestVisible = true;
            host.Opacity = 1;
        }

        private void HideStatusPopup()
        {
            var host = this.FindControl<ContentControl>("PopupHost");
            if (host != null)
            {
                host.Opacity = 0;
                host.IsVisible = false;
                host.IsHitTestVisible = false;
                host.Content = null;
            }
        }
        private void ShowSettings(bool isFromLogin = false)
        {
            _settingsFromLogin = isFromLogin;

            var settingsView = new Views.SettingsView();
            settingsView.OnMinimizeRequested += (_, __) => CollapseWidget();
            settingsView.OnExitAppRequested += (_, __) => ShutdownApp();
            settingsView.OnAvatarClicked += (_, __) => ShowStatusPopup();
            settingsView.OnSaveRequested += (_, __) =>
            {
                var settings = SipSettings.Load();
                var current = App.SipService.CurrentSettings;
                // Every session-scoped field, not a hand-maintained subset of them — the
                // inline list this replaced had already gone stale against RefreshToken.
                if (!string.IsNullOrEmpty(current.Username))
                    settings.CopySessionFrom(current);

                // Before the view swap below: the screens that follow are all sized from
                // _uiScale, so changing it afterwards would leave them in a window built
                // for the old scale until the next expand or collapse.
                RescaleWindow(settings.WidgetScalePercent);

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

            // HandleProtocolDial has always checked this; the dial pad never did. Holding
            // Enter in the number box autorepeats KeyDown, and every repeat rebuilt the call
            // view and restarted the entry animation — so the operator watched the call
            // timer reset to 00:00 over and over while CallAsync silently refused each
            // duplicate. Keyboard entry is the common path, so the guard belongs here too.
            if (App.SipService.State != CallState.Idle)
            {
                AppLogger.Log("MainWindow", $"Ignoring an outgoing call request while State={App.SipService.State}.");
                return;
            }

            if (_preferredMode == PreferredMode.Widget)
            {
                ShowActiveCallWidgetView(number, TimeSpan.Zero);
            }
            else
            {
                var callView = new Views.ActiveCallView(number, isOutgoing: true);
                WireActiveCallView(callView);
                SetMainContent(callView);
            }

            await App.SipService.CallAsync(number);
        }

        // ── Incoming call ─────────────────────────────────────────────
        private void ShowIncomingCall(string callerId)
        {
            var incoming = new Views.IncomingView();
            incoming.SetCaller(callerId);

            incoming.OnAnswer  += async (_, __) =>
            {
                try
                {
                    await App.SipService.AnswerAsync();

                    // If AnswerAsync() failed (audio init, exception, or caller hung up mid-answer)
                    // the service rolls back to Idle. ReturnToPreferredMode will already have been
                    // dispatched via OnCallStateChanged → no extra work needed here.
                    if (App.SipService.State != CallState.Active) return;

                    _anchorX = Position.X + (int)Width;
                    _anchorY = Position.Y + (int)Height;
                    if (_preferredMode == PreferredMode.Widget)
                        ShowActiveCallWidgetView(callerId, TimeSpan.Zero);
                    else
                    {
                        StartAnimation(Width, Height, ExpandedWidth, ExpandedHeight);
                        ShowActiveCallView(callerId);
                    }

                    // Campaign call bound to a questionnaire → auto-open it.
                    await MaybeAutoOpenSurveyAsync(callerId);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MainWindow] OnAnswer handler threw: {ex.Message}");
                }
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

        /// <summary>
        /// When the answered call belongs to a campaign that has a bound flow
        /// (анкета), open the questionnaire automatically. Non-campaign calls (or
        /// campaigns with no bound flow) resolve to nothing and are left alone.
        /// </summary>
        private async System.Threading.Tasks.Task MaybeAutoOpenSurveyAsync(string callerNumber)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(callerNumber)) return;
                var flows = await App.FlowsService.SuggestForNumberAsync(callerNumber);
                var flow = flows.FirstOrDefault(f => f.IsActive && f.ActiveVersionId != null);
                if (flow?.Id == null) return;

                Views.SurveyWindowLauncher.Open(this, callerNumber, flow.Id);
            }
            catch (Exception ex)
            {
                Services.AppLogger.Log("MainWindow", $"Auto-survey error: {ex.Message}");
            }
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
            widget.OnMuteToggled += (_, muted)  => App.SipService.SetMuted(muted);
            widget.OnHoldToggled += (_, onHold) => App.SipService.SetHold(onHold);
            widget.OnTransferRequested += (_, __) => ShowActiveCallView(App.SipService.ActiveCallerId, widget.Elapsed);
            widget.OnExpandRequested += (_, __) => ShowActiveCallView(App.SipService.ActiveCallerId, widget.Elapsed);
        }

        private void ShowActiveCallView(string callerId, TimeSpan? elapsed = null)
        {
            // Seeded from the service, exactly as ShowActiveCallWidgetView already does for
            // the mini widget. Without it a panel rebuilt mid-call started from false/false.
            var callView = new Views.ActiveCallView(
                callerId,
                initialElapsed: elapsed,
                isMuted:  App.SipService.IsMuted,
                isOnHold: App.SipService.IsOnHold);
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
            callView.OnExitAppRequested += (_, __) => ShutdownApp();
            callView.OnMuteToggled += (_, muted)  => App.SipService.SetMuted(muted);
            // The state the view asked for, not a blind flip — see SipService.SetHold.
            callView.OnHoldToggled += (_, onHold) => App.SipService.SetHold(onHold);
            callView.OnTransferRequested += async (_, dest) => await App.SipService.BlindTransferAsync(dest);
            callView.OnKeypadRequested += (_, __) => ShowDialer();
            callView.OnAvatarClicked += (_, __) => ShowStatusPopup();
        }

        // ── SIP state changes ─────────────────────────────────────────
        private void OnCallStateChanged(CallState state)
        {
            if (state == CallState.Idle && _sessionExpiredPending)
            {
                // The session died during the call; the login screen was held back until
                // the operator was off it.
                ShowLoginAfterSessionExpiry();
                return;
            }

            // Before the branches below, because neither of them reaches every screen: the
            // idle branch deliberately leaves Settings in place, and a call starting while
            // Settings is open takes no branch at all. Both left the bar stale.
            RefreshNavCallState();

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

        // ── Widget scale ──────────────────────────────────────────────

        /// <summary>
        /// Resolves the layout scale from the saved setting and the screen, applies it to
        /// the content transform, and reports whether it moved.
        ///
        /// The caller is left to deal with the window geometry: at startup it is about to
        /// place the window from scratch, and after a settings change it has an existing
        /// window to resize in place — two different jobs that should not be guessed at
        /// from here.
        /// </summary>
        private bool RefreshUiScale(int percent, Screen? screen)
        {
            var area    = screen?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
            var scaling = screen is { Scaling: > 0 } ? screen.Scaling : 1.0;

            // Logical units, not physical pixels: a screen Windows already scales for DPI
            // hands Avalonia sizes in those units, and scaling it a second time is exactly
            // the oversized widget this feature exists to fix.
            var factor = WidgetScale.Resolve(percent, area.Width / scaling, area.Height / scaling);

            if (Math.Abs(factor - _uiScale) < 0.001) return false;

            _uiScale = factor;
            if (this.FindControl<LayoutTransformControl>("ScaleHost") is { } host)
                host.LayoutTransform = new Avalonia.Media.ScaleTransform(_uiScale, _uiScale);

            return true;
        }

        /// <summary>
        /// Re-resolves the scale and resizes the open window to match, holding the corner
        /// the operator parked it by.
        ///
        /// Bottom-right, the same corner <see cref="StartAnimation"/> anchors on — the
        /// widget lives against the bottom-right of the screen, so growing from the
        /// top-left would walk it off the edge. The result is clamped back into the working
        /// area anyway, because a scale that grew the panel can still push it past the top
        /// of the screen, and this window has no title bar the OS would let you drag back.
        /// </summary>
        private void RescaleWindow(int percent)
        {
            var previous = _uiScale;
            if (!RefreshUiScale(percent, Screens?.ScreenFromWindow(this) ?? Screens?.Primary))
                return;

            var ratio = _uiScale / previous;
            var width  = Width  * ratio;
            var height = Height * ratio;

            _anchorX = Position.X + (int)Width;
            _anchorY = Position.Y + (int)Height;

            var position = new PixelPoint(
                (int)Math.Round(_anchorX - width),
                (int)Math.Round(_anchorY - height));

            var screen = Screens?.ScreenFromWindow(this) ?? Screens?.Primary;
            if (screen != null)
            {
                var size = PixelSize.FromSize(new Size(width, height), screen.Scaling);
                position = WindowPlacement.ClampToWorkingArea(
                    new PixelRect(position, size), screen.WorkingArea);
            }

            Width    = width;
            Height   = height;
            Position = position;
            _anchorX = position.X + (int)width;
            _anchorY = position.Y + (int)height;
        }

        // ── Resize animation ──────────────────────────────────────────
        private void StartAnimation(double fromW, double fromH,
                                    double toW,   double toH,
                                    object? nextContent = null,
                                    Action? onComplete = null)
        {
            HideStatusPopup();
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

            _animOverlay = overlay;
            _animHost = host;

            _animTimer = new DispatcherTimer(AnimFrameInterval, DispatcherPriority.Render, OnAnimTick);
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

            var host = _animHost;
            var overlay = _animOverlay;
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
            dialer.OnExitAppRequested += (_, __) => ShutdownApp();
            dialer.OnAvatarClicked += (_, __) => ShowStatusPopup();
            dialer.OutgoingCallRequested += (_, number) => StartOutgoingCall(number);
            return dialer;
        }

        private void SetMainContent(object content)
        {
            HideStatusPopup();
            var host = this.FindControl<ContentControl>("Host");
            var overlay = this.FindControl<ContentControl>("OverlayHost");
            if (overlay != null && ReferenceEquals(overlay.Content, content)) overlay.Content = null;
            if (host != null) { host.Content = content; host.Opacity = 1; }
            if (overlay != null) { overlay.Content = null; overlay.Opacity = 0; }
            _pendingContent = null;
            AttachNav(content);
        }

        /// <summary>
        /// Hands a freshly built screen's bottom bar its handler and its state.
        ///
        /// Called from both places content reaches the window — SetMainContent and
        /// CompleteAnimatedContentSwap. Missing either one leaves a bar whose buttons do
        /// nothing, which is the whole class of bug this replaced.
        ///
        /// Down the logical tree, not the visual one. SetMainContent hands over a view
        /// built a moment ago and never yet measured, and a UserControl gets its visual
        /// children from a ContentPresenter that is only created on the first measure — so
        /// a visual-tree search finds nothing on that path and would leave the bar dead,
        /// silently, exactly the way the old wiring did. The logical tree is built as the
        /// XAML loads.
        ///
        /// No leak: the reference runs from the control to this window, and the control is
        /// discarded with its screen. That is the opposite direction from the static
        /// App.Updater subscription the control has to unhook by hand.
        ///
        /// Takes the content rather than reading Host itself, and must keep doing so. Both
        /// callers happen to assign Host before calling this, so CurrentNav() would resolve
        /// identically today — that is a coincidence of their current statement order, not
        /// a guarantee, and this runs on content that is being installed rather than
        /// content that is installed.
        /// </summary>
        private void AttachNav(object? content)
        {
            // Both of these describe the screen being installed, so both are read off it
            // rather than assigned by whoever installed it. Hand-placed assignments are the
            // failure this rework exists to remove: "every screen must remember to set
            // _currentTab" fails exactly as silently as "every screen must remember to wire
            // its events" did. _settingsFromLogin had already proved it — cleared only on
            // the exits someone thought of, it let an answered call's panel inherit login
            // mode, where every tab press replaced the call screen with login mid-call,
            // taking hangup, mute and hold with it.
            //
            // Above both returns below, or the screens with no bar stop updating either one.
            // Null is not one of those screens: it means the animation carried no content
            // and Host kept whatever it already had, so there is nothing here to describe.
            // Deriving from it would silently reset both — some dialer screen, no login
            // mode — with no failure to trace it by. Today's two no-content animations both
            // run after IncomingView has already replaced the screen, so it would be
            // harmless; that is their statement order, not a property of this method.
            if (content is null) return;

            _settingsFromLogin &= content is Views.SettingsView;
            _currentTab = content switch
            {
                Views.RecentsView  => NavTab.Recents,
                Views.SettingsView => NavTab.Settings,
                // TasksView joins this list with task 8 — see ShowTasks.
                _                  => NavTab.Dialer,
            };

            if (content is not Control control) return;

            var nav = control.FindLogicalDescendantOfType<Views.BottomNavControl>();
            if (nav == null)
            {
                // Those four have no bottom bar by design. Any other screen arriving here
                // is one whose bar the search missed, and the symptom is a bar that draws
                // normally and does nothing — which is exactly what the four scattered
                // events used to produce, silently, in four places. One place now, so make
                // it a place that says something.
                if (content is not (Views.WidgetView or Views.LoginView or
                                    Views.IncomingView or Views.ActiveCallWidgetView))
                    AppLogger.Log("MainWindow",
                        $"No BottomNavControl found in {control.GetType().Name} — its tab bar is dead.");
                return;
            }

            nav.TabSelected += OnNavTabSelected;
            nav.ActiveTab = _currentTab;
            nav.SetInCall(App.SipService.State is CallState.Active or CallState.OnHold);
            nav.SetLoginMode(_settingsFromLogin);

            // Not in login mode: Settings is reachable from the login screen, and after a
            // session expiry the counters still in hand are the previous session's. Drawn
            // there they would be a claim about an operator who is no longer signed in,
            // sitting on the Recents and Tasks buttons SetLoginMode has just greyed out.
            if (!_settingsFromLogin) App.NavBadges.ApplyTo(nav);
        }

        /// <summary>
        /// The bottom bar of whatever is on screen, or null for the screens that have none
        /// (Widget, Login, Incoming).
        /// </summary>
        private Views.BottomNavControl? CurrentNav() =>
            (this.FindControl<ContentControl>("Host")?.Content as Control)
                ?.FindLogicalDescendantOfType<Views.BottomNavControl>();

        /// <summary>
        /// Re-tells the bar whether a call is up.
        ///
        /// AttachNav answers that question once, when a screen is built. Settings is the
        /// screen that outlives the answer: OnCallStateChanged deliberately leaves it in
        /// place when a call ends, so without this the tab kept advertising a call that
        /// was over — and kept an infinite animation running on an idle window.
        /// </summary>
        private void RefreshNavCallState() =>
            CurrentNav()?.SetInCall(App.SipService.State is CallState.Active or CallState.OnHold);

        private void OnNavTabSelected(object? sender, NavTab tab) => NavigateTo(tab);

        /// <summary>The only place a tab press turns into a screen.</summary>
        private void NavigateTo(NavTab tab)
        {
            // Pressing the tab you are already on used to be inert, because a screen simply
            // did not wire its own tab. Every screen wires every tab now, so without this a
            // stray tap on the lit Settings tab rebuilds the view and takes the operator's
            // unsaved host, credentials, language and scale with it — none of which are
            // committed before OnSaveRequested — a tap on the lit Dialer tab discards a
            // half-typed number, and on the call screen it rebuilds the panel mid-call.
            //
            // Above the login check on purpose: in login mode Settings is the screen you are
            // on, so its tab goes inert, while the Dialer slot's back arrow still leaves.
            if (tab == _currentTab) return;

            if (_settingsFromLogin)
            {
                ShowLogin();
                return;
            }

            switch (tab)
            {
                case NavTab.Dialer:   ShowDialer();   break;
                case NavTab.Recents:  App.NavBadges.MarkRecentsSeen(); ShowRecents();  break;
                case NavTab.Tasks:    ShowTasks();    break;
                case NavTab.Settings: ShowSettings(); break;
            }
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
            AttachNav(nextContent);
        }

        /// <summary>
        /// Exits through the lifetime, which is what raises <c>desktop.Exit</c> — the
        /// only place <see cref="SipService.Dispose"/> runs. The old
        /// <c>Environment.Exit(0)</c> skipped it entirely, so quitting left the operator
        /// REGISTERED on the PBX (the PBX kept ringing a client that was gone), an active
        /// call without its BYE, and the global keyboard hook still installed.
        ///
        /// <c>Shutdown()</c>, not <c>TryShutdown()</c>: this window's Closing handler
        /// cancels the close and hides instead (that is what the tray icon relies on), and
        /// only the forcing overload closes past it. Same call the tray's Exit item makes.
        /// </summary>
        private void ShutdownApp()
        {
            App.NavBadges.Stop();

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
                return;
            }

            System.Environment.Exit(0);
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
    }
}
