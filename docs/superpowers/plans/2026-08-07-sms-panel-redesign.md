# SMS Panel Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Превратить `SmsComposeDialog` в одноэкранную панель: без переключателя режимов, без экрана подтверждения, с автодополнением шаблонов, автозакрытием после успеха и повтором после ошибки.

**Architecture:** Вся работа в репозитории виджета. `SmsComposeState` теряет режимы и подтверждение, взамен получает флаг привязки текста к шаблону, который решает, уйдёт ли `templateId` в запрос. `SmsComposeDialog` перекладывается в сетку `Auto,*,Auto`, где поле сообщения занимает всю свободную высоту, а `ComboBox` шаблонов заменяется на `AutoCompleteBox` с фильтрацией по имени и телу шаблона. Форматирование номера выносится в чистый статический хелпер, чтобы покрыть тестами.

**Tech Stack:** .NET 8 (`net8.0-windows10.0.17763`), Avalonia 11.0.0 + Fluent, Material.Icons.Avalonia 2.1.0, xUnit 2.5.3.

**Спека:** `docs/superpowers/specs/2026-08-07-sms-panel-redesign-design.md`

---

## Структура файлов

| Файл | Действие | Ответственность |
|---|---|---|
| `OrbitalSIP/Models/SmsRecipientFormatter.cs` | создать | Чистое форматирование номера для показа. Ничего не знает про UI и про запрос. |
| `OrbitalSIP.Tests/SmsRecipientFormatterTests.cs` | создать | Тесты форматтера. |
| `OrbitalSIP/Models/SmsComposeState.cs` | изменить | Состояние черновика без режимов и подтверждения; решает судьбу `templateId`. |
| `OrbitalSIP.Tests/SmsComposeStateTests.cs` | изменить | Тесты нового контракта состояния. |
| `OrbitalSIP/Assets/i18n/ru.json` | изменить | Строки RU. |
| `OrbitalSIP/Assets/i18n/kk.json` | изменить | Строки KK. |
| `OrbitalSIP/Assets/i18n/tg.json` | изменить | Строки TG. |
| `OrbitalSIP/Assets/i18n/uz.json` | изменить | Строки UZ. |
| `OrbitalSIP/App.axaml` | изменить | Стили `AutoCompleteBox` для тёмной темы. |
| `OrbitalSIP/Views/SmsComposeDialog.axaml` | изменить | Новая разметка панели. |
| `OrbitalSIP/Views/SmsComposeDialog.axaml.cs` | изменить | Проводка контролов, автодополнение, состояния футера, автозакрытие. |

`SmsComposeSendSession` (в том же файле, что и `SmsComposeState`) не меняется — он не знает про подтверждение.

**Отклонения от спеки, принятые здесь:**

- Кнопка сброса шаблона — отдельная кнопка справа от `AutoCompleteBox` в сетке `*,Auto`, а не крестик внутри поля. Перешаблонивание `AutoCompleteBox` ради этого не окупается.
- Вместо нового ключа `SmsTemplateSearch` переиспользуется существующий `SmsTemplatePlaceholder` с новым текстом «Поиск по шаблонам» — он и так стоял в этом поле.
- Добавляются два ключа сверх спеки: `SmsSendingShort` (существующий `SmsSending` — «SMS ставится в очередь…» — не влезает в кнопку футера) и `SmsTemplateClear` (подсказка на кнопке сброса).

**Порядок сборки:** Task 2 удаляет `SmsComposeMode` и `RequestConfirmation`, которыми ещё пользуется `SmsComposeDialog.axaml.cs`. Проект `OrbitalSIP` не собирается с конца Task 2 до конца Task 6, а `dotnet test` собирает `OrbitalSIP` по ссылке проекта, поэтому зелёный прогон тестов возможен только начиная с Task 6 Step 3. Задачи 2–6 выполняются подряд, коммиты в этом окне промежуточные.

---

### Task 1: Форматирование номера получателя

**Files:**
- Create: `OrbitalSIP/Models/SmsRecipientFormatter.cs`
- Test: `OrbitalSIP.Tests/SmsRecipientFormatterTests.cs`

- [ ] **Step 1: Написать падающий тест**

Создать `OrbitalSIP.Tests/SmsRecipientFormatterTests.cs`:

```csharp
using OrbitalSIP.Models;
using Xunit;

namespace OrbitalSIP.Tests;

public class SmsRecipientFormatterTests
{
    [Fact]
    public void Format_GroupsNineDigitLocalNumber()
    {
        Assert.Equal("021 88 49 49", SmsRecipientFormatter.Format("021884949"));
    }

    [Fact]
    public void Format_GroupsTajikNumberWithCountryCode()
    {
        Assert.Equal("+992 90 123 45 67", SmsRecipientFormatter.Format("+992901234567"));
    }

    [Fact]
    public void Format_AddsPlusToBareCountryCodeNumber()
    {
        Assert.Equal("+992 90 123 45 67", SmsRecipientFormatter.Format("992901234567"));
    }

    [Fact]
    public void Format_LeavesAlreadySpacedValueUntouched()
    {
        Assert.Equal("+992 ** *** 12 34", SmsRecipientFormatter.Format("+992 ** *** 12 34"));
    }

    [Fact]
    public void Format_LeavesUnknownShapeUntouched()
    {
        Assert.Equal("3333", SmsRecipientFormatter.Format("3333"));
    }

    [Fact]
    public void Format_LeavesNonDigitValueUntouched()
    {
        Assert.Equal("anonymous", SmsRecipientFormatter.Format("anonymous"));
    }

    [Fact]
    public void Format_TrimsSurroundingWhitespace()
    {
        Assert.Equal("021 88 49 49", SmsRecipientFormatter.Format("  021884949  "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Format_ReturnsEmptyForBlankInput(string? raw)
    {
        Assert.Equal(string.Empty, SmsRecipientFormatter.Format(raw));
    }
}
```

