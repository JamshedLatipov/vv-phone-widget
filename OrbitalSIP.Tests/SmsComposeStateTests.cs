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
        Assert.Equal(SmsComposeMode.Template, state.Mode);
    }

    [Fact]
    public void SwitchMode_ToFreeText_AllowsContentWithoutTemplate()
    {
        var state = NewState();

        state.SwitchMode(SmsComposeMode.FreeText);
        state.EditContent("Свободный текст");

        Assert.Equal(SmsComposeMode.FreeText, state.Mode);
        Assert.True(state.CanSend);
    }

    [Fact]
    public void SelectTemplate_CopiesTextButKeepsFinalContentEditable()
    {
        var state = NewState();

        state.SelectTemplate(ReminderTemplate);
        state.EditContent("Уточнённый текст");

        Assert.Same(ReminderTemplate, state.SelectedTemplate);
        Assert.Equal("Уточнённый текст", state.Content);
        Assert.True(state.CanSend);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CanSend_RejectsBlankContent(string content)
    {
        var state = NewFreeTextState();

        state.EditContent(content);

        Assert.False(state.CanSend);
        Assert.Equal(SmsComposeValidation.ContentRequired, state.Validation);
    }

    [Fact]
    public void CanSend_RejectsContentOverOneThousandCharactersAndReportsCount()
    {
        var state = NewFreeTextState();

        state.EditContent(new string('x', 1001));

        Assert.Equal(1001, state.CharacterCount);
        Assert.False(state.CanSend);
        Assert.Equal(SmsComposeValidation.ContentTooLong, state.Validation);
    }

    [Fact]
    public void CanSend_AllowsExactlyOneThousandCharacters()
    {
        var state = NewFreeTextState();

        state.EditContent(new string('x', 1000));

        Assert.Equal(1000, state.CharacterCount);
        Assert.True(state.CanSend);
        Assert.Equal(SmsComposeValidation.None, state.Validation);
    }

    [Fact]
    public void CanSend_RequiresTemplateInTemplateMode()
    {
        var state = NewState();

        state.EditContent("Текст без выбранного шаблона");

        Assert.False(state.CanSend);
        Assert.Equal(SmsComposeValidation.TemplateRequired, state.Validation);
    }

    [Fact]
    public void RequestConfirmation_ShowsLockedRecipientAndFinalTextWithoutStartingRequest()
    {
        var state = NewFreeTextState();
        state.EditContent("Итоговый текст");

        var shown = state.RequestConfirmation();

        Assert.True(shown);
        Assert.True(state.IsConfirmationVisible);
        Assert.Equal("+992 ** *** 12 34", state.Recipient);
        Assert.Equal("Итоговый текст", state.Content);
        Assert.False(state.IsInFlight);
    }

    [Fact]
    public void TryBeginSend_RequiresConfirmationAndGuardsDoubleClick()
    {
        var state = NewFreeTextState();
        state.EditContent("Итоговый текст");

        Assert.False(state.TryBeginSend(out _));
        Assert.True(state.RequestConfirmation());
        Assert.True(state.TryBeginSend(out var request));
        Assert.False(state.TryBeginSend(out _));
        Assert.True(state.IsInFlight);
        Assert.False(state.CanSend);
        Assert.Equal(ActiveSource, request!.Source);
        Assert.Equal("Итоговый текст", request.Content);
        Assert.Null(request.TemplateId);
    }

    [Fact]
    public void FinishSendFailure_ReusesRequestIdOnConfirmedRetry()
    {
        var state = NewFreeTextState();
        state.EditContent("Повторяемый текст");
        state.RequestConfirmation();
        Assert.True(state.TryBeginSend(out var first));

        state.FinishSendFailure();
        Assert.True(state.TryBeginSend(out var retry));

        Assert.Equal(first!.RequestId, retry!.RequestId);
    }

    [Fact]
    public void EditContent_AfterFailureHidesConfirmationAndRegeneratesRequestId()
    {
        var state = NewFreeTextState();
        state.EditContent("Первый текст");
        state.RequestConfirmation();
        Assert.True(state.TryBeginSend(out var first));
        state.FinishSendFailure();

        state.EditContent("Изменённый текст");

        Assert.False(state.IsConfirmationVisible);
        Assert.True(state.RequestConfirmation());
        Assert.True(state.TryBeginSend(out var changed));
        Assert.NotEqual(first!.RequestId, changed!.RequestId);
    }

    [Fact]
    public void NewSourceState_UsesDifferentRequestIdForSameContent()
    {
        var firstState = NewFreeTextState();
        firstState.EditContent("Тот же текст");
        firstState.RequestConfirmation();
        Assert.True(firstState.TryBeginSend(out var first));

        var secondState = new SmsComposeState(new SmsCallSource("history", "cdr-id"), "+992 ** *** 12 34");
        secondState.SwitchMode(SmsComposeMode.FreeText);
        secondState.EditContent("Тот же текст");
        secondState.RequestConfirmation();
        Assert.True(secondState.TryBeginSend(out var second));

        Assert.NotEqual(first!.RequestId, second!.RequestId);
    }

    [Fact]
    public void FinishSendSuccess_MarksQueuedAndPreventsAnotherSend()
    {
        var state = NewFreeTextState();
        state.EditContent("Текст");
        state.RequestConfirmation();
        Assert.True(state.TryBeginSend(out _));

        state.FinishSendSuccess();

        Assert.True(state.IsQueued);
        Assert.False(state.IsInFlight);
        Assert.False(state.IsConfirmationVisible);
        Assert.False(state.CanSend);
        Assert.False(state.RequestConfirmation());
    }

    private static SmsComposeState NewState() => new(ActiveSource, "+992 ** *** 12 34");

    private static SmsComposeState NewFreeTextState()
    {
        var state = NewState();
        state.SwitchMode(SmsComposeMode.FreeText);
        return state;
    }
}
