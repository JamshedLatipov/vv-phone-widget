using System;
using System.Diagnostics;
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
        private TimeSpan _elapsed = TimeSpan.Zero;
        public TimeSpan Elapsed => _elapsed;
        private bool _muted;
        private bool _onHold;
        private bool _leadCreated;
        private bool _surveyOpen;
        private bool _taskOpen;

        /// <summary>Null until the first call-context lookup returns — which is
        /// LeadPanelState.Loading, deliberately distinct from «no lead».</summary>
        private Models.LeadCallContextResult? _leadContext;
        private bool _leadContextLoading;

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
            _ = LoadLeadContextAsync();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        // в”Ђв”Ђ Timer в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
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

        // в”Ђв”Ђ Buttons в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
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

            var surveyBtn = this.FindControl<Button>("SurveyBtn");
            if (surveyBtn != null)
                surveyBtn.Click += async (_, __) => await ShowSurveyDialog();

            var leadBtn = this.FindControl<Button>("CreateLeadBtn");
            if (leadBtn != null)
                leadBtn.Click += async (_, __) => await CreateLeadAsync();

            var taskBtn = this.FindControl<Button>("TaskBtn");
            if (taskBtn != null)
                taskBtn.Click += async (_, __) => await ShowTaskDialog();

            var leadRetryBtn = this.FindControl<Button>("LeadRetryBtn");
            if (leadRetryBtn != null)
                leadRetryBtn.Click += async (_, __) => await LoadLeadContextAsync();

            var leadOpenBtn = this.FindControl<Button>("LeadOpenBtn");
            if (leadOpenBtn != null)
                leadOpenBtn.Click += (_, __) => OpenLeadInCrm();

            var leadTransferOwnerBtn = this.FindControl<Button>("LeadTransferOwnerBtn");
            if (leadTransferOwnerBtn != null)
                leadTransferOwnerBtn.Click += (_, __) => TransferToLeadOwner();

            var leadCommentAddBtn = this.FindControl<Button>("LeadCommentAddBtn");
            if (leadCommentAddBtn != null)
                leadCommentAddBtn.Click += async (_, __) => await AddLeadCommentAsync();

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
                RenderConflictLeadCard(result);
                _ = LoadLeadContextAsync(showLoading: false);
                return;
            }

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

            ApplyLeadPanelState();
        }

        private void ApplyLeadPanelState()
        {
            var state = LeadCallPanelPresenter.SelectState(_leadContext);
            var context = _leadContext?.Context;

            var panel = this.FindControl<Border>("LeadPanel");
            var loading = this.FindControl<TextBlock>("LeadLoadingLabel");
            var unavailable = this.FindControl<StackPanel>("LeadUnavailableBlock");
            var card = this.FindControl<StackPanel>("LeadCardBlock");
            var createBtn = this.FindControl<Button>("CreateLeadBtn");

            if (createBtn != null && !_leadCreated)
                createBtn.IsVisible = LeadCallPanelPresenter.ShowsCreateButton(state);

            if (loading != null) loading.IsVisible = state == LeadPanelState.Loading;
            if (unavailable != null) unavailable.IsVisible = state == LeadPanelState.Unavailable;
            if (card != null) card.IsVisible = LeadCallPanelPresenter.ShowsLeadCard(state);

            // Nothing to say in OfferCreate (the button speaks) or Hidden.
            if (panel != null)
                panel.IsVisible = state == LeadPanelState.Loading
                                  || state == LeadPanelState.Unavailable
                                  || LeadCallPanelPresenter.ShowsLeadCard(state);

            if (LeadCallPanelPresenter.ShowsLeadCard(state) && context?.Lead != null)
                RenderLeadCard(context);
        }

        private void RenderLeadCard(Models.LeadCallContext context)
        {
            var lead = context.Lead!;
            var i18n = Services.I18nService.Instance;

            var headline = this.FindControl<TextBlock>("LeadHeadlineLabel");
            if (headline != null) headline.Text = LeadCallPanelPresenter.LeadHeadline(lead);

            var subline = this.FindControl<TextBlock>("LeadSublineLabel");
            if (subline != null) subline.Text = LeadCallPanelPresenter.LeadSubline(lead);

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
            SetVisible<Button>("CreateLeadBtn", false);
            SetVisible<Border>("LeadPanel", true);
            SetVisible<StackPanel>("LeadCardBlock", true);
            SetVisible<TextBlock>("LeadLoadingLabel", false);
            SetVisible<StackPanel>("LeadUnavailableBlock", false);

            var headline = this.FindControl<TextBlock>("LeadHeadlineLabel");
            if (headline != null)
                headline.Text = conflict.ExistingLeadId.HasValue
                    ? $"#{conflict.ExistingLeadId} · {conflict.ExistingLeadName}".Trim()
                    : conflict.Message ?? string.Empty;

            var subline = this.FindControl<TextBlock>("LeadSublineLabel");
            if (subline != null) subline.Text = conflict.ExistingLeadStatus ?? string.Empty;

            var ownerLabel = this.FindControl<TextBlock>("LeadOwnerLabel");
            if (ownerLabel != null) ownerLabel.Text = string.Empty;

            SetVisible<Button>("LeadOpenBtn", false);
            SetVisible<Button>("LeadTransferOwnerBtn", false);
            SetVisible<TextBlock>("LeadTransferBlockedLabel", false);
            SetVisible<StackPanel>("LeadCommentBlock", false);
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
                    : LeadCallPanelPresenter.TransferBlockedKey(
                        context.Actions.TransferBlockedReason
                        ?? LeadCallPanelPresenter.TransferBlockedUnknownKey);

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
                await AttachCallLinkAsync(payload, CallerNumber());

                var ok = await App.LeadService.AddCallCommentAsync(lead.Id, payload);
                AppLogger.Log("LeadPanel", $"Add call comment to lead {lead.Id}: {ok}");

                if (ok && box != null) box.Text = string.Empty;
                _ = ShowCommentStatusAsync(ok ? "LeadPanelCommentSaved" : "LeadPanelCommentFailed");
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
        private static async Task AttachCallLinkAsync(Models.AddCallCommentRequest payload, string? number)
        {
            if (string.IsNullOrWhiteSpace(number)) return;

            var uniqueId = await App.ScriptService.GetChannelUniqueIdAsync(number!);
            if (string.IsNullOrWhiteSpace(uniqueId)) return;

            var callLogId = await App.ScriptService.SaveCallLogAsync(uniqueId!);
            if (!string.IsNullOrWhiteSpace(callLogId))
                payload.CallLogId = callLogId;
            else
                payload.CallUniqueId = uniqueId;
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

        private async Task ShowSurveyDialog()
        {
            if (_surveyOpen) return;
            var topLevel = TopLevel.GetTopLevel(this) as Window;
            if (topLevel == null) return;

            var callerNumber = this.FindControl<TextBlock>("CallerNumberLabel")?.Text?.Trim() ?? "";
            var dialog = new SurveyDialog(callerNumber);
            _surveyOpen = true;
            try
            {
                await dialog.ShowDialog(topLevel);
            }
            finally
            {
                _surveyOpen = false;
            }
        }

        private async Task ShowTaskDialog()
        {
            if (_taskOpen) return;
            var topLevel = TopLevel.GetTopLevel(this) as Window;
            if (topLevel == null) return;

            var callerNumber = this.FindControl<TextBlock>("CallerNumberLabel")?.Text?.Trim() ?? "";

            _taskOpen = true;
            Models.CreateTaskRequest? request;
            try
            {
                var dialog = new TaskDialog(callerNumber);
                request = await dialog.ShowDialog<Models.CreateTaskRequest?>(topLevel);
            }
            finally
            {
                _taskOpen = false;
            }

            if (request == null) return;

            // Assign to the current operator (numeric user id from the JWT), when available.
            var sub = App.SipService?.CurrentSettings?.DecodedToken?.Sub;
            if (int.TryParse(sub, out var userId))
                request.AssignedToId = userId;

            // Link the task to this call via its CallLog row id — the field the CRM reads
            // to show the linked call. Resolve the call's Asterisk uniqueId, then ensure a
            // CallLog row exists and grab its id (POST /api/cdr/log upserts by uniqueId).
            if (!string.IsNullOrWhiteSpace(callerNumber))
            {
                var uniqueId = await App.ScriptService.GetChannelUniqueIdAsync(callerNumber);
                if (!string.IsNullOrWhiteSpace(uniqueId))
                {
                    var callLogId = await App.ScriptService.SaveCallLogAsync(uniqueId);
                    if (!string.IsNullOrWhiteSpace(callLogId))
                        request.CallLogId = callLogId;
                }
            }

            var taskBtn = this.FindControl<Button>("TaskBtn");
            if (taskBtn != null) taskBtn.IsEnabled = false;

            bool success = await App.TaskService.CreateTaskAsync(request);
            AppLogger.Log("CreateTask", $"Request success: {success}");

            if (taskBtn != null)
            {
                taskBtn.IsEnabled = true;
                if (success) await FlashTaskCreated(taskBtn);
            }
        }

        /// <summary>Briefly swaps the task-button icon to a checkmark to confirm creation.</summary>
        private static async Task FlashTaskCreated(Button taskBtn)
        {
            if (taskBtn.Content is StackPanel panel)
            {
                foreach (var child in panel.Children)
                {
                    if (child is MaterialIcon icon)
                    {
                        var original = icon.Kind;
                        icon.Kind = MaterialIconKind.Check;
                        await Task.Delay(1200);
                        icon.Kind = original;
                        break;
                    }
                }
            }
        }

        private async Task ShowScriptsDialog()
        {
            var topLevel = TopLevel.GetTopLevel(this) as Window;
            if (topLevel == null) return;

            var dialog = new ScriptsDialog();
            var result = await dialog.ShowDialog<Models.ScriptSelection?>(topLevel);

            if (result != null)
            {
                var callerLabel = this.FindControl<TextBlock>("CallerNumberLabel");
                var number = callerLabel?.Text?.Trim() ?? "";
                var uniqueId = await App.ScriptService.GetChannelUniqueIdAsync(number);
                if (uniqueId != null)
                    await App.ScriptService.RegisterAndMarkAsync(uniqueId, result);
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

        // в”Ђв”Ђ Events в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
        // -- Public hotkey triggers
        public void TriggerMute()   => ToggleMute();
        public void TriggerHold()   => ToggleHold();
        public void TriggerHangup() { _timer?.Stop(); OnHangup?.Invoke(this, System.EventArgs.Empty); }

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
