using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using System.Threading.Tasks;
using Material.Icons;
using Material.Icons.Avalonia;
using OrbitalSIP.Services;

namespace OrbitalSIP.Views
{
    public partial class ActiveCallView : UserControl
    {
        private DispatcherTimer? _timer;

        /// <summary>
        /// The call clock. Derived from a stopwatch rather than accumulated one tick at a
        /// time, because the timer does not survive the view's whole life: MainWindow's
        /// animated swap parents this view into OverlayHost and then moves it to Host,
        /// which raises a detach/attach pair, and the timer is stopped on the detach. A
        /// tick-counting clock loses whatever time it was not running for — and, without
        /// the restart in OnAttachedToVisualTree below, lost the entire call. A stopwatch
        /// keeps time regardless of whether anything is currently repainting it.
        /// </summary>
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

        /// <summary>Time this call had already run when the view was built.</summary>
        private readonly TimeSpan _initialElapsed;

        /// <summary>Set once the call is over, so a later re-attach does not restart the clock display.</summary>
        private bool _timerRetired;

        public TimeSpan Elapsed => _initialElapsed + _clock.Elapsed;
        private bool _muted;
        private bool _onHold;
        private bool _leadCreated;
        private readonly string _callIdentity;
        private readonly Models.ActiveCallSmsLaunchGuard _smsComposeLaunchGuard = new();
        private Action<CallState>? _smsCallStateChangedHandler;
        private SmsComposeDialog? _activeSmsDialog;

        /// <summary>Null until the first call-context lookup returns — which is
        /// LeadPanelState.Loading, deliberately distinct from «no lead».</summary>
        private Models.LeadCallContextResult? _leadContext;
        private bool _leadContextLoading;

        /// <summary>A 409 card is on screen. It is proof the lead exists, so no
        /// later refresh may downgrade the panel below it — see SelectState.</summary>
        private Models.CreateLeadResult? _leadConflict;

        /// <summary>
        /// Per-call scratch state, static because MainWindow builds a NEW
        /// ActiveCallView on every expand from the mini-widget and after every
        /// transfer. Without this the lookup — whose server side runs a full AMI
        /// endpoint sweep — re-ran on each expand, and a half-typed comment was lost
        /// on every collapse.
        /// </summary>
        private sealed class LeadCallCache
        {
            public string Key = string.Empty;
            public Models.LeadCallContextResult? Context;
            public Models.CreateLeadResult? Conflict;
            public string CommentDraft = string.Empty;
        }

        private static LeadCallCache? _leadCache;

        /// <summary>
        /// Drops the cached lead context and comment draft.
        ///
        /// The cache is static so it can outlive the view — that is the point, since
        /// MainWindow rebuilds this view on every collapse/expand. But it also outlived
        /// the call and the session, leaving the last caller's lead, status and whatever
        /// the operator had typed about them sitting in memory indefinitely, including
        /// after a sign-out. App wires this to the call ending, which is the moment the
        /// cache stops being useful.
        /// </summary>
        public static void ForgetCachedCall() => _leadCache = null;

        public ActiveCallView()
            : this("Unknown", false)
        {
        }

        /// <param name="isMuted">Current microphone state, so a panel rebuilt mid-call does
        /// not start from its own field defaults. The mini widget has always been given
        /// these; this panel was not, and its Hold button ended up showing the opposite of
        /// the call for the rest of the conversation.</param>
        /// <param name="isOnHold">Current hold state, same reason.</param>
        public ActiveCallView(string callerId, bool isOutgoing = false, TimeSpan? initialElapsed = null,
                              bool isMuted = false, bool isOnHold = false)
        {
            _callIdentity = callerId;
            InitializeComponent();

            var callerLabel  = this.FindControl<TextBlock>("CallerLabel");
            var callerNumberLabel = this.FindControl<TextBlock>("CallerNumberLabel");
            var statusLabel  = this.FindControl<TextBlock>("StatusLabel");
            if (callerLabel != null) callerLabel.Text = callerId;
            if (callerNumberLabel != null) callerNumberLabel.Text = callerId;
            if (statusLabel != null) statusLabel.Text = isOutgoing ? Services.I18nService.Instance.Get("Calling") : Services.I18nService.Instance.Get("InCall");

            WireButtons();
            _initialElapsed = initialElapsed ?? TimeSpan.Zero;
            _muted  = isMuted;
            _onHold = isOnHold;
            UpdateMuteUI();
            SetStatus(_onHold);
            UpdateTimeUI();
            StartTimer();

            // A rebuild of this view for the same call reuses what we already
            // fetched; only a genuinely new call hits the network.
            if (!RestoreCachedCallState())
                _ = LoadLeadContextAsync();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            // The animated swap detaches this view from OverlayHost and re-attaches it to
            // Host, and the detach stops the repaint timer. Without this it never ticked
            // again — the same asymmetry RecentsView already avoids.
            if (!_timerRetired && _timer == null) StartTimer();

            if (_smsCallStateChangedHandler == null)
            {
                _smsCallStateChangedHandler = state =>
                {
                    var capturedState = state;
                    if (!Models.ActiveCallSmsLifecycle.ShouldInvalidate(capturedState))
                        return;

                    Dispatcher.UIThread.Post(InvalidateSmsComposeForCallLifecycle);
                };
                App.SipService.CallStateChanged += _smsCallStateChangedHandler;
            }

            if (!IsSourceCallCurrent())
                InvalidateSmsComposeForCallLifecycle();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            // Minimize, expand and transfer all swap in a freshly built view, and the
            // outgoing one only ever stopped its timer when the operator hung up from
            // this very view. Every other swap left a 1 Hz timer ticking against a
            // detached tree — which also keeps that tree alive — for the whole session.
            //
            // Only the repaint stops here; Elapsed comes from _clock, so a detach that is
            // really the overlay→host reparenting loses nothing and OnAttachedToVisualTree
            // starts painting again.
            _timer?.Stop();
            _timer = null;

            InvalidateSmsComposeForCallLifecycle();
            if (_smsCallStateChangedHandler != null)
            {
                App.SipService.CallStateChanged -= _smsCallStateChangedHandler;
                _smsCallStateChangedHandler = null;
            }

            base.OnDetachedFromVisualTree(e);
        }

