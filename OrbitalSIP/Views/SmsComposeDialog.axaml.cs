using System;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using OrbitalSIP.Models;
using OrbitalSIP.Services;

namespace OrbitalSIP.Views;

public partial class SmsComposeDialog : Window
{
    private static readonly IBrush SelectedModeBrush = Brush.Parse("#1D4ED8");
    private static readonly IBrush UnselectedModeBrush = Brush.Parse("#1E293B");
    private static readonly IBrush NormalCountBrush = Brush.Parse("#64748B");
    private static readonly IBrush InvalidCountBrush = Brush.Parse("#F87171");

    private readonly SmsComposeState _state;
    private readonly SmsComposeSendSession _sendSession;
    private readonly SmsService _smsService;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly bool _loadTemplates;
    private bool _contentWasEdited;

    private TextBlock _recipientValue = null!;
    private Button _templateModeButton = null!;
    private Button _freeTextModeButton = null!;
    private StackPanel _templateArea = null!;
    private ComboBox _templateBox = null!;
    private TextBlock _templateStatusLabel = null!;
    private TextBox _contentBox = null!;
    private TextBlock _validationLabel = null!;
    private TextBlock _countLabel = null!;
    private TextBlock _errorLabel = null!;
    private Border _successBanner = null!;
    private Grid _composeFooter = null!;
    private StackPanel _confirmationFooter = null!;
    private Button _sendButton = null!;
    private Button _cancelButton = null!;
    private Button _backButton = null!;
    private Button _cancelSendButton = null!;
    private Button _confirmButton = null!;
    private TextBlock _confirmRecipientValue = null!;
    private TextBlock _confirmContentValue = null!;
    private TextBlock _progressLabel = null!;

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
        WireEvents();
        _recipientValue.Text = _state.Recipient;
        Render();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void FindControls()
    {
        _recipientValue = this.FindControl<TextBlock>("RecipientValue")!;
        _templateModeButton = this.FindControl<Button>("TemplateModeBtn")!;
        _freeTextModeButton = this.FindControl<Button>("FreeTextModeBtn")!;
        _templateArea = this.FindControl<StackPanel>("TemplateArea")!;
        _templateBox = this.FindControl<ComboBox>("TemplateBox")!;
        _templateStatusLabel = this.FindControl<TextBlock>("TemplateStatusLabel")!;
        _contentBox = this.FindControl<TextBox>("ContentBox")!;
        _validationLabel = this.FindControl<TextBlock>("ValidationLabel")!;
        _countLabel = this.FindControl<TextBlock>("CountLabel")!;
        _errorLabel = this.FindControl<TextBlock>("ErrorLabel")!;
        _successBanner = this.FindControl<Border>("SuccessBanner")!;
        _composeFooter = this.FindControl<Grid>("ComposeFooter")!;
        _confirmationFooter = this.FindControl<StackPanel>("ConfirmationFooter")!;
        _sendButton = this.FindControl<Button>("SendBtn")!;
        _cancelButton = this.FindControl<Button>("CancelBtn")!;
        _backButton = this.FindControl<Button>("BackBtn")!;
        _cancelSendButton = this.FindControl<Button>("CancelSendBtn")!;
        _confirmButton = this.FindControl<Button>("ConfirmBtn")!;
        _confirmRecipientValue = this.FindControl<TextBlock>("ConfirmRecipientValue")!;
        _confirmContentValue = this.FindControl<TextBlock>("ConfirmContentValue")!;
        _progressLabel = this.FindControl<TextBlock>("ProgressLabel")!;
    }

