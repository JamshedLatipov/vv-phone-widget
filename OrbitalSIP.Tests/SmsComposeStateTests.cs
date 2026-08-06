using System;
using OrbitalSIP.Models;
using Xunit;

namespace OrbitalSIP.Tests;

public class SmsComposeStateTests
{
    private static readonly SmsCallSource ActiveSource = new("active", "asterisk-unique-id");
    private static readonly MessageTemplateDto ReminderTemplate = new(
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        "Напоминание",
        "Перезвоните, пожалуйста");

    [Fact]
    public void Constructor_LocksSourceAndDisplayRecipient()
    {
        var state = new SmsComposeState(ActiveSource, "+992 ** *** 12 34");

        Assert.Same(ActiveSource, state.Source);
        Assert.Equal("+992 ** *** 12 34", state.Recipient);
        Assert.Null(state.SelectedTemplate);
        Assert.False(state.IsTemplateBound);
    }

    [Fact]
    public void EditContent_WithoutTemplate_IsSendable()
    {
        var state = NewState();

        state.EditContent("Свободный текст");

        Assert.True(state.CanSend);
        Assert.Equal(SmsComposeValidation.None, state.Validation);
    }

    [Fact]
    public void SelectTemplate_CopiesTextAndKeepsBinding()
    {
        var state = NewState();

        state.SelectTemplate(ReminderTemplate);

        Assert.Same(ReminderTemplate, state.SelectedTemplate);
        Assert.Equal("Перезвоните, пожалуйста", state.Content);
        Assert.True(state.IsTemplateBound);
        Assert.True(state.TryBeginSend(out var request));
        Assert.Equal(ReminderTemplate.Id, request!.TemplateId);
    }

    [Fact]
    public void EditContent_AfterTemplate_DropsTemplateIdFromRequest()
    {
        var state = NewState();
        state.SelectTemplate(ReminderTemplate);

        state.EditContent("Уточнённый текст");

        Assert.Same(ReminderTemplate, state.SelectedTemplate);
        Assert.False(state.IsTemplateBound);
        Assert.True(state.TryBeginSend(out var request));
        Assert.Equal("Уточнённый текст", request!.Content);
        Assert.Null(request.TemplateId);
    }

    [Fact]
    public void SelectTemplate_AfterEdit_RestoresBindingAndText()
    {
        var state = NewState();
        state.SelectTemplate(ReminderTemplate);
        state.EditContent("Уточнённый текст");

        state.SelectTemplate(ReminderTemplate);

        Assert.Equal("Перезвоните, пожалуйста", state.Content);
        Assert.True(state.IsTemplateBound);
        Assert.True(state.TryBeginSend(out var request));
        Assert.Equal(ReminderTemplate.Id, request!.TemplateId);
    }