        // в”Ђв”Ђ Timer в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
        private void StartTimer()
        {
            _timer = new DispatcherTimer(
                TimeSpan.FromSeconds(1),
                DispatcherPriority.Render,
                OnTick);
            _timer.Start();
        }

        /// <summary>
        /// Ends the clock for good, as opposed to the detach in OnDetachedFromVisualTree
        /// which only pauses the repaint. Called from the hangup paths, so a later
        /// re-attach does not put a running timer back on a call that is over.
        /// </summary>
        private void RetireTimer()
        {
            _timerRetired = true;
            _timer?.Stop();
            _timer = null;
        }

        private void OnTick(object? sender, EventArgs e) => UpdateTimeUI();

        private void UpdateTimeUI()
        {
            var label = this.FindControl<TextBlock>("TimerLabel");
            var minutesLabel = this.FindControl<TextBlock>("TimerMinutesLabel");
            var secondsLabel = this.FindControl<TextBlock>("TimerSecondsLabel");
            var elapsed = Elapsed;
            var totalMinutes = (int)elapsed.TotalMinutes;
            var seconds = elapsed.Seconds;

            if (label != null)
                label.Text = elapsed.TotalHours >= 1
                    ? elapsed.ToString(@"h\:mm\:ss")
                    : elapsed.ToString(@"mm\:ss");

            if (minutesLabel != null)
                minutesLabel.Text = totalMinutes.ToString("00");

            if (secondsLabel != null)
                secondsLabel.Text = seconds.ToString("00");
        }