- [ ] **Step 2: Запустить тест, убедиться что падает**

```bash
dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --filter FullyQualifiedName~SmsRecipientFormatterTests
```

Ожидается: ошибка компиляции `CS0103: The name 'SmsRecipientFormatter' does not exist in the current context`.

- [ ] **Step 3: Написать минимальную реализацию**

Создать `OrbitalSIP/Models/SmsRecipientFormatter.cs`:

```csharp
using System.Linq;

namespace OrbitalSIP.Models;

/// <summary>
/// Cosmetic grouping for the locked recipient. Display only — the raw value
/// stays untouched in <see cref="SmsComposeState.Recipient"/> and never reaches
/// the request, which is built server-side from the call anchor.
/// </summary>
public static class SmsRecipientFormatter
{
    public static string Format(string? raw)
    {
        var value = raw?.Trim() ?? string.Empty;
        if (value.Length == 0)
            return string.Empty;

        // Anything already carrying separators arrived formatted (or masked) from
        // upstream. Regrouping it would corrupt masks like "+992 ** *** 12 34".
        var digits = value.StartsWith('+') ? value[1..] : value;
        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit))
            return value;

        if (digits.Length == 12 && digits.StartsWith("992"))
            return $"+992 {digits[3..5]} {digits[5..8]} {digits[8..10]} {digits[10..12]}";

        if (digits.Length == 9 && !value.StartsWith('+'))
            return $"{digits[0..3]} {digits[3..5]} {digits[5..7]} {digits[7..9]}";

        return value;
    }
}
```

- [ ] **Step 4: Запустить тест, убедиться что проходит**

```bash
dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --filter FullyQualifiedName~SmsRecipientFormatterTests
```

Ожидается: `Passed! - Failed: 0, Passed: 10`.

- [ ] **Step 5: Коммит**

```bash
git add OrbitalSIP/Models/SmsRecipientFormatter.cs OrbitalSIP.Tests/SmsRecipientFormatterTests.cs
git commit -m "feat(sms): add display formatter for the locked recipient"
```

---

### Task 2: `SmsComposeState` без режимов и подтверждения

**Files:**
- Modify: `OrbitalSIP/Models/SmsComposeState.cs:1-165`
- Modify: `OrbitalSIP.Tests/SmsComposeStateTests.cs` (заменить целиком)

- [ ] **Step 1: Переписать тесты под новый контракт**

Заменить содержимое `OrbitalSIP.Tests/SmsComposeStateTests.cs` целиком:

```csharp
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
```

- [ ] **Step 2: Запустить тесты, убедиться что падают**

```bash
dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --filter FullyQualifiedName~SmsComposeStateTests
```

Ожидается: ошибки компиляции `CS1061` — у `SmsComposeState` нет `IsTemplateBound` и `ClearTemplate`.

- [ ] **Step 3: Переписать состояние**

Заменить в `OrbitalSIP/Models/SmsComposeState.cs` всё от начала файла до закрывающей скобки класса `SmsComposeState` (строки 1–165), оставив `SmsComposeSendAttempt` и `SmsComposeSendSession` ниже без изменений:

