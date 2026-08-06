using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Material.Icons;
using Material.Icons.Avalonia;
using OrbitalSIP.Models;
using OrbitalSIP.Services;

namespace OrbitalSIP.Views;

public partial class SmsComposeDialog : Window
{
    private static readonly TimeSpan SuccessCloseDelay = TimeSpan.FromMilliseconds(1500);
    private static readonly IBrush NormalCountBrush = Brush.Parse("#64748B");
    private static readonly IBrush InvalidCountBrush = Brush.Parse("#F87171");
    private static readonly IBrush NeutralBorderBrush = Brush.Parse("#334155");
    private static readonly IBrush NeutralForegroundBrush = Brush.Parse("#94A3B8");
    private static readonly IBrush CancelSendBorderBrush = Brush.Parse("#7F1D1D");
    private static readonly IBrush CancelSendForegroundBrush = Brush.Parse("#FCA5A5");
    private static readonly IBrush SendEnabledBrush = Brush.Parse("#3B82F6");
    private static readonly IBrush SendDisabledBrush = Brush.Parse("#1E3A6B");
    private static readonly IBrush SendEnabledForegroundBrush = Brush.Parse("#FFFFFF");
    private static readonly IBrush SendDisabledForegroundBrush = Brush.Parse("#7796C4");

    private readonly SmsComposeState _state;
    private readonly SmsComposeSendSession _sendSession;
    private readonly SmsService _smsService;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly bool _loadTemplates;
    private DispatcherTimer? _successCloseTimer;
    private bool _contentWasEdited;
    private bool _suppressContentChanged;
    private bool _lastSendFailed;

    // Template-picker commit tracking. Avalonia's AutoCompleteBox forwards arrow-key
    // navigation through the same SelectionChanged event as a mouse pick or Enter
    // commit, and closes its dropdown (with a refocus of its own internal search box)
    // on Escape exactly the same way it does on a commit. These three fields let us
    // tell an actual commit apart from browsing/cancelling — see ConfigureTemplateBox,
    // OnTemplateBoxPreviewKeyDown, OnTemplateDropDownClosed and OnTemplateBoxGotFocus.
    private MessageTemplateDto? _pendingTemplateSelection;
    private bool _suppressTemplateAutoOpen;
    private bool _templateDropDownClosedViaEscape;

    private TextBlock _recipientValue = null!;
    private AutoCompleteBox _templateBox = null!;
    private Button _clearTemplateButton = null!;
    private TextBlock _templateStatusLabel = null!;
    private TextBox _contentBox = null!;
    private TextBlock _validationLabel = null!;
    private TextBlock _countLabel = null!;
    private Border _errorBanner = null!;
    private TextBlock _errorLabel = null!;
    private Border _successBanner = null!;
    private Grid _composeFooter = null!;
    private Button _sendButton = null!;
    private MaterialIcon _sendIcon = null!;
    private TextBlock _sendLabel = null!;
    private Button _cancelButton = null!;

    public SmsComposeDialog()
        : this(new SmsCallSource("active", "design-time"), "—", App.SmsService, loadTemplates: false)
    {
    }

    public SmsComposeDialog(SmsCallSource source, string lockedRecipient)
        : this(source, lockedRecipient, App.SmsService, loadTemplates: true)
    {
    }

