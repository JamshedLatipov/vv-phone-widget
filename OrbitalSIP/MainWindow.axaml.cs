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
using System.Linq;
using OrbitalSIP.Models;
using OrbitalSIP.Services;

namespace OrbitalSIP
{
    public partial class MainWindow : Window
    {
        private const double AnimDurationMs = 280;

        /// <summary>
        /// Factor the window geometry and the content transform are both built from. The
        /// two have to move together: the transform decides how large the views are drawn,
        /// and the window has to be exactly that large or the operator gets a widget with
        /// empty transparent margin, or one clipped at the edge. Resolved by
        /// <see cref="WidgetScale"/> from the saved setting and the screen.
        ///
        /// The sizes it multiplies live in <see cref="ShellGeometry"/> now, one per surface,
        /// rather than in five constants handed out by hand at every call site.
        /// </summary>
        private double _uiScale = 1.0;

        private int _anchorX, _anchorY;

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
        /// Everything the window is. Changed only through Dispatch, and only to what
        /// ShellRouter returned — assign to it directly and back comes exactly the scatter
        /// of hand-kept flags this work exists to remove.
        /// </summary>
        private UiState _state = UiState.Initial(hasCredentials: false);

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
            // The caller id is dropped rather than carried in the event: SipService writes
            // ActiveCallerId before it raises this, and BuildContent reads it from there when
            // it builds the strip. An event that carried its own copy would be a second
            // answer to "who is calling", and the two would part company on the next rebuild.
            sip.IncomingCallReceived += _ =>
                Dispatcher.UIThread.InvokeAsync(() => Dispatch(new UiEvent.IncomingCall()));
            sip.CallStateChanged += state =>
                Dispatcher.UIThread.InvokeAsync(() => OnCallStateChanged(state));

            this.SystemDecorations = SystemDecorations.None;
            this.PointerPressed   += MainWindow_PointerPressed;
            this.PointerReleased  += MainWindow_PointerReleased;
            this.DoubleTapped     += (_, __) => ExpandOnDoubleTap();
            this.Closing += (s, e) => { e.Cancel = true; this.HideToTray(); };
            this.KeyDown += MainWindow_KeyDown;

            // Wire global hotkeys (work even when app is not focused)
            App.GlobalHotkeys.MuteToggleRequested += (_, __) => HotkeyMute();
            App.GlobalHotkeys.HoldToggleRequested += (_, __) => HotkeyHold();
            App.GlobalHotkeys.HangupPressed       += (_, __) => HotkeyHangup();
            App.GlobalHotkeys.AnswerPressed       += (_, __) => HotkeyAnswer();

            // Repaint the badges of whatever bar is on screen when a poll changes a number.
            // RefreshChrome covers the other direction — a bar built after the numbers arrived.
            // Never unhooked, and does not need to be: this window is built once and lives
            // as long as App.NavBadges does. Wired above the initial view below, which is
            // one of the two places the poll starts.
            App.NavBadges.Changed += () =>
            {
                var nav = CurrentNav();
                if (nav != null) ApplyBadges(nav);
            };

            // Initial view. Both surfaces are drawn by Apply now, so all this branch decides
            // is whether there is a session to resume — and, in practice, there almost never
            // is: Password carries [JsonIgnore] and does not survive a restart, so a cold
            // start goes through the login screen and reaches the widget by way of
            // LoginSucceeded.
            var hasCredentials = !string.IsNullOrEmpty(sip.CurrentSettings.Username) &&
                                 !string.IsNullOrEmpty(sip.CurrentSettings.Password);

            if (hasCredentials)
            {
                sip.Start(settings);
                _ = App.StatusService.SetStateAsync("offline", null);
                App.StatusService.StartPolling();
                App.NavBadges.Start();
            }

            // The corner the widget lives in, placed before the first Apply and without
            // setting Width or Height — those are Apply's to decide. Apply anchors every
            // resize on the bottom-right corner and needs one to hold on to; a login start
            // ignores it, because PlaceCentered overwrites position and size together.
            var widgetSize = ShellGeometry.WidgetSize * _uiScale;
            var startLeft  = workArea.Right  - (int)widgetSize - 24;
            var startTop   = workArea.Bottom - (int)widgetSize - 48;
            Position = new PixelPoint(startLeft, startTop);
            _anchorX = startLeft + (int)widgetSize;
            _anchorY = startTop  + (int)widgetSize;