```csharp
using System;
using System.Threading;

namespace OrbitalSIP.Models;

public enum SmsComposeValidation
{
    None,
    ContentRequired,
    ContentTooLong,
}

/// <summary>
/// UI-independent state for composing one SMS from an immutable call anchor.
/// The display recipient never participates in API request construction.
/// </summary>
public sealed class SmsComposeState
{
    public const int MaxContentLength = 1000;

    private Guid? _requestId;
    private bool _contentMatchesTemplate;

    public SmsComposeState(SmsCallSource source, string recipient)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Recipient = string.IsNullOrWhiteSpace(recipient)
            ? throw new ArgumentException("A display recipient is required.", nameof(recipient))
            : recipient;
    }

    public SmsCallSource Source { get; }
    public string Recipient { get; }
    public MessageTemplateDto? SelectedTemplate { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public int CharacterCount => Content.Length;
    public bool IsInFlight { get; private set; }
    public bool IsQueued { get; private set; }

    /// <summary>
    /// True while the content is byte-identical to the template it came from.
    /// Only then does the request carry a template id — an edited body is a
    /// free-text message that happens to have started from a template.
    /// </summary>
    public bool IsTemplateBound => _contentMatchesTemplate && SelectedTemplate is not null;

    public SmsComposeValidation Validation
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Content))
                return SmsComposeValidation.ContentRequired;
            if (CharacterCount > MaxContentLength)
                return SmsComposeValidation.ContentTooLong;
            return SmsComposeValidation.None;
        }
    }

    public bool CanSend => !IsInFlight && !IsQueued && Validation == SmsComposeValidation.None;

    public void SelectTemplate(MessageTemplateDto template)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (string.IsNullOrWhiteSpace(template.Content))
            throw new ArgumentException("A compose-ready template must contain text.", nameof(template));

        EnsureEditable();
        var changed = !IsTemplateBound ||
                      SelectedTemplate?.Id != template.Id ||
                      !string.Equals(Content, template.Content, StringComparison.Ordinal);
        SelectedTemplate = template;
        Content = template.Content;
        _contentMatchesTemplate = true;
        if (changed)
            InvalidatePendingRequest();
    }

    /// <summary>Drops the template without touching the text the operator already has.</summary>
    public void ClearTemplate()
    {
        EnsureEditable();
        if (SelectedTemplate is null)
            return;

        SelectedTemplate = null;
        _contentMatchesTemplate = false;
        InvalidatePendingRequest();
    }

    public void EditContent(string? content)
    {
        EnsureEditable();
        content ??= string.Empty;
        if (string.Equals(Content, content, StringComparison.Ordinal))
            return;

        Content = content;
        _contentMatchesTemplate = false;
        InvalidatePendingRequest();
    }

    public bool TryBeginSend(out SendCallSmsRequest? request)
    {
        request = null;
        if (!CanSend)
            return false;

        _requestId ??= Guid.NewGuid();
        request = new SendCallSmsRequest(
            _requestId.Value,
            Source,
            Content,
            IsTemplateBound ? SelectedTemplate!.Id : null);
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

    private void InvalidatePendingRequest() => _requestId = null;
}
```

- [ ] **Step 4: Убедиться, что осталась только ожидаемая поломка**

```bash
dotnet build OrbitalSIP.Tests/OrbitalSIP.Tests.csproj -c Debug
```

Ожидается: сборка падает, и **все** ошибки приходят из `OrbitalSIP/Views/SmsComposeDialog.axaml.cs` — `CS0246` по `SmsComposeMode` и `CS1061` по `RequestConfirmation`, `IsConfirmationVisible`, `SwitchMode`. Ошибок из `OrbitalSIP.Tests/SmsComposeStateTests.cs` и из `OrbitalSIP/Models/SmsComposeState.cs` быть не должно — если они есть, чинить их здесь, до перехода к следующей задаче. Зелёный прогон тестов будет в Task 6 Step 3.

- [ ] **Step 5: Коммит**

```bash
git add OrbitalSIP/Models/SmsComposeState.cs OrbitalSIP.Tests/SmsComposeStateTests.cs
git commit -m "refactor(sms): drop compose modes and the confirmation step from state"
```

---

### Task 3: Строки локализации

**Files:**
- Modify: `OrbitalSIP/Assets/i18n/ru.json:175-204`
- Modify: `OrbitalSIP/Assets/i18n/kk.json:165-194`
- Modify: `OrbitalSIP/Assets/i18n/tg.json:163-192`
- Modify: `OrbitalSIP/Assets/i18n/uz.json:165-194`

Тестов нет: `I18nService` читает JSON во время выполнения, покрытие даёт запуск приложения в Task 7.

- [ ] **Step 1: Обновить `ru.json`**

Удалить ключи `SmsRecipientLocked`, `SmsMode`, `SmsTemplateMode`, `SmsFreeTextMode`, `SmsTemplateRequired`, `SmsConfirmTitle`, `SmsConfirmHint`, `SmsBack`, `SmsConfirmSend`. Блок `Sms*` должен стать таким:

```json
  "SmsDialogTitle": "Отправить SMS",
  "SmsRecipient": "ПОЛУЧАТЕЛЬ",
  "SmsRecipientFromCall": "из звонка",
  "SmsTemplate": "Шаблон",
  "SmsTemplateOptional": "необязательно",
  "SmsTemplatePlaceholder": "Поиск по шаблонам",
  "SmsTemplateClear": "Сбросить шаблон",
  "SmsTemplatesLoading": "Загрузка шаблонов…",
  "SmsTemplatesEmpty": "Нет доступных шаблонов",
  "SmsTemplatesLoadError": "Не удалось загрузить шаблоны",
  "SmsMessage": "Сообщение",
  "SmsMessagePlaceholder": "Введите текст SMS",
  "SmsCharacterCount": "{0} / {1}",
  "SmsContentRequired": "Введите текст сообщения",
  "SmsContentTooLong": "Сообщение не должно превышать 1000 знаков",
  "SmsSend": "Отправить",
  "SmsRetry": "Повторить",
  "SmsCancelSend": "Отменить отправку",
  "SmsSending": "SMS ставится в очередь…",
  "SmsSendingShort": "Отправка…",
  "SmsQueuedSuccess": "SMS поставлено в очередь",
  "SmsSendError": "Не удалось поставить SMS в очередь",
  "SmsCancelled": "Отправка SMS отменена",
  "Sms": "SMS",
  "SmsActiveCallUnavailable": "Не удалось подтвердить активный звонок для SMS. Повторите попытку.",
  "SmsHistoryCallUnavailable": "Не удалось подтвердить запись истории для SMS. Повторите попытку."
```

- [ ] **Step 2: Обновить `kk.json`**