    private void WireEvents()
    {
        this.FindControl<Button>("CloseBtn")!.Click += (_, _) => CloseDialog();
        _cancelButton.Click += (_, _) => CancelOrClose();
        _templateModeButton.Click += (_, _) => SwitchMode(SmsComposeMode.Template);
        _freeTextModeButton.Click += (_, _) => SwitchMode(SmsComposeMode.FreeText);
        _templateBox.SelectionChanged += (_, _) => SelectTemplate();
        _contentBox.TextChanged += (_, _) => EditContent();
        _sendButton.Click += (_, _) => ShowConfirmation();
        _backButton.Click += (_, _) => HideConfirmation();
        _cancelSendButton.Click += (_, _) => CancelActiveSend();
        _confirmButton.Click += async (_, _) => await SendConfirmedAsync();

        this.EnableDrag(this.FindControl<Border>("HeaderBar"));
        KeyDown += OnDialogKeyDown;
        Opened += OnOpened;
        Closed += (_, _) =>
        {
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

        if (_state.Mode == SmsComposeMode.Template && _templateBox.IsEnabled)
            _templateBox.Focus();
        else
            _contentBox.Focus();
    }

    private async Task LoadTemplatesAsync()
    {
        SetTemplateStatus("SmsTemplatesLoading");
        _templateBox.IsEnabled = false;

        try
        {
            var templates = await _smsService.GetTemplatesAsync(_lifetimeCancellation.Token);
            foreach (var template in templates)
            {
                _templateBox.Items.Add(new ComboBoxItem
                {
                    Content = template.Name,
                    Tag = template,
                });
            }

            if (templates.Count == 0)
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
                _templateBox.IsEnabled = !_state.IsInFlight && !_state.IsQueued;
        }
    }

    private void SwitchMode(SmsComposeMode mode)
    {
        if (_state.IsInFlight || _state.IsQueued)
            return;

        _state.SwitchMode(mode);
        ClearTransientMessages();
        Render();
        if (mode == SmsComposeMode.Template)
            _templateBox.Focus();
        else
            _contentBox.Focus();
    }

    private void SelectTemplate()
    {
        if (_state.IsInFlight || _state.IsQueued ||
            _templateBox.SelectedItem is not ComboBoxItem { Tag: MessageTemplateDto template })
            return;

        _state.SelectTemplate(template);
        _contentBox.Text = _state.Content;
        _contentBox.CaretIndex = _state.Content.Length;
        _contentWasEdited = false;
        ClearTransientMessages();
        Render();
        _contentBox.Focus();
    }

    private void EditContent()
    {
        if (_state.IsInFlight || _state.IsQueued)
            return;

        _state.EditContent(_contentBox.Text);
        _contentWasEdited = true;
        ClearTransientMessages();
        Render();
    }

    private void ShowConfirmation()
    {
        if (!_state.RequestConfirmation())
        {
            _contentWasEdited = true;
            Render();
            return;
        }

        ClearTransientMessages();
        Render();
        _confirmButton.Focus();
    }

    private void HideConfirmation()
    {
        _state.CancelConfirmation();
        Render();
        _contentBox.Focus();
    }

    private async Task SendConfirmedAsync()
    {
        if (!_sendSession.TryBeginSend(out var attempt) || attempt is null)
            return;

        ClearTransientMessages();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            attempt.CancellationToken,
            _lifetimeCancellation.Token);
        Render();
        _cancelSendButton.Focus();

        try
        {
            await _smsService.SendFromCallAsync(attempt.Request, cancellation.Token);
            _sendSession.CompleteSuccess(attempt);
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
        _confirmButton.Focus();
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
            if (_state.IsConfirmationVisible)
                _ = SendConfirmedAsync();
            else
                ShowConfirmation();
        }
    }

    private void Render()
    {
        var editable = !_state.IsInFlight && !_state.IsQueued;
        var templateMode = _state.Mode == SmsComposeMode.Template;

        _templateModeButton.Background = templateMode ? SelectedModeBrush : UnselectedModeBrush;
        _freeTextModeButton.Background = templateMode ? UnselectedModeBrush : SelectedModeBrush;
        _templateArea.IsVisible = templateMode;
        _templateModeButton.IsEnabled = editable;
        _freeTextModeButton.IsEnabled = editable;
        _templateBox.IsEnabled = editable && _loadTemplates;
        _contentBox.IsEnabled = editable;
        _sendButton.IsEnabled = _state.CanSend;

        _composeFooter.IsVisible = !_state.IsConfirmationVisible && !_state.IsQueued;
        _confirmationFooter.IsVisible = _state.IsConfirmationVisible && !_state.IsQueued;
        _backButton.IsVisible = !_state.IsInFlight;
        _backButton.IsEnabled = !_state.IsInFlight;
        _cancelSendButton.IsVisible = _sendSession.CanCancelSend;
        _cancelSendButton.IsEnabled = _sendSession.CanCancelSend;
        _confirmButton.IsEnabled = !_state.IsInFlight;
        _progressLabel.IsVisible = _state.IsInFlight;

        _confirmRecipientValue.Text = _state.Recipient;
        _confirmContentValue.Text = _state.Content;
        _successBanner.IsVisible = _state.IsQueued;

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
            SmsComposeValidation.TemplateRequired => "SmsTemplateRequired",
            _ => null,
        };

        var show = key is not null &&
                   (_contentWasEdited || _state.Validation == SmsComposeValidation.ContentTooLong);
        _validationLabel.IsVisible = show;
        _validationLabel.Text = show ? I18nService.Instance.Get(key!) : string.Empty;
    }

    private void ClearTransientMessages()
    {
        _errorLabel.IsVisible = false;
        _errorLabel.Text = string.Empty;
    }

    private void ShowError(string message)
    {
        _errorLabel.Text = message;
        _errorLabel.IsVisible = true;
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
