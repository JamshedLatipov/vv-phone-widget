using System;

namespace OrbitalSIP.Models;

public enum SmsComposeMode
{
    Template,
    FreeText,
}

public enum SmsComposeValidation
{
    None,
    ContentRequired,
    ContentTooLong,
    TemplateRequired,
}

/// <summary>
/// UI-independent state for composing one SMS from an immutable call anchor.
/// The display recipient never participates in API request construction.
/// </summary>
public sealed class SmsComposeState
{
    public const int MaxContentLength = 1000;

    private Guid? _requestId;

    public SmsComposeState(SmsCallSource source, string recipient)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Recipient = string.IsNullOrWhiteSpace(recipient)
            ? throw new ArgumentException("A display recipient is required.", nameof(recipient))
            : recipient;
    }

    public SmsCallSource Source { get; }
    public string Recipient { get; }
    public SmsComposeMode Mode { get; private set; } = SmsComposeMode.Template;
    public MessageTemplateDto? SelectedTemplate { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public int CharacterCount => Content.Length;
    public bool IsConfirmationVisible { get; private set; }
    public bool IsInFlight { get; private set; }
    public bool IsQueued { get; private set; }

    public SmsComposeValidation Validation
    {
        get
        {
            if (Mode == SmsComposeMode.Template && SelectedTemplate is null)
                return SmsComposeValidation.TemplateRequired;
            if (string.IsNullOrWhiteSpace(Content))
                return SmsComposeValidation.ContentRequired;
            if (CharacterCount > MaxContentLength)
                return SmsComposeValidation.ContentTooLong;
            return SmsComposeValidation.None;
        }
    }

    public bool CanSend => !IsInFlight && !IsQueued && Validation == SmsComposeValidation.None;

    public void SwitchMode(SmsComposeMode mode)
    {
        EnsureEditable();
        if (Mode == mode)
            return;

        Mode = mode;
        InvalidatePendingRequest();
    }

    public void SelectTemplate(MessageTemplateDto template)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (string.IsNullOrWhiteSpace(template.Content))
            throw new ArgumentException("A compose-ready template must contain text.", nameof(template));

        EnsureEditable();
        var changed = Mode != SmsComposeMode.Template ||
                      SelectedTemplate?.Id != template.Id ||
                      !string.Equals(Content, template.Content, StringComparison.Ordinal);
        Mode = SmsComposeMode.Template;
        SelectedTemplate = template;
        Content = template.Content;
        if (changed)
            InvalidatePendingRequest();
    }

    public void EditContent(string? content)
    {
        EnsureEditable();
        content ??= string.Empty;
        if (string.Equals(Content, content, StringComparison.Ordinal))
            return;

        Content = content;
        InvalidatePendingRequest();
    }

    public bool RequestConfirmation()
    {
        if (!CanSend)
            return false;

        IsConfirmationVisible = true;
        return true;
    }

    public void CancelConfirmation()
    {
        if (!IsInFlight)
            IsConfirmationVisible = false;
    }

    public bool TryBeginSend(out SendCallSmsRequest? request)
    {
        request = null;
        if (!IsConfirmationVisible || !CanSend)
            return false;

        _requestId ??= Guid.NewGuid();
        request = new SendCallSmsRequest(
            _requestId.Value,
            Source,
            Content,
            Mode == SmsComposeMode.Template ? SelectedTemplate?.Id : null);
        IsInFlight = true;
        return true;
    }

    public void FinishSendFailure()
    {
        if (!IsInFlight)
            return;

        IsInFlight = false;
    }

    public void FinishSendSuccess()
    {
        if (!IsInFlight)
            return;

        IsInFlight = false;
        IsConfirmationVisible = false;
        IsQueued = true;
        _requestId = null;
    }

    private void EnsureEditable()
    {
        if (IsInFlight)
            throw new InvalidOperationException("SMS compose state cannot be edited while a request is in flight.");
        if (IsQueued)
            throw new InvalidOperationException("A queued SMS compose state cannot be edited.");
    }

    private void InvalidatePendingRequest()
    {
        _requestId = null;
        IsConfirmationVisible = false;
    }
}