```json
  "SmsDialogTitle": "SMS жіберу",
  "SmsRecipient": "АЛУШЫ",
  "SmsRecipientFromCall": "қоңыраудан",
  "SmsTemplate": "Үлгі",
  "SmsTemplateOptional": "міндетті емес",
  "SmsTemplatePlaceholder": "Үлгілерден іздеу",
  "SmsTemplateClear": "Үлгіні алып тастау",
  "SmsTemplatesLoading": "Үлгілер жүктелуде…",
  "SmsTemplatesEmpty": "Қолжетімді үлгілер жоқ",
  "SmsTemplatesLoadError": "Үлгілерді жүктеу мүмкін болмады",
  "SmsMessage": "Хабарлама",
  "SmsMessagePlaceholder": "SMS мәтінін енгізіңіз",
  "SmsCharacterCount": "{0} / {1}",
  "SmsContentRequired": "Хабарлама мәтінін енгізіңіз",
  "SmsContentTooLong": "Хабарлама 1000 таңбадан аспауы керек",
  "SmsSend": "Жіберу",
  "SmsRetry": "Қайталау",
  "SmsCancelSend": "Жіберуді тоқтату",
  "SmsSending": "SMS кезекке қойылуда…",
  "SmsSendingShort": "Жіберілуде…",
  "SmsQueuedSuccess": "SMS кезекке қойылды",
  "SmsSendError": "SMS-ті кезекке қою мүмкін болмады",
  "SmsCancelled": "SMS жіберу тоқтатылды",
  "Sms": "SMS",
  "SmsActiveCallUnavailable": "SMS үшін белсенді қоңырауды растау мүмкін болмады. Қайталап көріңіз.",
  "SmsHistoryCallUnavailable": "SMS үшін қоңыраулар тарихындағы жазбаны растау мүмкін болмады. Қайталап көріңіз."
```

- [ ] **Step 3: Обновить `tg.json`**

```json
  "SmsDialogTitle": "Ирсоли SMS",
  "SmsRecipient": "ҚАБУЛКУНАНДА",
  "SmsRecipientFromCall": "аз занг",
  "SmsTemplate": "Шаблон",
  "SmsTemplateOptional": "ихтиёрӣ",
  "SmsTemplatePlaceholder": "Ҷустуҷӯ дар шаблонҳо",
  "SmsTemplateClear": "Бекор кардани шаблон",
  "SmsTemplatesLoading": "Шаблонҳо бор шуда истодаанд…",
  "SmsTemplatesEmpty": "Шаблонҳои дастрас нестанд",
  "SmsTemplatesLoadError": "Шаблонҳоро бор кардан муяссар нашуд",
  "SmsMessage": "Паём",
  "SmsMessagePlaceholder": "Матни SMS-ро ворид кунед",
  "SmsCharacterCount": "{0} / {1}",
  "SmsContentRequired": "Матни паёмро ворид кунед",
  "SmsContentTooLong": "Паём набояд аз 1000 аломат зиёд бошад",
  "SmsSend": "Ирсол",
  "SmsRetry": "Такрор",
  "SmsCancelSend": "Бекор кардани ирсол",
  "SmsSending": "SMS ба навбат гузошта мешавад…",
  "SmsSendingShort": "Ирсол…",
  "SmsQueuedSuccess": "SMS ба навбат гузошта шуд",
  "SmsSendError": "SMS-ро ба навбат гузоштан муяссар нашуд",
  "SmsCancelled": "Ирсоли SMS бекор шуд",
  "Sms": "SMS",
  "SmsActiveCallUnavailable": "Занги фаъолро барои SMS тасдиқ кардан муяссар нашуд. Боз кӯшиш кунед.",
  "SmsHistoryCallUnavailable": "Сабти таърихи зангҳоро барои SMS тасдиқ кардан муяссар нашуд. Боз кӯшиш кунед."
```

- [ ] **Step 4: Обновить `uz.json`**

```json
  "SmsDialogTitle": "SMS yuborish",
  "SmsRecipient": "QABUL QILUVCHI",
  "SmsRecipientFromCall": "qo'ng'iroqdan",
  "SmsTemplate": "Shablon",
  "SmsTemplateOptional": "majburiy emas",
  "SmsTemplatePlaceholder": "Shablonlardan qidirish",
  "SmsTemplateClear": "Shablonni bekor qilish",
  "SmsTemplatesLoading": "Shablonlar yuklanmoqda…",
  "SmsTemplatesEmpty": "Mavjud shablonlar yo'q",
  "SmsTemplatesLoadError": "Shablonlarni yuklab bo'lmadi",
  "SmsMessage": "Xabar",
  "SmsMessagePlaceholder": "SMS matnini kiriting",
  "SmsCharacterCount": "{0} / {1}",
  "SmsContentRequired": "Xabar matnini kiriting",
  "SmsContentTooLong": "Xabar 1000 belgidan oshmasligi kerak",
  "SmsSend": "Yuborish",
  "SmsRetry": "Qayta urinish",
  "SmsCancelSend": "Yuborishni bekor qilish",
  "SmsSending": "SMS navbatga qo'yilmoqda…",
  "SmsSendingShort": "Yuborilmoqda…",
  "SmsQueuedSuccess": "SMS navbatga qo'yildi",
  "SmsSendError": "SMS-ni navbatga qo'yib bo'lmadi",
  "SmsCancelled": "SMS yuborish bekor qilindi",
  "Sms": "SMS",
  "SmsActiveCallUnavailable": "SMS uchun faol qo'ng'iroqni tasdiqlab bo'lmadi. Qayta urinib ko'ring.",
  "SmsHistoryCallUnavailable": "SMS uchun qo'ng'iroqlar tarixidagi yozuvni tasdiqlab bo'lmadi. Qayta urinib ko'ring."
```