    internal SmsComposeDialog(
        SmsCallSource source,
        string lockedRecipient,
        SmsService smsService,
        bool loadTemplates)
    {
        _state = new SmsComposeState(source, lockedRecipient);
        _sendSession = new SmsComposeSendSession(_state);
        _smsService = smsService ?? throw new ArgumentNullException(nameof(smsService));
        _loadTemplates = loadTemplates;

        InitializeComponent();
        FindControls();
        ConfigureTemplateBox();
        WireEvents();
        _recipientValue.Text = SmsRecipientFormatter.Format(_state.Recipient);
        Render();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void FindControls()
    {
        _recipientValue = this.FindControl<TextBlock>("RecipientValue")!;
        _templateBox = this.FindControl<AutoCompleteBox>("TemplateBox")!;
        _clearTemplateButton = this.FindControl<Button>("ClearTemplateBtn")!;
        _templateStatusLabel = this.FindControl<TextBlock>("TemplateStatusLabel")!;
        _contentBox = this.FindControl<TextBox>("ContentBox")!;
        _validationLabel = this.FindControl<TextBlock>("ValidationLabel")!;
        _countLabel = this.FindControl<TextBlock>("CountLabel")!;
        _errorBanner = this.FindControl<Border>("ErrorBanner")!;
        _errorLabel = this.FindControl<TextBlock>("ErrorLabel")!;
        _successBanner = this.FindControl<Border>("SuccessBanner")!;
        _composeFooter = this.FindControl<Grid>("ComposeFooter")!;
        _sendButton = this.FindControl<Button>("SendBtn")!;
        _sendIcon = this.FindControl<MaterialIcon>("SendIcon")!;
        _sendLabel = this.FindControl<TextBlock>("SendLabel")!;
        _cancelButton = this.FindControl<Button>("CancelBtn")!;
    }

    private void ConfigureTemplateBox()
    {
        // Name is what lands in the text box after a pick; the filter below is what
        // decides visibility, and it deliberately looks at the body too.
        _templateBox.ValueMemberBinding = new Binding("Name");
        _templateBox.FilterMode = AutoCompleteFilterMode.Custom;
        _templateBox.ItemFilter = (search, item) =>
        {
            if (item is not MessageTemplateDto template)
                return false;
            if (string.IsNullOrWhiteSpace(search))
                return true;

            return template.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                   (template.Content?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);
        };
    }

    private void WireEvents()
    {
        this.FindControl<Button>("CloseBtn")!.Click += (_, _) => CloseDialog();
        _cancelButton.Click += (_, _) => CancelOrClose();
        _clearTemplateButton.Click += (_, _) => ClearTemplate();
        // SelectionChanged fires on every ListBox.SelectedIndex mutation, including
        // plain arrow-key browsing — it is not a commit signal. Stage the candidate
        // here; ApplyPendingTemplateSelection decides whether it actually lands.
        _templateBox.SelectionChanged += (_, _) =>
            _pendingTemplateSelection = _templateBox.SelectedItem as MessageTemplateDto;
        _templateBox.DropDownClosed += (_, _) => OnTemplateDropDownClosed();
        // Escape closes the dropdown through the same internal path a commit uses
        // (both end with Avalonia refocusing its own search TextBox), so
        // DropDownClosed alone can't tell them apart. Catch Escape in the tunnel
        // phase, before AutoCompleteBox's own OnKeyDown consumes it.
        _templateBox.AddHandler(InputElement.KeyDownEvent, OnTemplateBoxPreviewKeyDown, RoutingStrategies.Tunnel);
        // An empty prefix must still show the whole list, otherwise the control
        // reads as a dead text box to anyone used to the old combo box. The one
        // exception is the GotFocus Avalonia raises on itself right after a commit —
        // see OnTemplateBoxGotFocus.
        _templateBox.GotFocus += (_, _) => OnTemplateBoxGotFocus();
        _contentBox.TextChanged += (_, _) => EditContent();
        _sendButton.Click += async (_, _) => await SendAsync();

        this.EnableDrag(this.FindControl<Border>("HeaderBar"));
        KeyDown += OnDialogKeyDown;
        Opened += OnOpened;
        Closed += (_, _) =>
        {
            StopSuccessClose();
            _sendSession.Dispose();
            _lifetimeCancellation.Cancel();
            _lifetimeCancellation.Dispose();
        };
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        // CenterOwner positions this window off the softphone widget, which operators
        // park against a screen edge. With SystemDecorations="None" the header bar is
        // the only drag handle, so a header pushed off-screen leaves the window
        // unreachable — pull it back inside the working area before anything else.
        this.KeepOnScreen();

        if (_loadTemplates)
            await LoadTemplatesAsync();

        _contentBox.Focus();
    }

    private async Task LoadTemplatesAsync()
    {
        SetTemplateStatus("SmsTemplatesLoading");
        _templateBox.IsEnabled = false;

        try
        {
            var templates = await _smsService.GetTemplatesAsync(_lifetimeCancellation.Token);
            // A template with no body cannot be composed from; hiding it beats
            // offering a pick that throws in SelectTemplate.
            var usable = templates
                .Where(template => !string.IsNullOrWhiteSpace(template.Content))
                .ToList();
            _templateBox.ItemsSource = usable;

            if (usable.Count == 0)
                SetTemplateStatus("SmsTemplatesEmpty");
            else
                HideTemplateStatus();
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }
        catch (Exception)
        {
            SetTemplateStatus("SmsTemplatesLoadError", isError: true);
        }
        finally
        {
            if (!_lifetimeCancellation.IsCancellationRequested)
                Render();
        }
    }

    /// <summary>
    /// Tunnel-phase KeyDown on the template box. Runs before AutoCompleteBox's own
    /// (bubble-phase) OnKeyDown, so it can flag a dropdown-closing Escape before the
    /// framework's internal Commit/Cancel handling — which we cannot see directly —
    /// has a chance to run.
    /// </summary>
    private void OnTemplateBoxPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _templateBox.IsDropDownOpen)
            _templateDropDownClosedViaEscape = true;
    }

    /// <summary>
    /// Fires whenever the template dropdown closes: a mouse pick, Enter, Escape, the
    /// operator focusing something else, or the filtered list emptying out. Only the
    /// first two are a commit; this method tells them apart from the rest and arms
    /// OnTemplateBoxGotFocus for the refocus that follows a genuine commit.
    /// </summary>
    private void OnTemplateDropDownClosed()
    {
        var closedViaEscape = _templateDropDownClosedViaEscape;
        _templateDropDownClosedViaEscape = false;

        if (closedViaEscape)
        {
            // Cancel. Whatever SelectedItem Avalonia's own cancel handling leaves
            // behind (it can restore an exact text match) must never be applied.
            _pendingTemplateSelection = null;
            return;
        }

        // A commit (mouse pick or Enter) closes the dropdown and then, synchronously,
        // refocuses the internal search TextBox (Avalonia's own
        // OnAdapterSelectionComplete). Arm a one-shot flag for the GotFocus that
        // follows. Losing focus entirely, or an empty-filter auto-close, also raise
        // DropDownClosed but do not refocus the search box — nothing will consume
        // this flag in those cases, so disarm it on the next UI-thread turn rather
        // than leave it to misfire on some unrelated later focus.
        _suppressTemplateAutoOpen = true;
        Dispatcher.UIThread.Post(() => _suppressTemplateAutoOpen = false);
    }

    /// <summary>
    /// Normally reopens the dropdown so an empty prefix still shows the whole list.
    /// The one exception is the GotFocus Avalonia raises on its own search TextBox
    /// right after a commit closes the dropdown — reopening there is bug 1 from the
    /// review (mouse pick ends with the dropdown back open); applying the pending
    /// template here instead is what actually completes the commit.
    /// </summary>
    private void OnTemplateBoxGotFocus()
    {
        if (_suppressTemplateAutoOpen)
        {
            _suppressTemplateAutoOpen = false;
            ApplyPendingTemplateSelection();
            return;
        }

        if (_templateBox.IsEnabled)
            _templateBox.IsDropDownOpen = true;
    }

    private void ApplyPendingTemplateSelection()
    {
        var template = _pendingTemplateSelection;
        _pendingTemplateSelection = null;
        if (_state.IsInFlight || _state.IsQueued ||
            template is null || string.IsNullOrWhiteSpace(template.Content))
            return;

        _state.SelectTemplate(template);
        SetContentBoxText(_state.Content);
        ClearTransientMessages();
        _contentWasEdited = false;
        Render();
        // Avalonia's OnAdapterSelectionComplete calls TextBox!.Focus() on its own
        // search box right after raising the GotFocus we're handling here, which
        // would otherwise immediately steal focus back from the message box. Posting
        // lets that framework call finish first, so ours is the one that sticks.
        Dispatcher.UIThread.Post(() => _contentBox.Focus());
    }

    private void ClearTemplate()
    {
        if (_state.IsInFlight || _state.IsQueued)
            return;

        _state.ClearTemplate();
        _pendingTemplateSelection = null;
        _templateBox.SelectedItem = null;
        _templateBox.Text = string.Empty;
        ClearTransientMessages();
        Render();
        _contentBox.Focus();
    }

    private void EditContent()
    {
        if (_suppressContentChanged || _state.IsInFlight || _state.IsQueued)
            return;

        _state.EditContent(_contentBox.Text);
        _contentWasEdited = true;
        ClearTransientMessages();
        Render();
    }

    /// <summary>Writes text the operator did not type, without counting it as an edit.</summary>
    private void SetContentBoxText(string text)
    {
        _suppressContentChanged = true;
        try
        {
            _contentBox.Text = text;
            _contentBox.CaretIndex = text.Length;
        }
        finally
        {
            _suppressContentChanged = false;
        }
    }

    private async Task SendAsync()
    {
        if (!_state.CanSend)
        {
            _contentWasEdited = true;
            Render();
            return;
        }

        if (!_sendSession.TryBeginSend(out var attempt) || attempt is null)
            return;

        ClearTransientMessages();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            attempt.CancellationToken,
            _lifetimeCancellation.Token);
        Render();
        _cancelButton.Focus();

        try
        {
            await _smsService.SendFromCallAsync(attempt.Request, cancellation.Token);
            if (_sendSession.CompleteSuccess(attempt) && !_lifetimeCancellation.IsCancellationRequested)
                StartSuccessClose();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!_lifetimeCancellation.IsCancellationRequested &&
                _sendSession.CompleteFailure(attempt, SmsComposeSendSession.CancelledMessageKey))
                ShowError(I18nService.Instance.Get("SmsCancelled"));
        }
        catch (SmsApiException ex)
        {
            if (_sendSession.CompleteFailure(attempt))
                ShowError(ex.ApiMessage);
        }
        catch (HttpRequestException)
        {
            if (_sendSession.CompleteFailure(attempt))
                ShowError(I18nService.Instance.Get("SmsSendError"));
        }
        catch (InvalidOperationException)
        {
            if (_sendSession.CompleteFailure(attempt))
                ShowError(I18nService.Instance.Get("SmsSendError"));
        }
        catch (Exception)
        {
            if (_sendSession.CompleteFailure(attempt))
                ShowError(I18nService.Instance.Get("SmsSendError"));
        }
        finally
        {
            if (!_lifetimeCancellation.IsCancellationRequested)
                Render();
        }
    }

    private void StartSuccessClose()
    {
        StopSuccessClose();
        _successCloseTimer = new DispatcherTimer { Interval = SuccessCloseDelay };
        _successCloseTimer.Tick += (_, _) =>
        {
            StopSuccessClose();
            Close(true);
        };
        _successCloseTimer.Start();
    }

    private void StopSuccessClose()
    {
        _successCloseTimer?.Stop();
        _successCloseTimer = null;
    }

    private void CancelOrClose()
    {
        if (_sendSession.CanCancelSend)
        {
            CancelActiveSend();
            return;
        }

        Close(_state.IsQueued);
    }

    private void CloseDialog()
    {
        if (_sendSession.CanCancelSend)
        {
            CancelActiveSend();
            return;
        }

        Close(_state.IsQueued);
    }

    private void CancelActiveSend()
    {
        if (!_sendSession.CancelCurrentSend())
            return;

        var key = _sendSession.StatusMessageKey ?? SmsComposeSendSession.CancelledMessageKey;
        ShowError(I18nService.Instance.Get(key));
        Render();
        _sendButton.Focus();
    }

    private void OnDialogKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseDialog();
            return;
        }

        if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            _ = SendAsync();
        }
    }

    private void Render()
    {
        var editable = !_state.IsInFlight && !_state.IsQueued;
        var canCancelSend = _sendSession.CanCancelSend;

        _templateBox.IsEnabled = editable && _loadTemplates;
        _clearTemplateButton.IsVisible = _state.SelectedTemplate is not null;
        _clearTemplateButton.IsEnabled = editable;
        _contentBox.IsEnabled = editable;

        _composeFooter.IsVisible = !_state.IsQueued;
        _successBanner.IsVisible = _state.IsQueued;

        _cancelButton.Content = I18nService.Instance.Get(canCancelSend ? "SmsCancelSend" : "Cancel");
        _cancelButton.BorderBrush = canCancelSend ? CancelSendBorderBrush : NeutralBorderBrush;
        _cancelButton.Foreground = canCancelSend ? CancelSendForegroundBrush : NeutralForegroundBrush;

        _sendButton.IsEnabled = _state.CanSend;
        _sendButton.Background = _state.CanSend ? SendEnabledBrush : SendDisabledBrush;
        var sendForeground = _state.CanSend ? SendEnabledForegroundBrush : SendDisabledForegroundBrush;
        _sendLabel.Foreground = sendForeground;
        _sendIcon.Foreground = sendForeground;

        if (_state.IsInFlight)
        {
            _sendLabel.Text = I18nService.Instance.Get("SmsSendingShort");
            _sendIcon.Kind = MaterialIconKind.ClockOutline;
        }
        else if (_lastSendFailed)
        {
            _sendLabel.Text = I18nService.Instance.Get("SmsRetry");
            _sendIcon.Kind = MaterialIconKind.Refresh;
        }
        else
        {
            _sendLabel.Text = I18nService.Instance.Get("SmsSend");
            _sendIcon.Kind = MaterialIconKind.Send;
        }

        _countLabel.Text = string.Format(
            CultureInfo.CurrentCulture,
            I18nService.Instance.Get("SmsCharacterCount"),
            _state.CharacterCount,
            SmsComposeState.MaxContentLength);
        _countLabel.Foreground = _state.CharacterCount > SmsComposeState.MaxContentLength
            ? InvalidCountBrush
            : NormalCountBrush;

        RenderValidation();
    }

    private void RenderValidation()
    {
        var key = _state.Validation switch
        {
            SmsComposeValidation.ContentRequired => "SmsContentRequired",
            SmsComposeValidation.ContentTooLong => "SmsContentTooLong",
            _ => null,
        };

        var show = key is not null &&
                   (_contentWasEdited || _state.Validation == SmsComposeValidation.ContentTooLong);
        _validationLabel.IsVisible = show;
        _validationLabel.Text = show ? I18nService.Instance.Get(key!) : string.Empty;
    }

    private void ClearTransientMessages()
    {
        _lastSendFailed = false;
        _errorBanner.IsVisible = false;
        _errorLabel.Text = string.Empty;
    }

    private void ShowError(string message)
    {
        _lastSendFailed = true;
        _errorLabel.Text = message;
        _errorBanner.IsVisible = true;
    }

    private void SetTemplateStatus(string key, bool isError = false)
    {
        _templateStatusLabel.Text = I18nService.Instance.Get(key);
        _templateStatusLabel.Foreground = isError ? InvalidCountBrush : NormalCountBrush;
        _templateStatusLabel.IsVisible = true;
    }

    private void HideTemplateStatus()
    {
        _templateStatusLabel.Text = string.Empty;
        _templateStatusLabel.IsVisible = false;
    }
}