        /// <summary>
        /// Paints the hold state. Also the resync point: MainWindow calls this on every
        /// Active/OnHold transition, so the Hold button follows the call even if the two
        /// ever drift apart again.
        /// </summary>
        public void SetStatus(bool isOnHold)
        {
            _onHold = isOnHold;
            UpdateHoldUI();
            UpdateDtmfPadEnabled();

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

        // в”Ђв”Ђ Buttons в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
        private void WireButtons()
        {
            var hangup = this.FindControl<Button>("HangupBtn");
            if (hangup != null)
                hangup.Click += (_, __) =>
                {
                    RetireTimer();
                    InvalidateSmsComposeForCallLifecycle();
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
                keypad.Click += (_, __) => ToggleDtmfPanel();

            var scriptBtn = this.FindControl<Button>("ScriptBtn");
            if (scriptBtn != null)
                scriptBtn.Click += (_, __) => ShowScriptsDialog();

            var surveyBtn = this.FindControl<Button>("SurveyBtn");
            if (surveyBtn != null)
                surveyBtn.Click += (_, __) => ShowSurveyDialog();

            var leadBtn = this.FindControl<Button>("CreateLeadBtn");
            if (leadBtn != null)
                leadBtn.Click += SafeHandler.Click("CreateLead", CreateLeadAsync);

            var taskBtn = this.FindControl<Button>("TaskBtn");
            if (taskBtn != null)
                taskBtn.Click += (_, __) => ShowTaskDialog();

            var leadRetryBtn = this.FindControl<Button>("LeadRetryBtn");
            if (leadRetryBtn != null)
                leadRetryBtn.Click += SafeHandler.Click("LeadPanel", () => LoadLeadContextAsync());

            var leadOpenBtn = this.FindControl<Button>("LeadOpenBtn");
            if (leadOpenBtn != null)
                leadOpenBtn.Click += (_, __) => OpenLeadInCrm();

            var leadTransferOwnerBtn = this.FindControl<Button>("LeadTransferOwnerBtn");
            if (leadTransferOwnerBtn != null)
                leadTransferOwnerBtn.Click += (_, __) => TransferToLeadOwner();

            var leadCommentAddBtn = this.FindControl<Button>("LeadCommentAddBtn");
            if (leadCommentAddBtn != null)
                leadCommentAddBtn.Click += SafeHandler.Click("LeadPanel", AddLeadCommentAsync);

            // Keeps a half-typed comment alive across a collapse/expand, which
            // rebuilds this whole view.
            var leadCommentBox = this.FindControl<TextBox>("LeadCommentBox");
            if (leadCommentBox != null)
                leadCommentBox.TextChanged += (_, __) => RememberCommentDraft(leadCommentBox.Text);

            var smsBtn = this.FindControl<Button>("SmsBtn");
            if (smsBtn != null)
                smsBtn.Click += SafeHandler.Click("ActiveCallSms", ShowSmsComposeDialog);

            var callInfoBtn = this.FindControl<Button>("CallInfoBtn");
            if (callInfoBtn != null)
                callInfoBtn.Click += (_, __) => ToggleCallInfoPanel();

            var callInfoCloseBtn = this.FindControl<Button>("CallInfoCloseBtn");
            if (callInfoCloseBtn != null)
                callInfoCloseBtn.Click += (_, __) => HideCallInfoPanel();

            var copy = this.FindControl<Button>("CopyCallerBtn");
            if (copy != null)
                copy.Click += SafeHandler.Click("ActiveCall", CopyCallerAsync);
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

            await IconFlash.ConfirmAsync(this.FindControl<Button>("CopyCallerBtn")?.Content);
        }

        private void ToggleMute()
        {
            _muted = !_muted;
            UpdateMuteUI();
            OnMuteToggled?.Invoke(this, _muted);
        }

        /// <summary>Paints the microphone button from <see cref="_muted"/>.</summary>
        private void UpdateMuteUI()
        {
            var icon  = this.FindControl<MaterialIcon>("MuteIcon");
            var label = this.FindControl<TextBlock>("MuteLabel");
            var btn   = this.FindControl<Button>("MuteBtn");
            var i18n  = Services.I18nService.Instance;

            if (icon  != null)
            {
                icon.Foreground = new SolidColorBrush(_muted ? Color.Parse("#FFFFFF") : Color.Parse("#DDE7F3"));
                icon.Kind = _muted ? MaterialIconKind.MicrophoneOff : MaterialIconKind.Microphone;
            }
            // Was hardcoded English in an otherwise translated interface.
            if (label != null) label.Text  = _muted ? i18n.Get("Unmute") : i18n.Get("Mute");
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
            AppLogger.Log("CreateLead", $"Caller number: {Models.LogRedaction.Phone(callerNumber)}");
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

            // The button is disabled across an await with no finally underneath it, so a
            // throw from here down used to leave it dead for the rest of the call — the
            // operator could neither create the lead nor retry. LeadService catches
            // everything internally today, which is the only reason this has not bitten.
            var created = false;
            try
            {
                await CreateLeadInnerAsync(request, leadBtn, c => created = c);
            }
            finally
            {
                if (!created && leadBtn != null) leadBtn.IsEnabled = true;
            }
        }

        private async Task CreateLeadInnerAsync(
            Models.CreateLeadRequest request, Button? leadBtn, Action<bool> reportCreated)
        {
            var result = await App.LeadService.CreateLeadAsync(request);
            bool success = result.Success;
            AppLogger.Log("CreateLead", $"Request success: {success}");

            if (result.AlreadyOpen)
            {
                // The caller already had a lead — the race this panel normally
                // prevents (context said «none», someone created one in between).
                // Render what the 409 already told us, then refresh in the
                // background for the owner and the actions it cannot carry.
                AppLogger.Log("CreateLead", $"Lead already open: id={result.ExistingLeadId?.ToString() ?? "?"} name='{result.ExistingLeadName}'");

                // Recorded BEFORE the refresh: SelectState reads it to refuse any
                // downgrade below the card, and without it a refresh returning
                // `none` would put the create button back.
                _leadConflict = result;
                if (leadBtn != null) leadBtn.IsEnabled = true;

                RenderConflictLeadCard(result);
                CacheForThisCall();
                _ = LoadLeadContextAsync(showLoading: false);
                return;
            }

            if (success)
            {
                _leadCreated = true;
                reportCreated(true);   // the button stays disabled: the lead exists now

                // Refresh so the cache holds the lead that now exists. Without this
                // the cached «none» outlives the create, and the next expand — which
                // rebuilds this view and resets _leadCreated — would re-arm the
                // create button for a caller who now has a lead.
                _ = LoadLeadContextAsync(showLoading: false);

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
            // A failure needs no re-enable here: the finally in CreateLeadAsync gives the
            // button back for everything that did not end in a created lead, which is the
            // same set plus the throws this used to miss.
        }

        // ── Active-lead panel ────────────────────────────────────────────────
        private string CallerNumber() =>
            this.FindControl<TextBlock>("CallerNumberLabel")?.Text?.Trim() ?? string.Empty;

        /// <summary>
        /// Loads the caller's lead context and repaints the panel. Any failure ends
        /// up as LeadPanelState.Unavailable — never as «no lead», which would put
        /// the create button back in front of a caller who already has one.
        ///
        /// <paramref name="showLoading"/> is false for the background refresh after
        /// a 409: the card is already on screen from the conflict payload, and
        /// resetting to «Ищем активный лид…» would blank it for the duration of the
        /// request — the exact wait that rendering from the 409 exists to avoid.
        /// </summary>
        private async Task LoadLeadContextAsync(bool showLoading = true)
        {
            if (_leadContextLoading) return;

            // Nothing to look up for a withheld number — and the endpoint would 400
            // on it anyway. SelectState renders this as Hidden.
            if (!LeadCallPanelPresenter.IsLookupablePhone(CallerNumber()))
            {
                AppLogger.Log("LeadPanel", "Skipping call-context lookup: caller number has no digits.");
                ApplyLeadPanelState();
                return;
            }

            _leadContextLoading = true;

            if (showLoading)
            {
                _leadContext = null;
                ApplyLeadPanelState();
            }

            try
            {
                _leadContext = await App.LeadService.GetCallContextAsync(CallerNumber());
            }
            finally
            {
                _leadContextLoading = false;
            }

            CacheForThisCall();
            ApplyLeadPanelState();
        }

        /// <summary>
        /// Null when this call has no reliable identity — nothing is cached then.
        /// Keyed off CallerNumber(), the same source the lookup itself uses, so the
        /// key can never describe a different number than the cached result.
        /// </summary>
        private string? CurrentCallKey() =>
            LeadCallPanelPresenter.BuildCallKey(
                CallerNumber(),
                App.SipService?.ActiveCallStartedAt);

        /// <summary>
        /// Restores context, conflict card and comment draft when this view is a
        /// rebuild for the SAME call. A different call — including a call-back from
        /// the same number — misses, because reusing a stale «no lead» would offer a
        /// create for a caller who has since acquired a lead.
        /// </summary>
        private bool RestoreCachedCallState()
        {
            var key = CurrentCallKey();
            if (key == null || _leadCache == null || _leadCache.Key != key) return false;

            _leadContext = _leadCache.Context;
            _leadConflict = _leadCache.Conflict;

            if (_leadConflict != null) RenderConflictLeadCard(_leadConflict);
            ApplyLeadPanelState();

            var box = this.FindControl<TextBox>("LeadCommentBox");
            if (box != null) box.Text = _leadCache.CommentDraft;

            return _leadContext != null || _leadConflict != null;
        }

        /// <summary>Returns the cache entry for the current call, or null when the
        /// call has no reliable identity — in which case nothing is stored.</summary>
        private LeadCallCache? CacheEntry()
        {
            var key = CurrentCallKey();
            if (key == null) return null;

            if (_leadCache == null || _leadCache.Key != key)
                _leadCache = new LeadCallCache { Key = key };

            return _leadCache;
        }

        private void CacheForThisCall()
        {
            var entry = CacheEntry();
            if (entry == null) return;

            entry.Context = _leadContext;
            entry.Conflict = _leadConflict;
        }

        private void RememberCommentDraft(string? text)
        {
            var entry = CacheEntry();
            if (entry != null) entry.CommentDraft = text ?? string.Empty;
        }

        private void ApplyLeadPanelState()
        {
            var state = LeadCallPanelPresenter.SelectState(
                CallerNumber(), _leadContext, _leadConflict != null);
            var context = _leadContext?.Context;

            var panel = this.FindControl<Border>("LeadPanel");
            var loading = this.FindControl<TextBlock>("LeadLoadingLabel");
            var unavailable = this.FindControl<StackPanel>("LeadUnavailableBlock");
            var card = this.FindControl<StackPanel>("LeadCardBlock");
            var createBtn = this.FindControl<Button>("CreateLeadBtn");

            if (createBtn != null && !_leadCreated)
                SetCreateButtonVisible(createBtn, LeadCallPanelPresenter.ShowsCreateButton(state));

            if (loading != null) loading.IsVisible = state == LeadPanelState.Loading;
            if (unavailable != null) unavailable.IsVisible = state == LeadPanelState.Unavailable;
            if (card != null) card.IsVisible = LeadCallPanelPresenter.ShowsLeadCard(state);

            // Nothing to say in OfferCreate (the button speaks) or Hidden.
            if (panel != null)
                panel.IsVisible = state == LeadPanelState.Loading
                                  || state == LeadPanelState.Unavailable
                                  || LeadCallPanelPresenter.ShowsLeadCard(state);

            // Only a full ActiveLead repaints the card. ConflictLead leaves whatever
            // RenderConflictLeadCard already put there — the refresh had nothing
            // better to offer.
            if (state == LeadPanelState.ActiveLead && context?.Lead != null)
                RenderLeadCard(context);
        }

        private void RenderLeadCard(Models.LeadCallContext context)
        {
            var lead = context.Lead!;
            var i18n = Services.I18nService.Instance;

            var headline = this.FindControl<TextBlock>("LeadHeadlineLabel");
            if (headline != null) headline.Text = LeadCallPanelPresenter.LeadHeadline(lead);

            var subline = this.FindControl<TextBlock>("LeadSublineLabel");
            if (subline != null)
            {
                // Unknown status falls back to the raw backend value, exactly as the
                // CRM's own statusLabel() does.
                var statusKey = LeadCallPanelPresenter.LeadStatusKey(lead.Status);
                var statusText = statusKey == null ? lead.Status : i18n.Get(statusKey);
                subline.Text = LeadCallPanelPresenter.LeadSubline(statusText, lead.StageName);
            }

            var ownerLabel = this.FindControl<TextBlock>("LeadOwnerLabel");
            if (ownerLabel != null)
            {
                var ownerName = string.IsNullOrWhiteSpace(context.Owner?.FullName)
                    ? i18n.Get("LeadPanelOwnerUnknown")
                    : context.Owner!.FullName;
                ownerLabel.Text = $"{i18n.Get("LeadPanelOwner")}: {ownerName}";
            }

            var openBtn = this.FindControl<Button>("LeadOpenBtn");
            if (openBtn != null)
                openBtn.IsVisible = context.Actions.CanOpenLead
                                    && LeadCallPanelPresenter.IsLaunchableUrl(lead.Url);

            RenderTransferRow(context);

            var commentBlock = this.FindControl<StackPanel>("LeadCommentBlock");
            if (commentBlock != null) commentBlock.IsVisible = context.Actions.CanComment;
        }

        /// <summary>
        /// Paints the lead card from a 409 alone, so the operator sees the existing
        /// lead immediately. Transfer/comment/open stay hidden: the conflict carries
        /// the lead but not the owner's name, extension or this operator's rights —
        /// LoadLeadContextAsync fills those in a moment later.
        /// </summary>
        private void RenderConflictLeadCard(Models.CreateLeadResult conflict)
        {
            var conflictCreateBtn = this.FindControl<Button>("CreateLeadBtn");
            if (conflictCreateBtn != null) SetCreateButtonVisible(conflictCreateBtn, false);

            SetVisible<Border>("LeadPanel", true);
            SetVisible<StackPanel>("LeadCardBlock", true);
            SetVisible<TextBlock>("LeadLoadingLabel", false);
            SetVisible<StackPanel>("LeadUnavailableBlock", false);

            var headline = this.FindControl<TextBlock>("LeadHeadlineLabel");
            if (headline != null)
                headline.Text = LeadCallPanelPresenter.ConflictHeadline(
                    conflict.ExistingLeadId, conflict.ExistingLeadName, conflict.Message);

            var subline = this.FindControl<TextBlock>("LeadSublineLabel");
            if (subline != null)
            {
                var statusKey = LeadCallPanelPresenter.LeadStatusKey(conflict.ExistingLeadStatus);
                subline.Text = statusKey == null
                    ? conflict.ExistingLeadStatus ?? string.Empty
                    : Services.I18nService.Instance.Get(statusKey);
            }

            var ownerLabel = this.FindControl<TextBlock>("LeadOwnerLabel");
            if (ownerLabel != null) ownerLabel.Text = string.Empty;

            SetVisible<Button>("LeadOpenBtn", false);
            SetVisible<Button>("LeadTransferOwnerBtn", false);
            SetVisible<TextBlock>("LeadTransferBlockedLabel", false);
            SetVisible<StackPanel>("LeadCommentBlock", false);
        }

        /// <summary>
        /// Hides «Создать лид» AND collapses its column, so the five-button action
        /// grid closes up instead of leaving a hole on the left.
        /// </summary>
        private void SetCreateButtonVisible(Button createBtn, bool visible)
        {
            createBtn.IsVisible = visible;
            createBtn.Margin = visible ? new Thickness(0, 0, 2, 0) : new Thickness(0);

            var grid = this.FindControl<Grid>("CallActionsGrid");
            if (grid == null || grid.ColumnDefinitions.Count == 0) return;

            grid.ColumnDefinitions[0].Width = visible
                ? new GridLength(1, GridUnitType.Star)
                : new GridLength(0);
        }

        private void SetVisible<T>(string name, bool visible) where T : Control
        {
            var control = this.FindControl<T>(name);
            if (control != null) control.IsVisible = visible;
        }

        private void RenderTransferRow(Models.LeadCallContext context)
        {
            var i18n = Services.I18nService.Instance;
            var canTransfer = LeadCallPanelPresenter.CanTransferToOwner(context);

            var transferBtn = this.FindControl<Button>("LeadTransferOwnerBtn");
            if (transferBtn != null)
            {
                // Shown whenever there is an owner to name, disabled when blocked —
                // the reason below is what makes the disabled state informative.
                transferBtn.IsVisible = context.Owner != null;
                transferBtn.IsEnabled = canTransfer;
                transferBtn.Opacity = canTransfer ? 1.0 : 0.5;
                transferBtn.Content = $"{i18n.Get("LeadPanelTransferTo")} {context.Owner?.FullName}".Trim();
            }

            var blockedLabel = this.FindControl<TextBlock>("LeadTransferBlockedLabel");
            if (blockedLabel != null)
            {
                // Straight from the server's reason — see TransferBlockedKey on why
                // this must not be re-derived from ManualStatus.
                var key = canTransfer
                    ? null
                    : LeadCallPanelPresenter.TransferBlockedKeyOrDefault(
                        context.Actions.TransferBlockedReason);

                blockedLabel.IsVisible = key != null;
                blockedLabel.Text = key == null ? string.Empty : i18n.Get(key);
            }
        }

        private void OpenLeadInCrm()
        {
            var url = _leadContext?.Context?.Lead?.Url;

            // Re-validated here even though the backend builds and checks it: this
            // string is handed to the OS shell.
            if (!LeadCallPanelPresenter.IsLaunchableUrl(url))
            {
                AppLogger.Log("LeadPanel", $"Refused to open non-http(s) lead url: '{url}'");
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(url!) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AppLogger.Log("LeadPanel", $"Failed to open lead url: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void TransferToLeadOwner()
        {
            var context = _leadContext?.Context;
            if (!LeadCallPanelPresenter.CanTransferToOwner(context)) return;

            // The dialable extension, never a SIP endpoint id.
            var extension = context!.Owner!.ExtensionNumber!.Trim();
            AppLogger.Log("LeadPanel", $"Transferring call to lead owner extension {extension}");
            OnTransferRequested?.Invoke(this, extension);
        }

        private async Task AddLeadCommentAsync()
        {
            var context = _leadContext?.Context;
            var lead = context?.Lead;
            if (lead == null || context?.Actions.CanComment != true) return;

            var box = this.FindControl<TextBox>("LeadCommentBox");
            var comment = box?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(comment)) return;

            var addBtn = this.FindControl<Button>("LeadCommentAddBtn");
            if (addBtn != null) addBtn.IsEnabled = false;

            try
            {
                var payload = new Models.AddCallCommentRequest { Comment = comment };
                var linked = await AttachCallLinkAsync(payload, CallerNumber());

                var ok = await App.LeadService.AddCallCommentAsync(lead.Id, payload);
                AppLogger.Log("LeadPanel", $"Add call comment to lead {lead.Id}: ok={ok} linked={linked}");

                if (ok && box != null) box.Text = string.Empty;
                if (ok) RememberCommentDraft(string.Empty);

                _ = ShowCommentStatusAsync(
                    LeadCallPanelPresenter.CommentStatusKey(saved: ok, linkedToCall: linked));
            }
            finally
            {
                if (addBtn != null) addBtn.IsEnabled = true;
            }
        }

        /// <summary>
        /// Links the comment to this call the same way ShowTaskDialog links a task.
        /// A callLogId that the backend cannot resolve is rejected outright (400),
        /// so only an id actually returned by SaveCallLogAsync is ever sent; the raw
        /// uniqueId is the fallback because an unresolvable one still saves the
        /// comment, just without call linkage.
        /// </summary>
        /// <returns>
        /// False when the comment will be saved with no link to the call at all.
        /// The comment is NOT failed over this — losing the link is worth far less
        /// than losing the note the operator just typed mid-call — but the operator
        /// is told, because the link to the recording is the point of the
        /// attribution.
        /// </returns>
        private static async Task<bool> AttachCallLinkAsync(Models.AddCallCommentRequest payload, string? number)
        {
            if (string.IsNullOrWhiteSpace(number)) return false;

            // notifyErrors:false — a failure here costs only the link, and an error
            // banner during a live call for an action that then succeeds is worse
            // than the missing link itself. Both calls still log in full.
            var uniqueId = await App.ScriptService.GetChannelUniqueIdAsync(number!, notifyErrors: false);
            if (string.IsNullOrWhiteSpace(uniqueId))
            {
                AppLogger.Log("LeadPanel", "Call comment: could not resolve the channel uniqueId; saving without a call link.");
                return false;
            }

            var callLogId = await App.ScriptService.SaveCallLogAsync(uniqueId!, notifyErrors: false);
            if (!string.IsNullOrWhiteSpace(callLogId))
            {
                payload.CallLogId = callLogId;
                return true;
            }

            // Only an id that SaveCallLogAsync actually returned may go in
            // callLogId — the backend 400s an unresolvable one. The raw uniqueId is
            // the safe fallback: it saves the comment either way, linked if the
            // backend can resolve it.
            AppLogger.Log("LeadPanel", "Call comment: no callLogId; falling back to callUniqueId.");
            payload.CallUniqueId = uniqueId;
            return true;
        }

        private async Task ShowCommentStatusAsync(string key)
        {
            var label = this.FindControl<TextBlock>("LeadCommentStatusLabel");
            if (label == null) return;

            label.Text = Services.I18nService.Instance.Get(key);
            label.IsVisible = true;
            await Task.Delay(2500);
            label.IsVisible = false;
        }

        private void ShowSurveyDialog()
        {
            var topLevel = TopLevel.GetTopLevel(this) as Window;
            if (topLevel == null) return;

            var callerNumber = this.FindControl<TextBlock>("CallerNumberLabel")?.Text?.Trim() ?? "";
            SurveyWindowLauncher.Open(topLevel, callerNumber);
        }

        private void ShowTaskDialog()
        {
            var topLevel = TopLevel.GetTopLevel(this) as Window;
            if (topLevel == null) return;

            var callerNumber = this.FindControl<TextBlock>("CallerNumberLabel")?.Text?.Trim() ?? "";

            // Resolve the call anchor NOW, while the call is still up. The window is no
            // longer modal, so the operator can hang up before saving — and
            // /api/cdr/channel-uniqueid only ever resolves the ACTIVE call, so a lookup
            // deferred to Save would come back empty and silently drop the link.
            var callLogId = ResolveCallLogIdAsync(callerNumber);

            TaskWindowLauncher.Open(topLevel, callerNumber, request => _ = SubmitTaskAsync(request, callLogId));
        }

        /// <summary>
        /// Resolves the CallLog row id that links a task to this call — the field the CRM
        /// reads to show the call on a task. POST /api/cdr/log upserts by uniqueId, so
        /// running this when the window opens (rather than on save) only ever creates the
        /// row the call would get anyway.
        ///
        /// Every step reports itself: the whole chain used to fail into a null callLogId,
        /// which is dropped from the payload, so an unlinked task looked like a clean 201.
        /// </summary>
        private static async Task<string?> ResolveCallLogIdAsync(string callerNumber)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(callerNumber))
                {
                    AppLogger.Log("CreateTask", "No call anchor: the call view carries no caller number.");
                    return null;
                }

                var uniqueId = await App.ScriptService.GetChannelUniqueIdAsync(callerNumber);
                if (string.IsNullOrWhiteSpace(uniqueId))
                {
                    AppLogger.Log("CreateTask", $"No call anchor: no active channel for '{callerNumber}'.");
                    return null;
                }

                var callLogId = await App.ScriptService.SaveCallLogAsync(uniqueId);
                if (string.IsNullOrWhiteSpace(callLogId))
                {
                    AppLogger.Log("CreateTask", $"No call anchor: no CallLog row for uniqueId '{uniqueId}'.");
                    return null;
                }

                AppLogger.Log("CreateTask", $"Call anchor resolved: callLogId '{callLogId}'.");
                return callLogId;
            }
            catch (Exception ex)
            {
                AppLogger.Log("CreateTask", $"Call anchor lookup threw: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Everything the old ShowTaskDialog did after its await. The task window is no
        /// longer modal, so this runs off its TaskConfirmed event instead.
        /// </summary>
        private async Task SubmitTaskAsync(Models.CreateTaskRequest request, Task<string?> callLogIdLookup)
        {
            // Assign to the current operator (numeric user id from the JWT), when available.
            var sub = App.SipService?.CurrentSettings?.DecodedToken?.Sub;
            if (int.TryParse(sub, out var userId))
                request.AssignedToId = userId;
            else
                // Unassigned tasks used to leave no trace at all: assignedToId is dropped
                // from the payload when null, so the POST looked perfectly healthy.
                AppLogger.Log("CreateTask",
                    $"No assignee — JWT sub is not a user id (sub: {(sub == null ? "<absent>" : $"'{sub}'")}).");

            var callLogId = await callLogIdLookup;
            if (!string.IsNullOrWhiteSpace(callLogId))
                request.CallLogId = callLogId;

            var taskBtn = this.FindControl<Button>("TaskBtn");
            if (taskBtn != null) taskBtn.IsEnabled = false;

            // try/finally for the same reason CreateLeadAsync has one: the button is
            // disabled across an await, this runs fire-and-forget from the task window's
            // callback, and a throw would leave «Задача» dead for the rest of the call with
            // the exception disappearing into UnobservedTaskException.
            bool success;
            try
            {
                success = await App.TaskService.CreateTaskAsync(request);
                AppLogger.Log("CreateTask", $"Request success: {success}");
            }
            finally
            {
                if (taskBtn != null) taskBtn.IsEnabled = true;
            }

            if (success && taskBtn != null) await FlashTaskCreated(taskBtn);
        }

        private async Task ShowSmsComposeDialog()
        {
            var topLevel = TopLevel.GetTopLevel(this) as Window;
            if (topLevel == null || !IsSourceCallCurrent()) return;

            var displayNumber = this.FindControl<TextBlock>("CallerNumberLabel")?.Text?.Trim() ?? string.Empty;
            var smsBtn = this.FindControl<Button>("SmsBtn");
            if (!_smsComposeLaunchGuard.TryBegin(_callIdentity, displayNumber, out var snapshot, out var cancellationToken))
                return;

            if (smsBtn != null) smsBtn.IsEnabled = false;
            SetSmsComposeError(null);
            var shown = false;

            try
            {
                var primaryLinkedId = await App.ScriptService.GetPrimaryLinkedIdAsync(snapshot.DisplayNumber, cancellationToken);
                if (cancellationToken.IsCancellationRequested ||
                    !_smsComposeLaunchGuard.IsCurrent(snapshot) ||
                    !IsSourceCallCurrent())
                    return;

                if (!Models.ActiveCallSmsContext.TryCreate(primaryLinkedId, snapshot.DisplayNumber, out var context) || context is null)
                {
                    SetSmsComposeError(I18nService.Instance.Get("SmsActiveCallUnavailable"));
                    return;
                }

                var dialog = new SmsComposeDialog(context.Source, context.LockedDisplayNumber);
                _activeSmsDialog = dialog;
                dialog.Closed += (_, _) =>
                {
                    if (ReferenceEquals(_activeSmsDialog, dialog))
                        _activeSmsDialog = null;
                    ReleaseSmsComposeLaunch(snapshot, smsBtn);
                };
                dialog.Show(topLevel);
                shown = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Call/view invalidation is expected and must not show an error.
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested &&
                    _smsComposeLaunchGuard.IsCurrent(snapshot) &&
                    IsSourceCallCurrent())
                {
                    AppLogger.Log("ActiveCallSms", $"Failed to open SMS compose: {ex.GetType().Name}");
                    SetSmsComposeError(I18nService.Instance.Get("SmsActiveCallUnavailable"));
                }
            }
            finally
            {
                // Show returns as soon as the window is up, so the launch outlives this
                // method now. Only a launch that never put a window on screen ends here;
                // otherwise the Closed handler owns the teardown.
                if (!shown) ReleaseSmsComposeLaunch(snapshot, smsBtn);
            }
        }

        /// <summary>Frees the launch slot and gives the SMS button back, if this call still owns it.</summary>
        private void ReleaseSmsComposeLaunch(Models.ActiveCallSmsLaunchSnapshot snapshot, Button? smsBtn)
        {
            _smsComposeLaunchGuard.Complete(snapshot);
            if (smsBtn != null && IsSourceCallCurrent())
                smsBtn.IsEnabled = true;
        }

        private bool IsSourceCallCurrent()
        {
            var state = App.SipService.State;
            return (state == CallState.Active || state == CallState.OnHold) &&
                   string.Equals(App.SipService.ActiveCallerId, _callIdentity, StringComparison.Ordinal);
        }

        private void InvalidateSmsComposeForCallLifecycle()
        {
            _smsComposeLaunchGuard.Invalidate();
            var dialog = _activeSmsDialog;
            _activeSmsDialog = null;
            dialog?.Close(false);
        }

        private void SetSmsComposeError(string? message)
        {
            var errorLabel = this.FindControl<TextBlock>("SmsComposeErrorLabel");
            if (errorLabel == null) return;

            errorLabel.Text = message ?? string.Empty;
            errorLabel.IsVisible = !string.IsNullOrWhiteSpace(message);
        }

        /// <summary>Briefly swaps the task-button icon to a checkmark to confirm creation.</summary>
        private static Task FlashTaskCreated(Button taskBtn) => IconFlash.ConfirmAsync(taskBtn.Content);

        private void ShowScriptsDialog()
        {
            var topLevel = TopLevel.GetTopLevel(this) as Window;
            if (topLevel == null) return;

            // The number is read here rather than in the continuation: by the time the
            // operator picks a script the label may already belong to the next call.
            var number = this.FindControl<TextBlock>("CallerNumberLabel")?.Text?.Trim() ?? "";

            ScriptsWindowLauncher.Open(topLevel, selection => _ = RegisterScriptAsync(number, selection));
        }

        private static async Task RegisterScriptAsync(string number, Models.ScriptSelection selection)
        {
            var uniqueId = await App.ScriptService.GetChannelUniqueIdAsync(number);
            if (uniqueId != null)
                await App.ScriptService.RegisterAndMarkAsync(uniqueId, selection);
        }

        private void ToggleHold()
        {
            _onHold = !_onHold;
            UpdateHoldUI();
            UpdateDtmfPadEnabled();
            OnHoldToggled?.Invoke(this, _onHold);
        }

        /// <summary>Paints the hold button from <see cref="_onHold"/>.</summary>
        private void UpdateHoldUI()
        {
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

        // в”Ђв”Ђ Events в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
        // -- Public hotkey triggers
        public void TriggerMute()   => ToggleMute();
        public void TriggerHold()   => ToggleHold();
        public void TriggerHangup()
        {
            RetireTimer();
            InvalidateSmsComposeForCallLifecycle();
            OnHangup?.Invoke(this, System.EventArgs.Empty);
        }

        public event EventHandler?        OnHangup;
        public event EventHandler<bool>?  OnMuteToggled;      // arg = isMuted
        public event EventHandler<bool>?  OnHoldToggled;      // arg = isOnHold
        public event EventHandler<string>? OnTransferRequested; // arg = destination
    }
}