- [ ] **Step 5: Проверить, что JSON валиден и наборы ключей совпадают**

```bash
python -c "import json,glob; ks=[set(json.load(open(f,encoding='utf-8'))) for f in sorted(glob.glob('OrbitalSIP/Assets/i18n/*.json'))]; print('same keys:', all(k==ks[0] for k in ks))"
```

Ожидается: `same keys: True`.

- [ ] **Step 6: Коммит**

```bash
git add OrbitalSIP/Assets/i18n
git commit -m "chore(i18n): retune SMS strings for the redesigned compose panel"
```

---

### Task 4: Стили `AutoCompleteBox` для тёмной темы

**Files:**
- Modify: `OrbitalSIP/App.axaml:11-40`

- [ ] **Step 1: Добавить стили**

Вставить в `<Application.Styles>` сразу после блока стилей `ComboBox` (после строки со `Style Selector="ComboBox:focus ..."`, перед `</Application.Styles>`):

```xml
    <Style Selector="AutoCompleteBox">
      <Setter Property="Background" Value="#1E293B" />
      <Setter Property="BorderBrush" Value="#334155" />
      <Setter Property="Foreground" Value="#E2E8F0" />
      <Setter Property="CornerRadius" Value="8" />
    </Style>

    <Style Selector="AutoCompleteBox /template/ TextBox#PART_TextBox">
      <Setter Property="Background" Value="#1E293B" />
      <Setter Property="BorderBrush" Value="#334155" />
      <Setter Property="BorderThickness" Value="1" />
      <Setter Property="Foreground" Value="#E2E8F0" />
      <Setter Property="CornerRadius" Value="8" />
      <Setter Property="Padding" Value="10,7" />
    </Style>

    <Style Selector="AutoCompleteBox:pointerover /template/ TextBox#PART_TextBox">
      <Setter Property="BorderBrush" Value="#3B82F6" />
    </Style>

    <Style Selector="AutoCompleteBox:focus-within /template/ TextBox#PART_TextBox">
      <Setter Property="BorderBrush" Value="#3B82F6" />
    </Style>

    <Style Selector="AutoCompleteBox /template/ Popup#PART_Popup ListBox">
      <Setter Property="Background" Value="#152132" />
      <Setter Property="BorderBrush" Value="#334155" />
      <Setter Property="BorderThickness" Value="1" />
      <Setter Property="CornerRadius" Value="8" />
    </Style>

    <Style Selector="AutoCompleteBox ListBoxItem">
      <Setter Property="Padding" Value="10,7" />
      <Setter Property="Foreground" Value="#E2E8F0" />
    </Style>

    <Style Selector="AutoCompleteBox ListBoxItem:pointerover /template/ ContentPresenter">
      <Setter Property="Background" Value="#1D3055" />
    </Style>

    <Style Selector="AutoCompleteBox ListBoxItem:selected /template/ ContentPresenter">
      <Setter Property="Background" Value="#1D4ED8" />
    </Style>
```

Существующее правило `TextBox:focus /template/ Border#PART_BorderElement` в этом же файле обнуляет рамку у всех `TextBox`, включая внутренний `PART_TextBox`. Рамку рисует стиль выше на самом `PART_TextBox`, поэтому конфликта нет: обнуляется `Border#PART_BorderElement` внутри шаблона `TextBox`, а не сам `TextBox`.

- [ ] **Step 2: Проверить, что XAML компилируется**

```bash
dotnet build OrbitalSIP/OrbitalSIP.csproj -c Debug
```

Ожидается: сборка падает на `SmsComposeDialog.axaml.cs` (Task 2 уже удалил `SmsComposeMode`), но **не** на `App.axaml`. В выводе не должно быть строк с `App.axaml` и `AVLN`.

- [ ] **Step 3: Коммит**

```bash
git add OrbitalSIP/App.axaml
git commit -m "style(app): theme AutoCompleteBox for the dark palette"
```

---

### Task 5: Новая разметка панели

**Files:**
- Modify: `OrbitalSIP/Views/SmsComposeDialog.axaml` (заменить целиком)

- [ ] **Step 1: Заменить разметку**