            _state = UiState.Initial(hasCredentials);
            Apply(null, _state);

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
        ///
        /// The one field this work leaves on MainWindow by hand, though its whole point is
        /// to remove such fields. That is deliberate: it describes not what the window is
        /// but that one event arrived too early and will have to be sent again. UiState
        /// answers "what to draw", and a flag saying "an expired session is also queued up
        /// somewhere" would put back into the record exactly the hidden coupling it exists
        /// to remove. Keeping it outside is safe because it is read in one place and
        /// cleared in that same place, on the first Idle.
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

            // Here rather than beside the Dispatch below, which the deferred branch does not
            // reach until the call ends: every poll in between goes out with a token the
            // backend has already disowned, and logs two failures for it.
            App.NavBadges.Stop();

            if (App.SipService.State != CallState.Idle)
            {
                _sessionExpiredPending = true;
                return;
            }

            SurfaceWindow();
            Dispatch(new UiEvent.SessionExpired());
            CloseDialogWindows();
        });

        /// <summary>
        /// Brings the window out of the tray and to the front.
        ///
        /// Carried over from ShowLoginAfterSessionExpiry, which did this before drawing the
        /// login screen and which the migration to Apply replaced. Apply took over the
        /// centring and the animation kill, but not this: a session that expires while the
        /// operator has the softphone hidden would otherwise redraw login behind a window
        /// nobody can see, and the next thing they know is that their calls stopped arriving.
        /// </summary>
        private void SurfaceWindow()
        {
            if (!IsVisible) Show();
            Activate();
        }

        /// <summary>
        /// Windows this one hid for the tray and has not shown again yet. Window.Hide()
        /// already walks OwnedWindows and hides every entry on its own — see HideToTray,
        /// which leans on that instead of hiding them by hand — but the same walk also
        /// calls RemoveChild on each one as it goes, so OwnedWindows is empty by the time
        /// anything asks again. Without this list, ShowFromTray would have nothing to
        /// show and a session expiring while the softphone sits in the tray would find
        /// no SMS window to close either; see both methods below.
        /// </summary>
        private Window[] _hiddenOwnedWindows = System.Array.Empty<Window>();

        /// <summary>
        /// Shuts the windows this one opened alongside itself. Only on a session expiry:
        /// what is in them belongs to a session that no longer exists, and there is
        /// nothing left to send or save from them.
        ///
        /// A change of screen and the end of a call deliberately do not come here — the
        /// after-call work outlives the conversation, and a half-written SMS draft is worth
        /// more than consistency.
        /// </summary>
        private void CloseDialogWindows()
        {
            Views.TaskWindowLauncher.CloseIfOpen();
            Views.SurveyWindowLauncher.CloseIfOpen();
            Views.ScriptsWindowLauncher.CloseIfOpen();

            // The SMS windows are opened by the screens directly, with no launcher, but
            // this window is their owner all the same. _hiddenOwnedWindows covers the one
            // OwnedWindows cannot: a dialog this window hid for the tray and has not shown
            // again — Hide() already dropped it out of OwnedWindows on its way to hidden
            // (see _hiddenOwnedWindows), so a session that expires before the softphone
            // comes back out of the tray would otherwise leave it open and unreachable.
            foreach (var owned in OwnedWindows.Concat(_hiddenOwnedWindows).ToArray())
                if (owned is Views.SmsComposeDialog) owned.Close();
            _hiddenOwnedWindows = System.Array.Empty<Window>();
        }

        /// <summary>
        /// Hides the main window for the tray. Owned windows are not hidden by hand here:
        /// Avalonia's own Window.Hide() already walks OwnedWindows and hides every one of
        /// them before it hides itself, so a second pass over the same list would just
        /// repeat what Hide() is about to do anyway.
        ///
        /// What Hide() does NOT do is leave them reachable afterwards — hiding a window
        /// clears its Owner and drops it from the owner's OwnedWindows as it goes, the
        /// same as closing would. Captured here, before Hide() takes the list away, so
        /// ShowFromTray still has something to bring back.
        ///
        /// Internal, not private: App.axaml.cs calls this from the tray icon and the
        /// Hide/Show menu items, in place of the direct Hide() they used before.
        ///
        /// Guarded on IsVisible because the tray's Hide item has no matching "already
        /// hidden" state to grey it out against — a second call while already hidden
        /// would capture OwnedWindows a second time, after the first Hide() had already
        /// emptied it, and wipe out the one real list with an empty one.
        /// </summary>
        internal void HideToTray()
        {
            if (!IsVisible) return;

            _hiddenOwnedWindows = OwnedWindows.ToArray();
            Hide();
        }

        /// <summary>
        /// Brings the main window back from the tray along with whatever HideToTray hid.
        /// Show(this) rather than the parameterless Show(): it re-establishes ownership as
        /// well as visibility, so a later hide, or a session expiry, reaches these windows
        /// again instead of finding an OwnedWindows list Hide() already emptied once.
        ///
        /// Internal, not private: App.axaml.cs calls this from the tray icon and the
        /// Hide/Show menu items, in place of the direct Show() they used before.
        /// </summary>
        internal void ShowFromTray()
        {
            Show();
            Activate();
            foreach (var owned in _hiddenOwnedWindows) owned.Show(this);
            _hiddenOwnedWindows = System.Array.Empty<Window>();
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

        // These follow the call, not the screen. They used to be dispatched to Host.Content
        // and did nothing unless a call view was in front, which on main was nearly
        // harmless: the call panel's bar could only reach Settings. Every tab works from
        // every screen now, so an operator can be on Recents, Tasks or Settings mid-call —
        // and on all three, all four of these were silently dead. Global hangup is
        // documented as working even when the app is not focused, so an operator who has
        // navigated away and cannot end the call by the means they were told to use is the
        // one that matters.

        /// <summary>True while there is a call to act on at all, whatever is on screen.</summary>
        private static bool HasCall => App.SipService.State != CallState.Idle;

        /// <summary>True while a call is up far enough to be muted or held.</summary>
        private static bool IsTalking => App.SipService.State is CallState.Active or CallState.OnHold;

        /// <summary>Whatever is in Host, when it is a <typeparamref name="T"/>.</summary>
        private T? HostContent<T>() where T : class =>
            this.FindControl<ContentControl>("Host")?.Content as T;

        /// <summary>
        /// Mute and hold go through a call view when one is in front, because both views
        /// hold their own _muted and _onHold and paint their buttons from it — told only
        /// the service, they would keep drawing the state before the press. Off screen
        /// there is no button to keep honest, so the service is told directly and the next
        /// panel built seeds itself from it, which is what ShowActiveCallView already does.
        /// </summary>
        private void HotkeyMute()
        {
            if (HostContent<Views.ActiveCallView>() is { } panel) { panel.TriggerMute(); return; }
            if (HostContent<Views.ActiveCallWidgetView>() is { } mini) { mini.TriggerMute(); return; }
            if (IsTalking) App.SipService.SetMuted(!App.SipService.IsMuted);
        }

        private void HotkeyHold()
        {
            if (HostContent<Views.ActiveCallView>() is { } panel) { panel.TriggerHold(); return; }
            if (HostContent<Views.ActiveCallWidgetView>() is { } mini) { mini.TriggerHold(); return; }
            if (IsTalking) App.SipService.SetHold(!App.SipService.IsOnHold);
        }

        /// <summary>
        /// Ends whatever is going on. A view in front does its own teardown first — a
        /// retired timer, an invalidated SMS draft — and raises the event that hangs up and
        /// restores the window. Off screen there is no teardown to do, so this does both
        /// halves itself rather than leaving the operator holding a call they cannot end.
        /// </summary>
        private void HotkeyHangup()
        {
            if (HostContent<Views.IncomingView>() is { } incoming) { incoming.TriggerDecline(); return; }
            if (HostContent<Views.ActiveCallView>() is { } panel) { panel.TriggerHangup(); return; }
            if (HostContent<Views.ActiveCallWidgetView>() is { } mini) { mini.TriggerHangup(); return; }
            if (!HasCall) return;

            // No screen change here, and that is the whole of it: both calls end in
            // RollbackToIdle, which announces Idle, and CallStateChanged is what takes the
            // window off the call. A restore driven from here as well would be the second
            // road to the same screen that this rework exists to close.
            if (App.SipService.State == CallState.IncomingRinging) App.SipService.Decline();
            else App.SipService.Hangup();
        }

        /// <summary>
        /// The one of the four that genuinely needs its view, and says so by doing nothing
        /// without it. Answering runs the geometry, the campaign survey and the choice of
        /// screen that follows, all of which live in the handler wired to
        /// IncomingView.OnAnswer. It is also the one that cannot be stranded: the incoming
        /// panel has no bottom bar, so there is no way to navigate off a ringing call.
        /// </summary>
        private void HotkeyAnswer() => HostContent<Views.IncomingView>()?.TriggerAnswer();

        /// <summary>
        /// The focused-window half of the same four actions, routed the same way. Gated on
        /// there being a call rather than on a call view being in front, for the reason
        /// above — but still gated, so Escape stays unhandled on the screens where nothing
        /// is going on and whatever else wants it can have it.
        /// </summary>
        private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.M when e.KeyModifiers == KeyModifiers.Control && IsTalking:
                    HotkeyMute();
                    e.Handled = true;
                    break;
                case Key.H when e.KeyModifiers == KeyModifiers.Control && IsTalking:
                    HotkeyHold();
                    e.Handled = true;
                    break;
                case Key.Escape when HasCall:
                    HotkeyHangup();
                    e.Handled = true;
                    break;
                case Key.Enter when HostContent<Views.IncomingView>() != null:
                    HotkeyAnswer();
                    e.Handled = true;
                    break;
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
        private void ExpandOnDoubleTap()
        {
            if (_state.Shell != Shell.Collapsed) return;
            Dispatch(new UiEvent.ExpandRequested());
        }

        // ── Login ─────────────────────────────────────────────────────
        private Views.LoginView CreateLoginView()
        {
            var login = new Views.LoginView();
            login.OnLoginSuccess += (_, __) =>
            {
                App.NavBadges.Start();
                Dispatch(new UiEvent.LoginSucceeded());
            };
            login.OnSettingsRequested += (_, __) => Dispatch(new UiEvent.LoginSettingsRequested());
            return login;
        }

        // ── Dialer ────────────────────────────────────────────────────

        private Views.RecentsView CreateRecentsView()
        {
            var r = new Views.RecentsView();
            r.OutgoingCallRequested += (sender, num) => StartOutgoingCall(num);
            return r;
        }

        private Views.TasksView CreateTasksView()
        {
            var tasks = new Views.TasksView();
            return tasks;
        }

        // ── Settings ──────────────────────────────────────────────────
        // ── Status Popup ──────────────────────────────────────────────
        private void ShowStatusPopup()
        {
            var host = this.FindControl<ContentControl>("PopupHost");
            if (host == null) return;

            var popup = new Views.StatusPopupControl();
            popup.OnCloseRequested += (_, __) => Dispatch(new UiEvent.StatusPopupToggled(false));
            popup.OnStatusUpdateRequested += async (_, args) =>
            {
                var (status, duration) = args;
                string? manualStatus = status == "online" ? null : status;

                await App.StatusService.SetStateAsync(manualStatus, null, duration);
                // Through the dispatcher, not straight to HideStatusPopup: that would take
                // the popup off the screen while _state.StatusPopup stayed true, and the
                // next press on the avatar would reduce to an equal record and open
                // nothing — for the rest of the time the operator stayed on that screen.
                Dispatch(new UiEvent.StatusPopupToggled(false));
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
        /// <summary>
        /// The settings screen. <paramref name="fromLogin"/> still decides what saving does
        /// — restart the session, or hand the operator back to a login screen they have not
        /// passed yet — but it no longer writes a field: which of the two surfaces this is
        /// belongs to <see cref="Shell.LoginSettings"/> now.
        /// </summary>
        private Views.SettingsView CreateSettingsView(bool fromLogin)
        {
            var settingsView = new Views.SettingsView();
            settingsView.OnSaveRequested += (_, __) =>
            {
                var settings = SipSettings.Load();
                var current = App.SipService.CurrentSettings;
                // Every session-scoped field, not a hand-maintained subset of them — the
                // inline list this replaced had already gone stale against RefreshToken.
                if (!string.IsNullOrEmpty(current.Username))
                    settings.CopySessionFrom(current);

                // Before the Dispatch below: the screens that follow are all sized from
                // _uiScale, so changing it afterwards would leave them in a window built
                // for the old scale until the next expand or collapse.
                RescaleWindow(settings.WidgetScalePercent);

                // What makes an edited host, transport or audio device take effect, and
                // still only off the login path: from login there is no session to restart,
                // and the operator is about to sign in with these settings instead.
                if (!fromLogin) App.SipService.Start(settings);

                Dispatch(new UiEvent.SettingsSaved());
            };
            return settingsView;
        }

        // ── Outgoing call ─────────────────────────────────────────────
        private async void StartOutgoingCall(string number)
        {
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

            // Started before the dispatch and awaited after it, rather than dispatched first
            // and started second. CallAsync writes ActiveCallerId in its synchronous prologue,
            // before its first await, and the strip this raises is built around that name —
            // dispatch first and a widget-home operator gets a strip labelled with the
            // previous caller.
            //
            // The route no longer rides on this ordering: ShellRouter normalizes CallStarted
            // against the call it announces rather than the Idle the service still reports,
            // because the guard above guarantees Idle at this exact point. Before that fix,
            // dialling from the dialpad left the operator on the dialpad for the whole
            // conversation. The name is what is left depending on the order, and it is worth
            // a line to keep: raising the event here rather than off CallStateChanged is what
            // puts the screen up without waiting for the network.
            var call = App.SipService.CallAsync(number);
            Dispatch(new UiEvent.CallStarted());
            await call;
        }

        // ── Incoming call ─────────────────────────────────────────────
        private Views.IncomingView CreateIncomingView(string callerId)
        {
            var incoming = new Views.IncomingView();
            incoming.SetCaller(callerId);

            incoming.OnAnswer  += async (_, __) =>
            {
                try
                {
                    await App.SipService.AnswerAsync();

                    // If AnswerAsync() failed (audio init, exception, or caller hung up
                    // mid-answer) the service rolls back to Idle and announces it, and
                    // CallStateChanged takes the window off the strip → nothing to do here.
                    // The guard is also what keeps CallStarted off a dead call: reduced
                    // against Idle it would put up a call screen for nobody.
                    if (App.SipService.State != CallState.Active) return;

                    Dispatch(new UiEvent.CallStarted());

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
                Dispatch(new UiEvent.IncomingDeclined());
            };

            return incoming;
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

        /// <summary>
        /// How long the call on the line has been up, or zero while it is still ringing.
        ///
        /// From the service rather than from the outgoing view's own stopwatch, which is
        /// what the two call screens used to hand each other across a collapse. They are
        /// rebuilt by <see cref="BuildContent"/> now and have nobody to hand it to, and the
        /// service has held the real answer all along — the old ShowDialer already preferred
        /// it over the view it was replacing.
        /// </summary>
        private static TimeSpan ElapsedCall() =>
            App.SipService.ActiveCallStartedAt is { } startedAt
                ? DateTime.Now - startedAt
                : TimeSpan.Zero;

        private Views.ActiveCallWidgetView CreateActiveCallWidgetView()
        {
            var widget = new Views.ActiveCallWidgetView(
                App.SipService.ActiveCallerId, ElapsedCall(), App.SipService.IsMuted, App.SipService.IsOnHold);

            // Hangup and nothing else. The screen that follows is CallStateChanged's to
            // decide, and a restore driven from here as well is the second road this rework
            // closes — it was ReturnToPreferredMode, and it wrote a geometry _state knew
            // nothing about.
            widget.OnHangup += (_, __) => App.SipService.Hangup();
            widget.OnMuteToggled += (_, muted)  => App.SipService.SetMuted(muted);
            widget.OnHoldToggled += (_, onHold) => App.SipService.SetHold(onHold);
            // Transfer needs the full panel to type a destination into, and expanding is how
            // it gets there — the same event, because to the state model they are one gesture.
            widget.OnTransferRequested += (_, __) => Dispatch(new UiEvent.ExpandRequested());
            widget.OnExpandRequested   += (_, __) => Dispatch(new UiEvent.ExpandRequested());
            return widget;
        }

        private Views.ActiveCallView CreateActiveCallView()
        {
            // Every argument seeded from the service. Without it a panel rebuilt mid-call
            // started from its own field defaults, and the Hold button showed the opposite of
            // the call for the rest of the conversation.
            //
            // isOutgoing picks the Calling caption over the InCall one, so it asks the one
            // question that tells the two apart: Ringing is the outgoing ringback, and an
            // incoming call never reaches this screen before it has been answered.
            var callView = new Views.ActiveCallView(
                App.SipService.ActiveCallerId,
                isOutgoing: App.SipService.State == CallState.Ringing,
                initialElapsed: ElapsedCall(),
                isMuted:  App.SipService.IsMuted,
                isOnHold: App.SipService.IsOnHold);

            callView.OnHangup += (_, __) => App.SipService.Hangup();
            callView.OnMuteToggled += (_, muted)  => App.SipService.SetMuted(muted);
            // The state the view asked for, not a blind flip — see SipService.SetHold.
            callView.OnHoldToggled += (_, onHold) => App.SipService.SetHold(onHold);
            callView.OnTransferRequested += async (_, dest) => await App.SipService.BlindTransferAsync(dest);
            return callView;
        }

        // ── SIP state changes ─────────────────────────────────────────
        private void OnCallStateChanged(CallState state)
        {
            // The deferred login: the session died mid-call and has been waiting for it to
            // end. SessionExpired again rather than CallStateChanged, so the decision stays
            // one row of the table instead of a second road to the login screen.
            if (state == CallState.Idle && _sessionExpiredPending)
            {
                _sessionExpiredPending = false;
                SurfaceWindow();
                Dispatch(new UiEvent.SessionExpired());
                CloseDialogWindows();
                return;
            }

            // A call ending is the one moment the missed-call count can just have moved, and
            // it is also the moment the operator is looking at the bar again.
            if (state == CallState.Idle) _ = App.NavBadges.RefreshNowAsync();

            Dispatch(new UiEvent.CallStateChanged(state));

            // The labels and buttons of a call screen already on show are not a change of
            // screen, so they go around Dispatch.
            var host = this.FindControl<ContentControl>("Host");
            bool isOnHold = state == CallState.OnHold;
            if (host?.Content is Views.ActiveCallView av) { av.MarkConnected(); av.SetStatus(isOnHold); }
            else if (host?.Content is Views.ActiveCallWidgetView awv) awv.SetStatus(isOnHold);

            RefreshChrome(_state);
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

            // Here rather than only at the end of the swap: the incoming screen is visible
            // for the whole 280 ms fade, and a panel dressed at completion spends that fade
            // painting its markup defaults — the wrong tab lit, badges popping in at the last
            // frame, and a call still running with no strip offering the way back to it.
            // CompleteAnimatedContentSwap calls this again on the same panel, which the
            // idempotent subscription in RefreshChrome makes free, and which re-reads any
            // state that moved during the fade.
            RefreshChrome(_state, nextContent);

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
            // The content, not Host, even though Host was assigned two lines up: this dresses
            // the screen being installed, and relying on the statement order above to make
            // the two the same is how the pair drifts apart.
            RefreshChrome(_state, content);
        }

        /// <summary>
        /// Draws the badge counts on a bar — the fourth piece of state this window pushes
        /// into one, beside ActiveTab and SetLoginMode above.
        ///
        /// Here rather than on NavBadgeService, which used to take the control and call
        /// SetBadge itself: that made a service under Services the only thing in the
        /// project reaching into Views, and it was also the only way the counts could be
        /// observed, so nothing could assert them without building a control.
        ///
        /// Both places that put a bar on screen go through this — RefreshChrome when the bar
        /// is built, and the Changed handler when the numbers move under one already up.
        /// </summary>
        private void ApplyBadges(Views.BottomNavControl nav)
        {
            // Not in login mode: Settings is reachable from the login screen, and after a
            // session expiry the counters still in hand are the previous session's. Drawn
            // there they would be a claim about an operator who is no longer signed in,
            // sitting on the Recents and Tasks buttons SetLoginMode has just greyed out.
            if (_state.Shell == Shell.LoginSettings) return;

            nav.SetBadge(NavTab.Tasks, App.NavBadges.OpenTasks, App.NavBadges.HasOverdueTasks);
            nav.SetBadge(NavTab.Recents, App.NavBadges.NewMissed, alert: false);
        }

        /// <summary>
        /// The bottom bar of whatever is on screen, or null for the screens that have none
        /// (Widget, Login, Incoming).
        /// </summary>
        private Views.BottomNavControl? CurrentNav() =>
            (this.FindControl<ContentControl>("Host")?.Content as Control)
                ?.FindLogicalDescendantOfType<Views.BottomNavControl>();

        // No MarkRecentsSeen here. The tap is not what "seen" means — the list arriving in
        // front of the operator is, and RecentsView marks it there, on the load that
        // actually produced rows. Clearing on the press instead cleared the badge over a CDR
        // fetch that then failed silently, and those calls stayed under the watermark for
        // the rest of the day. RecentsView and NavBadgeState.SetMissed both argue the badge
        // must fail lit rather than dark.
        //
        // Nothing else here either: which screen a tab press opens, whether it is the tab
        // already lit, and whether a session even exists to open one are all ShellRouter's
        // answers now.
        private void OnNavTabSelected(object? sender, NavTab tab) => Dispatch(new UiEvent.TabPressed(tab));

        /// <summary>
        /// The one way in to a change of state. Works out the next state, and draws the
        /// difference when there is one.
        /// </summary>
        private void Dispatch(UiEvent e)
        {
            // The event's own payload wins over the live property when it has one. Both come
            // from App.SipService.State, but at different moments: SIP events arrive on
            // background threads and reach here through InvokeAsync, so the property can have
            // moved on by the time this runs. A CallStateChanged(Idle) still queued when the
            // next call starts ringing would otherwise be reduced against IncomingRinging —
            // the arm matching the payload would fire while Normalize, reading the parameter,
            // decided the call was still alive and left the route on the call screen. One
            // reduction, two answers to "is there a call".
            var call = e is UiEvent.CallStateChanged changed ? changed.State : App.SipService.State;

            var next = ShellRouter.Reduce(_state, e, call);

            // Record equality, and it carries more weight than it looks: this is what makes
            // a press on the already-lit tab free. ShellRouter has an arm for that case, but
            // the arm is belt-and-braces — the general arm below it returns an equal record
            // anyway, and this line is what turns "equal" into "do not rebuild the screen".
            if (next == _state) return;

            var prev = _state;
            _state = next;
            Apply(prev, next);
        }

        /// <summary>
        /// Draws the difference between two states. Decides nothing — every decision was
        /// already made in ShellRouter.
        ///
        /// prev == null is the first draw, and then everything is drawn.
        /// </summary>
        private void Apply(UiState? prev, UiState next)
        {
            var box = ShellGeometry.For(next.Shell);
            var width  = box.Width  * _uiScale;
            var height = box.Height * _uiScale;

            var contentChanged = prev is null
                              || prev.Shell != next.Shell
                              || prev.Route != next.Route;

            var content = contentChanged ? BuildContent(next) : null;

            // Passed to RefreshChrome below, so a screen still parked in the overlay gets
            // dressed there rather than 280 ms later when it lands.
            if (box.Placement == ShellPlacement.CenterOnScreen)
            {
                // The login screens are neither animated nor anchored to the corner: they
                // centre, the way they do on a cold start with no credentials. An animation
                // in flight is killed right here — its next tick would overwrite the
                // geometry set below and leave login the size of the widget.
                CancelAnimation();
                if (content != null) SetMainContent(content);
                PlaceCentered(width, height);
            }
            else if (Math.Abs(Width - width) > 1 || Math.Abs(Height - height) > 1)
            {
                _anchorX = Position.X + (int)Width;
                _anchorY = Position.Y + (int)Height;
                StartAnimation(Width, Height, width, height, content);
            }
            else if (content != null)
            {
                SetMainContent(content);
            }

            RefreshChrome(next, content);
            ApplyStatusPopup(next);
        }

        /// <summary>The screen this state calls for. Rebuilt whenever the (Shell, Route) pair changes.</summary>
        private object BuildContent(UiState s) => s.Shell switch
        {
            Shell.Login         => CreateLoginView(),
            Shell.LoginSettings => WrapInShell(CreateSettingsView(fromLogin: true)),
            Shell.Collapsed     => new Views.WidgetView(),
            Shell.Incoming      => CreateIncomingView(App.SipService.ActiveCallerId),
            Shell.CallBar       => CreateActiveCallWidgetView(),
            Shell.Panel         => BuildPanelContent(s.Route),
            _ => throw new ArgumentOutOfRangeException(nameof(s), s.Shell, "Surface has no screen"),
        };

        private object BuildPanelContent(NavRoute route) => WrapInShell(CreateRouteBody(route));

        /// <summary>
        /// Puts a screen inside the panel's chrome and wires the chrome to this window.
        ///
        /// Separate from BuildPanelContent because LoginSettings is not a route and still
        /// needs the same treatment: it is the one login surface that carries the bottom
        /// bar. Shell.Login does not, and is deliberately not wrapped.
        /// </summary>
        private Views.PanelShellView WrapInShell(object body)
        {
            var shell = new Views.PanelShellView { Body = body };

            if (shell.TopBar is { } bar)
            {
                // Settings is the one screen that renames the bar, and it used to do so on
                // its own copy. The bar is shared now, so the override has to be reapplied
                // per screen or the caption silently becomes the operator's username.
                if (body is Views.SettingsView) bar.SetTitle("Settings");

                bar.OnMinimizeRequested += (_, __) => Dispatch(new UiEvent.CollapseRequested());
                bar.OnAvatarClicked     += (_, __) => Dispatch(new UiEvent.StatusPopupToggled(true));
                bar.OnCloseRequested    += (_, __) => ShutdownApp();
            }

            shell.OnReturnRequested += (_, __) => Dispatch(new UiEvent.ReturnStripPressed());

            return shell;
        }

        private object CreateRouteBody(NavRoute route) => route switch
        {
            NavRoute.Dialer   => CreateDialerView(),
            NavRoute.Recents  => CreateRecentsView(),
            NavRoute.Tasks    => CreateTasksView(),
            NavRoute.Settings => CreateSettingsView(fromLogin: false),
            NavRoute.Call     => CreateActiveCallView(),
            _ => throw new ArgumentOutOfRangeException(nameof(route), route, "Route has no screen"),
        };

        /// <summary>Centres the window on the work area of the screen it is currently on.</summary>
        private void PlaceCentered(double width, double height)
        {
            var area = (Screens?.ScreenFromWindow(this) ?? Screens?.Primary)?.WorkingArea
                       ?? new PixelRect(0, 0, 1920, 1080);

            var left = area.X + (area.Width  - (int)width)  / 2;
            var top  = area.Y + (area.Height - (int)height) / 2;

            Position = new PixelPoint(left, top);
            Width    = width;
            Height   = height;
            _anchorX = left + (int)width;
            _anchorY = top  + (int)height;
        }

        /// <summary>Kills an animation in flight without letting it finish.</summary>
        private void CancelAnimation()
        {
            _animTimer?.Stop();
            _animTimer     = null;
            _animStopwatch = null;
            _pendingContent = null;
        }

        /// <summary>
        /// Hands the bottom bar its state. The replacement for AttachNav, which derived the
        /// tab and the login mode from the screen's type — both are already in UiState here.
        ///
        /// Takes the screen rather than reading Host, and must keep doing so. During a resize
        /// the incoming screen is parked in the overlay and shown for the whole 280 ms fade
        /// while Host still holds the outgoing one; reading Host here would dress the screen
        /// that is leaving and let the arriving one paint its markup defaults for the length
        /// of the fade — a blue dialpad on a call screen snapping to green at the last frame,
        /// badges popping in as it lands. That is the bug the comment beside StartAnimation's
        /// own call to this describes, and this signature is what prevents it. Null means "the
        /// screen already in Host", which is every caller that is not mid-animation.
        /// </summary>
        private void RefreshChrome(UiState s, object? content = null)
        {
            var screen = content ?? this.FindControl<ContentControl>("Host")?.Content;
            var nav = (screen as Control)?.FindLogicalDescendantOfType<Views.BottomNavControl>();
            if (nav == null)
            {
                // Those four have no bottom bar by design. Any other screen arriving here is
                // one whose bar the search missed, and the symptom is a bar that draws
                // normally and does nothing — which is exactly what the scattered per-screen
                // wiring used to produce, silently. One place now, so make it a place that
                // says something.
                if (screen is not (null or Views.WidgetView or Views.LoginView or
                                   Views.IncomingView or Views.ActiveCallWidgetView))
                    AppLogger.Log("MainWindow",
                        $"No BottomNavControl found in {screen.GetType().Name} — its tab bar is dead.");
                return;
            }

            nav.TabSelected -= OnNavTabSelected;
            nav.TabSelected += OnNavTabSelected;
            nav.ActiveTab = ShellRouter.TabFor(s.Route);
            nav.SetLoginMode(s.Shell == Shell.LoginSettings);
            ApplyBadges(nav);

            // The screen RefreshChrome already resolved on its first line, not Host. During a
            // resize the arriving screen sits in the overlay while Host still holds the one
            // leaving, so reading Host here would drive the strip on the wrong panel and
            // leave the arriving one blank for the length of every fade — the same trap the
            // doc comment above this method describes.
            if (screen is Views.PanelShellView panel)
                panel.SetReturnStrip(
                    ShellRouter.ShowReturnStrip(s, App.SipService.State),
                    App.SipService.ActiveCallerId,
                    App.SipService.ActiveCallStartedAt);
        }

        private void ApplyStatusPopup(UiState s)
        {
            if (s.StatusPopup) ShowStatusPopup();
            else               HideStatusPopup();
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
            // Again at the end of the fade, on the same bar StartAnimation already dressed:
            // 280 ms is long enough for a call to connect or end under it, and this is what
            // re-reads whatever moved.
            RefreshChrome(_state, nextContent);
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