    [Fact]
    public void ClearTemplate_KeepsContentAndDropsTemplateId()
    {
        var state = NewState();
        state.SelectTemplate(ReminderTemplate);

        state.ClearTemplate();

        Assert.Null(state.SelectedTemplate);
        Assert.False(state.IsTemplateBound);
        Assert.Equal("Перезвоните, пожалуйста", state.Content);
        Assert.True(state.TryBeginSend(out var request));
        Assert.Null(request!.TemplateId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CanSend_RejectsBlankContent(string content)
    {
        var state = NewState();

        state.EditContent(content);

        Assert.False(state.CanSend);
        Assert.Equal(SmsComposeValidation.ContentRequired, state.Validation);
    }

    [Fact]
    public void CanSend_RejectsContentOverOneThousandCharactersAndReportsCount()
    {
        var state = NewState();

        state.EditContent(new string('x', 1001));

        Assert.Equal(1001, state.CharacterCount);
        Assert.False(state.CanSend);
        Assert.Equal(SmsComposeValidation.ContentTooLong, state.Validation);
    }

    [Fact]
    public void CanSend_AllowsExactlyOneThousandCharacters()
    {
        var state = NewState();

        state.EditContent(new string('x', 1000));

        Assert.Equal(1000, state.CharacterCount);
        Assert.True(state.CanSend);
        Assert.Equal(SmsComposeValidation.None, state.Validation);
    }

    [Fact]
    public void TryBeginSend_NeedsNoConfirmationAndGuardsDoubleClick()
    {
        var state = NewState();
        state.EditContent("Итоговый текст");

        Assert.True(state.TryBeginSend(out var request));
        Assert.False(state.TryBeginSend(out _));
        Assert.True(state.IsInFlight);
        Assert.False(state.CanSend);
        Assert.Equal(ActiveSource, request!.Source);
        Assert.Equal("Итоговый текст", request.Content);
        Assert.Null(request.TemplateId);
    }

    [Fact]
    public void FinishSendFailure_ReusesRequestIdOnRetry()
    {
        var state = NewState();
        state.EditContent("Повторяемый текст");
        Assert.True(state.TryBeginSend(out var first));

        state.FinishSendFailure();
        Assert.True(state.TryBeginSend(out var retry));

        Assert.Equal(first!.RequestId, retry!.RequestId);
    }

    [Fact]
    public void EditContent_AfterFailureRegeneratesRequestId()
    {
        var state = NewState();
        state.EditContent("Первый текст");
        Assert.True(state.TryBeginSend(out var first));
        state.FinishSendFailure();

        state.EditContent("Изменённый текст");

        Assert.True(state.TryBeginSend(out var changed));
        Assert.NotEqual(first!.RequestId, changed!.RequestId);
    }

    [Fact]
    public void CancelCurrentSend_CancelsAttemptAndKeepsRetryableRequestId()
    {
        var state = NewState();
        state.EditContent("Отменяемый текст");
        using var session = new SmsComposeSendSession(state);
        Assert.True(session.TryBeginSend(out var attempt));

        Assert.True(session.CanCancelSend);
        Assert.True(session.CancelCurrentSend());

        Assert.True(attempt!.CancellationToken.IsCancellationRequested);
        Assert.False(state.IsInFlight);
        Assert.Equal("SmsCancelled", session.StatusMessageKey);
        Assert.True(session.TryBeginSend(out var retry));
        Assert.Equal(attempt.Request.RequestId, retry!.Request.RequestId);
    }

    [Fact]
    public void CancelCurrentSend_RejectsStaleSuccessFromCancelledAttempt()
    {
        var state = NewState();
        state.EditContent("Отменяемый текст");
        using var session = new SmsComposeSendSession(state);
        Assert.True(session.TryBeginSend(out var attempt));

        session.CancelCurrentSend();
        var accepted = session.CompleteSuccess(attempt!);

        Assert.False(accepted);
        Assert.False(state.IsQueued);
        Assert.False(state.IsInFlight);
        Assert.Equal("SmsCancelled", session.StatusMessageKey);
    }

    [Fact]
    public void NewSourceState_UsesDifferentRequestIdForSameContent()
    {
        var firstState = NewState();
        firstState.EditContent("Тот же текст");
        Assert.True(firstState.TryBeginSend(out var first));

        var secondState = new SmsComposeState(new SmsCallSource("history", "cdr-id"), "+992 ** *** 12 34");
        secondState.EditContent("Тот же текст");
        Assert.True(secondState.TryBeginSend(out var second));

        Assert.NotEqual(first!.RequestId, second!.RequestId);
    }

    [Fact]
    public void FinishSendSuccess_MarksQueuedAndPreventsAnotherSend()
    {
        var state = NewState();
        state.EditContent("Текст");
        Assert.True(state.TryBeginSend(out _));

        state.FinishSendSuccess();

        Assert.True(state.IsQueued);
        Assert.False(state.IsInFlight);
        Assert.False(state.CanSend);
        Assert.False(state.TryBeginSend(out _));
    }

    [Fact]
    public void EditContent_AfterQueuedThrows()
    {
        var state = NewState();
        state.EditContent("Текст");
        Assert.True(state.TryBeginSend(out _));
        state.FinishSendSuccess();

        Assert.Throws<InvalidOperationException>(() => state.EditContent("Ещё текст"));
    }

    private static SmsComposeState NewState() => new(ActiveSource, "+992 ** *** 12 34");
}