Заменить содержимое `OrbitalSIP/Views/SmsComposeDialog.axaml` целиком:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:i18n="clr-namespace:OrbitalSIP.Services"
        xmlns:materialIcons="clr-namespace:Material.Icons.Avalonia;assembly=Material.Icons.Avalonia"
        x:Class="OrbitalSIP.Views.SmsComposeDialog"
        Title="SMS"
        Width="420" Height="560"
        MinWidth="380" MinHeight="480"
        WindowStartupLocation="CenterOwner"
        Background="#0F172A"
        Topmost="True"
        SystemDecorations="None"
        CornerRadius="16">
  <Border BorderBrush="#1E293B" BorderThickness="1" CornerRadius="16">
    <Grid RowDefinitions="Auto,*,Auto">

      <Border Name="HeaderBar" Background="#152132" CornerRadius="16,16,0,0"
              Padding="14,12" BorderBrush="#1E293B" BorderThickness="0,0,0,1">
        <Grid ColumnDefinitions="Auto,*,Auto">
          <materialIcons:MaterialIcon Kind="MessageTextOutline" Width="17" Height="17"
                                      Foreground="#3B82F6" VerticalAlignment="Center" />
          <TextBlock Grid.Column="1" Margin="8,0,0,0" Text="{i18n:I18n SmsDialogTitle}"
                     Foreground="#E2E8F0" FontWeight="SemiBold" VerticalAlignment="Center" />
          <Button Grid.Column="2" Name="CloseBtn" Width="28" Height="28"
                  Background="Transparent" BorderThickness="0" Padding="0"
                  HorizontalContentAlignment="Center" VerticalContentAlignment="Center" Cursor="Hand">
            <materialIcons:MaterialIcon Kind="Close" Width="16" Height="16" Foreground="#94A3B8" />
          </Button>
        </Grid>
      </Border>

      <ScrollViewer Grid.Row="1" HorizontalScrollBarVisibility="Disabled"
                    VerticalScrollBarVisibility="Auto">
        <Grid Margin="14" RowDefinitions="Auto,Auto,*">

          <Border Background="#111C2E" BorderBrush="#2A3B52" BorderThickness="1"
                  CornerRadius="10" Padding="11,10">
            <Grid ColumnDefinitions="Auto,*,Auto">
              <Border Width="34" Height="34" CornerRadius="17" Background="#1D3055">
                <materialIcons:MaterialIcon Kind="AccountOutline" Width="17" Height="17"
                                            Foreground="#7FB0F0" />
              </Border>
              <StackPanel Grid.Column="1" Margin="11,0,8,0" VerticalAlignment="Center">
                <TextBlock Text="{i18n:I18n SmsRecipient}" FontSize="10"
                           LetterSpacing="0.8" Foreground="#7B92AA" />
                <TextBlock Name="RecipientValue" Margin="0,1,0,0"
                           Foreground="#E2E8F0" FontSize="16" FontWeight="SemiBold"
                           TextTrimming="CharacterEllipsis" />
              </StackPanel>
              <Border Grid.Column="2" Background="#16233A" CornerRadius="6" Padding="7,3"
                      VerticalAlignment="Center">
                <StackPanel Orientation="Horizontal" Spacing="4">
                  <materialIcons:MaterialIcon Kind="LockOutline" Width="12" Height="12"
                                              Foreground="#64748B" VerticalAlignment="Center" />
                  <TextBlock Text="{i18n:I18n SmsRecipientFromCall}" FontSize="10"
                             Foreground="#64748B" VerticalAlignment="Center" />
                </StackPanel>
              </Border>
            </Grid>
          </Border>

          <StackPanel Grid.Row="1" Margin="0,12,0,0" Spacing="5">
            <Grid ColumnDefinitions="*,Auto">
              <TextBlock Text="{i18n:I18n SmsTemplate}" FontSize="11" Foreground="#94A3B8" />
              <TextBlock Grid.Column="1" Text="{i18n:I18n SmsTemplateOptional}"
                         FontSize="10" Foreground="#64748B" VerticalAlignment="Center" />
            </Grid>
            <Grid ColumnDefinitions="*,Auto">
              <AutoCompleteBox Name="TemplateBox" HorizontalAlignment="Stretch"
                               Watermark="{i18n:I18n SmsTemplatePlaceholder}"
                               MinimumPrefixLength="0" MaxDropDownHeight="220">
                <AutoCompleteBox.ItemTemplate>
                  <DataTemplate>
                    <StackPanel Spacing="1">
                      <TextBlock Text="{Binding Name}" FontSize="13" Foreground="#E2E8F0" />
                      <TextBlock Text="{Binding Content}" FontSize="11" Foreground="#7B92AA"
                                 MaxLines="1" TextTrimming="CharacterEllipsis" />
                    </StackPanel>
                  </DataTemplate>
                </AutoCompleteBox.ItemTemplate>
              </AutoCompleteBox>
              <Button Grid.Column="1" Name="ClearTemplateBtn" IsVisible="False"
                      Margin="6,0,0,0" Width="34" Height="34" Padding="0"
                      Background="#1E293B" BorderBrush="#334155" BorderThickness="1"
                      CornerRadius="8" Cursor="Hand"
                      HorizontalContentAlignment="Center" VerticalContentAlignment="Center"
                      ToolTip.Tip="{i18n:I18n SmsTemplateClear}">
                <materialIcons:MaterialIcon Kind="Close" Width="14" Height="14" Foreground="#94A3B8" />
              </Button>
            </Grid>
            <TextBlock Name="TemplateStatusLabel" IsVisible="False" FontSize="11"
                       Foreground="#94A3B8" TextWrapping="Wrap" />
          </StackPanel>

          <Grid Grid.Row="2" Margin="0,12,0,0" RowDefinitions="Auto,*,Auto,Auto">
            <Grid ColumnDefinitions="*,Auto" Margin="0,0,0,5">
              <TextBlock Text="{i18n:I18n SmsMessage}" FontSize="11" Foreground="#94A3B8" />
              <TextBlock Grid.Column="1" Name="CountLabel" Foreground="#64748B" FontSize="10"
                         VerticalAlignment="Center" />
            </Grid>
            <TextBox Grid.Row="1" Name="ContentBox" MinHeight="120"
                     AcceptsReturn="True" TextWrapping="Wrap"
                     VerticalContentAlignment="Top" Watermark="{i18n:I18n SmsMessagePlaceholder}"
                     Background="#1E293B" BorderBrush="#334155" BorderThickness="1"
                     Foreground="#E2E8F0" CornerRadius="8" Padding="10,8" />
            <TextBlock Grid.Row="2" Name="ValidationLabel" Margin="2,5,0,0"
                       Foreground="#F87171" FontSize="11"
                       TextWrapping="Wrap" IsVisible="False" />
            <Border Grid.Row="3" Name="ErrorBanner" IsVisible="False" Margin="0,8,0,0"
                    Background="#2A1416" BorderBrush="#7F1D1D" BorderThickness="1"
                    CornerRadius="8" Padding="10,8">
              <StackPanel Orientation="Horizontal" Spacing="7">
                <materialIcons:MaterialIcon Kind="AlertCircleOutline" Width="14" Height="14"
                                            Foreground="#FCA5A5" VerticalAlignment="Top" />
                <TextBlock Name="ErrorLabel" Foreground="#FCA5A5" FontSize="11"
                           TextWrapping="Wrap" MaxWidth="320" />
              </StackPanel>
            </Border>
          </Grid>

        </Grid>
      </ScrollViewer>

      <Border Grid.Row="2" Background="#152132" CornerRadius="0,0,16,16"
              Padding="14,12" BorderBrush="#1E293B" BorderThickness="0,1,0,0">
        <Panel>
          <Grid Name="ComposeFooter" ColumnDefinitions="Auto,*,Auto">
            <Button Name="CancelBtn" Grid.Column="0" Content="{i18n:I18n Cancel}"
                    Background="Transparent" BorderThickness="1" BorderBrush="#334155"
                    Foreground="#94A3B8" CornerRadius="8" Padding="13,7" Cursor="Hand" />
            <Button Name="SendBtn" Grid.Column="2"
                    Background="#3B82F6" BorderThickness="0"
                    CornerRadius="8" Padding="14,7" Cursor="Hand">
              <StackPanel Orientation="Horizontal" Spacing="6">
                <materialIcons:MaterialIcon Name="SendIcon" Kind="Send" Width="15" Height="15"
                                            Foreground="#FFFFFF" VerticalAlignment="Center" />
                <TextBlock Name="SendLabel" Text="{i18n:I18n SmsSend}" Foreground="#FFFFFF"
                           FontWeight="SemiBold" VerticalAlignment="Center" />
              </StackPanel>
            </Button>
          </Grid>

          <Border Name="SuccessBanner" IsVisible="False" Background="#052E2B"
                  BorderBrush="#0F766E" BorderThickness="1" CornerRadius="8" Padding="10,9">
            <StackPanel Orientation="Horizontal" Spacing="7">
              <materialIcons:MaterialIcon Kind="CheckCircleOutline" Width="15" Height="15"
                                          Foreground="#5EEAD4" VerticalAlignment="Center" />
              <TextBlock Text="{i18n:I18n SmsQueuedSuccess}" Foreground="#5EEAD4"
                         FontSize="12" FontWeight="SemiBold" TextWrapping="Wrap"
                         VerticalAlignment="Center" />
            </StackPanel>
          </Border>
        </Panel>
      </Border>

    </Grid>
  </Border>
