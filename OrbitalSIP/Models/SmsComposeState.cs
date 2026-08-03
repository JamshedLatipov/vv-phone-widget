using System;
using System.Threading;

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

/// <summary>One cancellable send attempt owned by <see cref="SmsComposeSendSession"/>.</summary>
public sealed class SmsComposeSendAttempt
{
    private readonly CancellationTokenSource _cancellation = new();

    internal SmsComposeSendAttempt(SendCallSmsRequest request)
    {
        Request = request;
    }

    public SendCallSmsRequest Request { get; }
    public CancellationToken CancellationToken => _cancellation.Token;

    internal void Cancel() => _cancellation.Cancel();
    internal void Dispose() => _cancellation.Dispose();
}

/// <summary>
/// Presentation-level owner for the active request token. It keeps cancellation
/// retryable and rejects completions from attempts which are no longer current.
/// </summary>
public sealed class SmsComposeSendSession : IDisposable
{
    public const string CancelledMessageKey = "SmsCancelled";

    private readonly object _gate = new();
    private readonly SmsComposeState _state;
    private SmsComposeSendAttempt? _currentAttempt;
    private string? _statusMessageKey;
    private bool _disposed;

    public SmsComposeSendSession(SmsComposeState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

    public bool CanCancelSend
    {
        get
        {
            lock (_gate)
                return !_disposed && _currentAttempt is not null && _state.IsInFlight;
        }
    }

    public string? StatusMessageKey
    {
        get
        {
            lock (_gate)
                return _statusMessageKey;
        }
    }

    public bool TryBeginSend(out SmsComposeSendAttempt? attempt)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            attempt = null;
            if (_currentAttempt is not null || !_state.TryBeginSend(out var request) || request is null)
                return false;

            attempt = new SmsComposeSendAttempt(request);
            _currentAttempt = attempt;
            _statusMessageKey = null;
            return true;
        }
    }

    public bool CancelCurrentSend()
    {
        SmsComposeSendAttempt? cancelled;
        lock (_gate)
        {
            if (_disposed || _currentAttempt is null)
                return false;

            cancelled = _currentAttempt;
            _currentAttempt = null;
            _state.FinishSendFailure();
            _statusMessageKey = CancelledMessageKey;
        }

        cancelled.Cancel();
        return true;
    }

    public bool CompleteSuccess(SmsComposeSendAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        var accepted = false;
        lock (_gate)
        {
            if (ReferenceEquals(_currentAttempt, attempt))
            {
                _currentAttempt = null;
                _state.FinishSendSuccess();
                _statusMessageKey = null;
                accepted = true;
            }
        }

        attempt.Dispose();
        return accepted;
    }

    public bool CompleteFailure(SmsComposeSendAttempt attempt, string? statusMessageKey = null)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        var accepted = false;
        lock (_gate)
        {
            if (ReferenceEquals(_currentAttempt, attempt))
            {
                _currentAttempt = null;
                _state.FinishSendFailure();
                _statusMessageKey = statusMessageKey;
                accepted = true;
            }
        }

        attempt.Dispose();
        return accepted;
    }

    public void Dispose()
    {
        SmsComposeSendAttempt? cancelled;
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            cancelled = _currentAttempt;
            _currentAttempt = null;
            if (cancelled is not null)
                _state.FinishSendFailure();
        }

        if (cancelled is not null)
        {
            cancelled.Cancel();
            cancelled.Dispose();
        }
    }
}