</Window>
```

- [ ] **Step 2: Проверить, что XAML компилируется**

```bash
dotnet build OrbitalSIP/OrbitalSIP.csproj -c Debug
```

Ожидается: сборка всё ещё падает на `SmsComposeDialog.axaml.cs` (`CS0246`/`CS1061` по `SmsComposeMode`, `TemplateModeBtn` и прочему), но в выводе не должно быть ошибок `AVLN` по `SmsComposeDialog.axaml`. Код-бихайнд чинится в Task 6.

- [ ] **Step 3: Коммит**

```bash
git add OrbitalSIP/Views/SmsComposeDialog.axaml
git commit -m "feat(sms): relayout the compose panel to a single screen"
```

---

### Task 6: Проводка панели

**Files:**
- Modify: `OrbitalSIP/Views/SmsComposeDialog.axaml.cs` (заменить целиком)

- [ ] **Step 1: Заменить код-бихайнд**

Заменить содержимое `OrbitalSIP/Views/SmsComposeDialog.axaml.cs` целиком:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
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

            return template.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                   (template.Content?.Contains(search, StringComparison.CurrentCultureIgnoreCase) ?? false);
        };
    }

    private void WireEvents()
    {
        this.FindControl<Button>("CloseBtn")!.Click += (_, _) => CloseDialog();
        _cancelButton.Click += (_, _) => CancelOrClose();
        _clearTemplateButton.Click += (_, _) => ClearTemplate();
        _templateBox.SelectionChanged += (_, _) => SelectTemplate();
        // An empty prefix must still show the whole list, otherwise the control
        // reads as a dead text box to anyone used to the old combo box.
        _templateBox.GotFocus += (_, _) =>
        {
            if (_templateBox.IsEnabled)
                _templateBox.IsDropDownOpen = true;
        };
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

    private void SelectTemplate()
    {
        if (_state.IsInFlight || _state.IsQueued ||
            _templateBox.SelectedItem is not MessageTemplateDto template ||
            string.IsNullOrWhiteSpace(template.Content))
            return;

        _state.SelectTemplate(template);
        SetContentBoxText(_state.Content);
        ClearTransientMessages();
        _contentWasEdited = false;
        Render();
        _contentBox.Focus();
    }

    private void ClearTemplate()
    {
        if (_state.IsInFlight || _state.IsQueued)
            return;

        _state.ClearTemplate();
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
```

- [ ] **Step 2: Собрать проект**

```bash
dotnet build OrbitalSIP/OrbitalSIP.csproj -c Debug
```

Ожидается: `Build succeeded. 0 Error(s)`.

Если компилятор ругается на `MaterialIconKind.MessageTextOutline`, `AccountOutline`, `LockOutline`, `AlertCircleOutline`, `CheckCircleOutline` или `ClockOutline` — сверить имена по `Material.Icons.MaterialIconKind` и подставить существующий вариант (например `Message`, `Account`, `Lock`, `AlertCircle`, `CheckCircle`, `Clock`), в `.axaml` и в `Render()` одновременно.

- [ ] **Step 3: Прогнать все тесты**

```bash
dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj
```

Ожидается: `Failed: 0`. Наборы `SmsServiceTests`, `ActiveCallSmsContextTests`, `ActiveCallSmsLifecycleTests`, `ActiveCallSmsLaunchGuardTests`, `HistoryCallSmsContextTests`, `SmsModelsTests` должны пройти без изменений — если какой-то падает, значит задет контракт, который менять не планировалось.

- [ ] **Step 4: Коммит**

```bash
git add OrbitalSIP/Views/SmsComposeDialog.axaml.cs
git commit -m "feat(sms): wire the single-screen compose panel"
```

---

### Task 7: Проверка вживую

**Files:** нет изменений, только проверка.

- [ ] **Step 1: Собрать и запустить**

```bash
dotnet run --project OrbitalSIP/OrbitalSIP.csproj
```

Войти под оператором, поднять звонок (или открыть SMS из строки истории в `RecentsView`, если нет возможности позвонить).

- [ ] **Step 2: Пройти сценарии**

Проверить по списку:

1. Номер в карточке получателя разбит на группы, справа чип «из звонка» с иконкой замка, жёлтой эмодзи нет.
2. Клик по полю шаблона открывает список целиком; в строках видно имя и первую строку текста.
3. Ввод куска текста из тела шаблона (не имени) фильтрует список.
4. Выбор шаблона подставляет текст, курсор в конце, фокус в поле сообщения, появилась кнопка сброса.
5. Кнопка сброса убирает шаблон, текст в поле остаётся.
6. Поле сообщения занимает всю свободную высоту; пустой полосы над футером нет.
7. Пустое поле — «Отправить» приглушена; после ввода становится синей.
8. `Ctrl+Enter` отправляет сразу, без промежуточного экрана.
9. Во время отправки «Отмена» превращается в «Отменить отправку» красной обводкой, «Отправить» — в «Отправка…».
10. После успеха футер сменяется зелёной плашкой, окно закрывается само примерно через полторы секунды.
11. Отключить сеть, отправить: показывается плашка ошибки, кнопка становится «Повторить», повтор работает.
12. Завершить звонок при открытом окне — окно закрывается (`ActiveCallSmsLifecycle`).
13. Переключить язык в настройках на каждый из четырёх — новые строки на месте, `{}` и пустых подписей нет.

- [ ] **Step 3: Коммит, если по итогам проверки были правки**

```bash
git add -A
git commit -m "fix(sms): address findings from the manual compose panel pass"
```

---

## Проверка соответствия спеке

| Требование спеки | Задача |
|---|---|
| Режимы удаляются | 2, 5, 6 |
| `templateId` сбрасывается при правке текста | 2 |
| Подтверждение удаляется, `Ctrl+Enter` шлёт сразу | 2, 5, 6 |
| Автозакрытие через 1.5 с после успеха | 6 |
| Повтор после ошибки, тот же `RequestId` | 2, 6 |
| Сетка `Auto,*,Auto`, поле тянется | 5 |
| Шапка с иконкой | 5 |
| Карточка получателя с чипом «из звонка» | 5 |
| Форматирование номера | 1, 6 |
| Счётчик в строке подписи | 5, 6 |
| Приглушённая неактивная «Отправить» | 6 |
| Таблица состояний футера | 6 |
| `AutoCompleteBox`, фильтр по имени и телу, превью, `MinimumPrefixLength=0` | 5, 6 |
| Кнопка сброса шаблона | 5, 6 |
| Стили `AutoCompleteBox` для тёмной темы | 4 |
| Локализация ×4 | 3 |
| Тесты состояния | 2 |
