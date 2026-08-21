# Bottom Navigation Rework — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Сделать нижнее меню виджета управляемым из одной точки, занять мёртвый четвёртый таб экраном задач оператора и показать бейджи, которые не требуют открывать экран, чтобы узнать, что там что-то есть.

**Architecture:** `BottomNavControl` перестаёт знать, куда ведут его кнопки: он поднимает одно событие `TabSelected` и принимает своё состояние снаружи. `MainWindow` привязывает обработчик в единственном месте (`AttachNav`), вызываемом из обоих путей вставки контента, и решает переходы в единственном `NavigateTo`. Опрос бейджей владеет собой сам (`NavBadgeService`, один таймер на сессию), потому что контрол пересоздаётся при каждой навигации. Вся вычислимая логика вынесена в чистые классы под тесты.

**Tech Stack:** .NET 8 (`net8.0-windows10.0.17763`), Avalonia UI 11, Material.Icons.Avalonia, xUnit 2.5.

**Спека:** `docs/superpowers/specs/2026-08-21-bottom-nav-design.md`

**Ветка:** `feat/bottom-nav-rework` (уже создана, спека закоммичена в `70195c0`)

---

## Как гонять тесты

```bash
dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q
```

Базовая линия на момент написания плана: **501 тест, все зелёные**.

**Если сборка падает с `MSB3027` / `MSB3021`** («The process cannot access the file ... OrbitalSIP.exe») — на машине запущен виджет и держит свой exe. Тестовый проект ссылается на основной, поэтому его всё равно собирают. Либо закрой виджет, либо собери в отдельный каталог:

```bash
dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q -p:BaseOutputPath=./artifacts/test/
```

Фильтр по одному классу тестов:

```bash
dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q --filter "FullyQualifiedName~NavBadgeStateTests"
```

Сборка приложения без тестов:

```bash
dotnet build OrbitalSIP/OrbitalSIP.csproj --nologo
```

---

## Структура файлов

### Создаются

| Файл | Ответственность |
|---|---|
| `OrbitalSIP/Models/NavTab.cs` | Перечисление табов. Больше ничего. |
| `OrbitalSIP/Models/NavBadgeState.cs` | Чистая арифметика бейджей: сложение открытых, watermark пропущенных, формат счётчика. Без HTTP и без UI. |
| `OrbitalSIP/Services/NavPulse.cs` | Одна функция: пульсировать ли табу «Набор». |
| `OrbitalSIP/Services/TaskItemPresenter.cs` | Чистые вычисления для строки задачи: просрочена ли, цвет приоритета, ведро срока, текст времени. Здесь же живёт `DueBucket` — он ничего не отражает на проводе, а описывает подпись строки, как `LeadPanelState` рядом со своим презентером. |
| `OrbitalSIP/Services/NavBadgeService.cs` | Расписание опроса, HTTP, уведомление подписчиков. Считать не умеет — держит `NavBadgeState`. |
| `OrbitalSIP/ViewModels/TaskItemViewModel.cs` | Готовые к биндингу свойства строки. Склеивает `TaskItemPresenter` с `I18nService`. |
| `OrbitalSIP/Views/TasksView.axaml` + `.axaml.cs` | Экран задач. |
| `OrbitalSIP.Tests/NavBadgeStateTests.cs` | |
| `OrbitalSIP.Tests/NavPulseTests.cs` | |
| `OrbitalSIP.Tests/TaskItemPresenterTests.cs` | |
| `OrbitalSIP.Tests/TaskServiceTests.cs` | |

### Изменяются

| Файл | Что |
|---|---|
| `OrbitalSIP/Views/BottomNavControl.axaml` | `ContactsBtn` → `TasksBtn`, бейджи, стили, тултипы |
| `OrbitalSIP/Views/BottomNavControl.axaml.cs` | Одно событие вместо четырёх, свойства состояния |
| `OrbitalSIP/MainWindow.axaml.cs` | `AttachNav`, `NavigateTo`, `ShowTasks`, владение `NavBadgeService` |
| `OrbitalSIP/Views/ExpandedView.axaml.cs` | Минус два события и их проводка |
| `OrbitalSIP/Views/RecentsView.axaml.cs` | Минус два события и их проводка |
| `OrbitalSIP/Views/SettingsView.axaml.cs` | Минус `OnBackRequested` |
| `OrbitalSIP/Views/ActiveCallView.axaml` | Панель DTMF |
| `OrbitalSIP/Views/ActiveCallView.axaml.cs` | Минус три события, DTMF по месту |
| `OrbitalSIP/Views/OperatorStatsControl.axaml.cs` | Снятие собственного таймера |
| `OrbitalSIP/Services/TaskService.cs` | Внедряемый конструктор, хелпер `SendAsync`, три метода |
| `OrbitalSIP/Models/TaskModels.cs` | `TaskItem`, `TaskStats`, `TaskListResponse` |
| `OrbitalSIP/App.axaml.cs` | `NavBadges` в списке статических сервисов |
| `OrbitalSIP/Assets/i18n/{ru,uz,kk,tg}.json` | Новые ключи |

---

## Task 1: `NavTab` и `NavBadgeState`

Чистые типы без зависимостей от Avalonia. Ставим первыми — на них опирается всё остальное.

**Files:**
- Create: `OrbitalSIP/Models/NavTab.cs`
- Create: `OrbitalSIP/Models/NavBadgeState.cs`
- Test: `OrbitalSIP.Tests/NavBadgeStateTests.cs`

- [ ] **Step 1: Создать перечисление табов**

`OrbitalSIP/Models/NavTab.cs`:

```csharp
namespace OrbitalSIP.Models;

/// <summary>
/// The four slots of the bottom navigation bar.
///
/// The bar used to identify its tabs by string ("Dialer", "Recents", …), compared in
/// four places at once inside the control. A typo in any of them silently highlighted
/// nothing, which is exactly what an enum exists to prevent.
/// </summary>
public enum NavTab
{
    Dialer,
    Recents,
    Tasks,
    Settings,
}
```

- [ ] **Step 2: Написать падающие тесты**

`OrbitalSIP.Tests/NavBadgeStateTests.cs`:

```csharp
using OrbitalSIP.Models;
using Xunit;

namespace OrbitalSIP.Tests;

public class NavBadgeStateTests
{
    /// <summary>
    /// The backend reports pending and in_progress as disjoint sets — its "pending"
    /// filter is literally NOT IN ('in_progress', 'done', 'completed'). Counting only
    /// pending would put a 3 on a badge above a list of 5.
    /// </summary>
    [Fact]
    public void OpenTasksAddsPendingAndInProgress()
    {
        var state = new NavBadgeState();

        state.SetTasks(pending: 3, inProgress: 2, overdue: 0);

        Assert.Equal(5, state.OpenTasks);
    }

    /// <summary>
    /// Overdue overlaps both pending and in_progress, so adding it would double-count
    /// the same task. It only decides the colour.
    /// </summary>
    [Fact]
    public void OverdueColoursTheBadgeWithoutInflatingIt()
    {
        var state = new NavBadgeState();

        state.SetTasks(pending: 3, inProgress: 2, overdue: 2);

        Assert.Equal(5, state.OpenTasks);
        Assert.True(state.HasOverdueTasks);
    }

    [Fact]
    public void NoOverdueTasksLeavesTheBadgeUnalarmed()
    {
        var state = new NavBadgeState();

        state.SetTasks(pending: 4, inProgress: 0, overdue: 0);

        Assert.False(state.HasOverdueTasks);
    }

    [Fact]
    public void MissedCallsCountAsNewUntilRecentsIsOpened()
    {
        var state = new NavBadgeState();

        state.SetMissed(3);

        Assert.Equal(3, state.NewMissed);
    }

    [Fact]
    public void OpeningRecentsClearsTheMissedBadge()
    {
        var state = new NavBadgeState();
        state.SetMissed(3);

        state.MarkRecentsSeen();

        Assert.Equal(0, state.NewMissed);
    }

    [Fact]
    public void MissedCallsArrivingAfterRecentsWasOpenedCountAgain()
    {
        var state = new NavBadgeState();
        state.SetMissed(3);
        state.MarkRecentsSeen();

        state.SetMissed(5);

        Assert.Equal(2, state.NewMissed);
    }

    /// <summary>
    /// The counter is "missed today", so a night shift crossing midnight sees it reset
    /// to zero while the watermark still holds yesterday's number. Without re-seating
    /// the watermark, every missed call of the new day would be swallowed until the
    /// operator beat yesterday's total.
    /// </summary>
    [Fact]
    public void CounterResettingAtMidnightReseatsTheWatermark()
    {
        var state = new NavBadgeState();
        state.SetMissed(7);
        state.MarkRecentsSeen();

        state.SetMissed(0);
        Assert.Equal(0, state.NewMissed);

        state.SetMissed(1);
        Assert.Equal(1, state.NewMissed);
    }

    /// <summary>
    /// The rollover is rarely caught at exactly zero: the counter restarts at midnight
    /// and the next poll two minutes later already reports the calls missed since. Those
    /// are unseen, and reseating the watermark to the new total instead of to zero would
    /// hide every one of them.
    /// </summary>
    [Fact]
    public void CounterRestartingBelowTheWatermarkTreatsTheRemainderAsUnseen()
    {
        var state = new NavBadgeState();
        state.SetMissed(10);
        state.MarkRecentsSeen();

        state.SetMissed(3);

        Assert.Equal(3, state.NewMissed);
    }

    /// <summary>
    /// Guards the shape of the arithmetic rather than a caller: the backend should never
    /// send a negative count, but one arriving must not turn a badge into a negative
    /// number or a subtraction into an inflated one.
    /// </summary>
    [Fact]
    public void NegativePendingCountClampsToZeroInsteadOfSubtracting()
    {
        var state = new NavBadgeState();

        state.SetTasks(pending: -3, inProgress: 5, overdue: 0);

        Assert.Equal(5, state.OpenTasks);
    }

    /// <summary>
    /// One test per guard, not one table covering both: a case that makes only pending
    /// negative leaves the inProgress clamp free to be deleted unnoticed, which is
    /// exactly what happened the first time this was written.
    /// </summary>
    [Fact]
    public void NegativeInProgressCountClampsToZeroInsteadOfSubtracting()
    {
        var state = new NavBadgeState();

        state.SetTasks(pending: 1, inProgress: -5, overdue: 0);

        Assert.Equal(1, state.OpenTasks);
    }

    [Fact]
    public void NegativeMissedCountIsIgnored()
    {
        var state = new NavBadgeState();

        state.SetMissed(-4);

        Assert.Equal(0, state.NewMissed);
    }

    /// <summary>Each poll replaces the totals; they do not accumulate across calls.</summary>
    [Fact]
    public void SecondPollReplacesTheTaskTotalsRatherThanAddingToThem()
    {
        var state = new NavBadgeState();
        state.SetTasks(pending: 3, inProgress: 2, overdue: 1);

        state.SetTasks(pending: 1, inProgress: 0, overdue: 0);

        Assert.Equal(1, state.OpenTasks);
        Assert.False(state.HasOverdueTasks);
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "1")]
    [InlineData(9, "9")]
    [InlineData(10, "9+")]
    [InlineData(250, "9+")]
    [InlineData(-1, "")]
    public void CountIsFormattedForAnEighteenPixelPill(int count, string expected)
    {
        Assert.Equal(expected, NavBadgeState.FormatCount(count));
    }

    [Fact]
    public void FreshStateShowsNothing()
    {
        var state = new NavBadgeState();

        Assert.Equal(0, state.OpenTasks);
        Assert.Equal(0, state.NewMissed);
        Assert.False(state.HasOverdueTasks);
    }
}
```

- [ ] **Step 3: Прогнать — убедиться, что падает на компиляции**

```bash
dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q --filter "FullyQualifiedName~NavBadgeStateTests"
```

Ожидаемо: FAIL, `error CS0246: The type or namespace name 'NavBadgeState' could not be found`.

- [ ] **Step 4: Реализовать**

`OrbitalSIP/Models/NavBadgeState.cs`:

```csharp
using System;

namespace OrbitalSIP.Models;

/// <summary>
/// The numbers behind the bottom-nav badges, and nothing else — no HTTP, no timer, no
/// control. <see cref="Services.NavBadgeService"/> owns the polling and hands the
/// answers here; this type decides what they mean.
/// </summary>
public sealed class NavBadgeState
{
    private int _missedCalls;
    private int _seenMissed;

    /// <summary>
    /// Tasks the operator still has to deal with.
    ///
    /// pending + inProgress, because the backend's "pending" filter excludes
    /// in_progress outright (NOT IN ('in_progress', 'done', 'completed')). Overdue is
    /// deliberately absent: it overlaps both buckets, so adding it would count the same
    /// task twice.
    /// </summary>
    public int OpenTasks { get; private set; }

    /// <summary>True when at least one open task is past its due date.</summary>
    public bool HasOverdueTasks { get; private set; }

    /// <summary>Missed calls the operator has not looked at since last opening Recents.</summary>
    public int NewMissed => Math.Max(0, _missedCalls - _seenMissed);

    public void SetTasks(int pending, int inProgress, int overdue)
    {
        OpenTasks = Math.Max(0, pending) + Math.Max(0, inProgress);
        HasOverdueTasks = overdue > 0;
    }

    /// <summary>
    /// Records the backend's "missed today" total.
    ///
    /// A total below the previous one can only mean the counter restarted at midnight —
    /// calls are never un-missed. Everything it reports after a restart is therefore
    /// unseen, which is why the watermark goes to zero and not to the new total:
    /// reseating it to the total would swallow any call missed between the rollover and
    /// this poll, and at a two-minute interval that gap is ordinary, not exotic.
    /// </summary>
    public void SetMissed(int missedCalls)
    {
        var value = Math.Max(0, missedCalls);
        if (value < _missedCalls) _seenMissed = 0;
        _missedCalls = value;
    }

    public void MarkRecentsSeen() => _seenMissed = _missedCalls;

    /// <summary>Badge text. Empty means "draw nothing"; the pill is 18px and holds two glyphs.</summary>
    public static string FormatCount(int count) =>
        count <= 0 ? string.Empty :
        count > 9  ? "9+" :
        count.ToString();
}
```

- [ ] **Step 5: Прогнать — зелёные**

```bash
dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q --filter "FullyQualifiedName~NavBadgeStateTests"
```

Ожидаемо: PASS, 19 тестов.

- [ ] **Step 6: Коммит**

```bash
git add OrbitalSIP/Models/NavTab.cs OrbitalSIP/Models/NavBadgeState.cs OrbitalSIP.Tests/NavBadgeStateTests.cs
git commit -m "feat(nav): add NavTab and the badge arithmetic behind it"
```

---

## Task 2: `NavPulse`

**Files:**
- Create: `OrbitalSIP/Services/NavPulse.cs`
- Test: `OrbitalSIP.Tests/NavPulseTests.cs`

- [ ] **Step 1: Написать падающий тест**

`OrbitalSIP.Tests/NavPulseTests.cs`:

```csharp
using OrbitalSIP.Models;
using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

public class NavPulseTests
{
    [Theory]
    [InlineData(NavTab.Recents)]
    [InlineData(NavTab.Tasks)]
    [InlineData(NavTab.Settings)]
    public void CallRunningOffScreenDrawsTheOperatorBack(NavTab currentTab)
    {
        Assert.True(NavPulse.ShouldPulse(inCall: true, currentTab));
    }

    /// <summary>
    /// The call screen is already in front of the operator, so there is nothing to draw
    /// their eye to. Animating anyway would repaint a transparent topmost window for the
    /// length of every call, which is the cost WidgetPulse exists to avoid.
    /// </summary>
    [Fact]
    public void CallScreenItselfDoesNotPulse()
    {
        Assert.False(NavPulse.ShouldPulse(inCall: true, NavTab.Dialer));
    }

    [Theory]
    [InlineData(NavTab.Dialer)]
    [InlineData(NavTab.Recents)]
    [InlineData(NavTab.Tasks)]
    [InlineData(NavTab.Settings)]
    public void NoCallNeverPulses(NavTab currentTab)
    {
        Assert.False(NavPulse.ShouldPulse(inCall: false, currentTab));
    }
}
```

- [ ] **Step 2: Прогнать — падает**

```bash
dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q --filter "FullyQualifiedName~NavPulseTests"
```

Ожидаемо: FAIL, `error CS0103: The name 'NavPulse' does not exist`.

- [ ] **Step 3: Реализовать**

`OrbitalSIP/Services/NavPulse.cs`:

```csharp
using OrbitalSIP.Models;

namespace OrbitalSIP.Services;

/// <summary>
/// Decides whether the Dialer tab animates while a call is up.
///
/// Sibling of <see cref="WidgetPulse"/> and written for the same reason: an animation
/// that never stops keeps a transparent, topmost window repainting for no one. The tab
/// only breathes when the operator has navigated away from the call, which is the one
/// moment "tap here to get back" is worth saying out loud.
/// </summary>
public static class NavPulse
{
    public static bool ShouldPulse(bool inCall, NavTab currentTab) =>
        inCall && currentTab != NavTab.Dialer;
}
```

- [ ] **Step 4: Прогнать — зелёные**

```bash
dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q --filter "FullyQualifiedName~NavPulseTests"
```

Ожидаемо: PASS, 8 тестов.

- [ ] **Step 5: Коммит**

```bash
git add OrbitalSIP/Services/NavPulse.cs OrbitalSIP.Tests/NavPulseTests.cs
git commit -m "feat(nav): decide the in-call tab pulse in one testable place"
```

---

## Task 3: Переписать `BottomNavControl`

> **Как оно легло на самом деле.** Задачи 3 и 4 выполнены; код ниже — исходное задание, а
> не описание того, что в репозитории. Три вещи здесь были неверны и исправлены по
> измерению (коммит `2296e79`), ещё четыре пришли из ревью и меняют форму решения:
>
> - `_currentTab` **не присваивается вручную**. Он выводится из типа контента в
>   `AttachNav` — десять ручных присваиваний были той же схемой отказа «каждый экран
>   должен не забыть», ради устранения которой затевался рефакторинг.
> - `_settingsFromLogin` **не гасится вручную**. `AttachNav` делает
>   `_settingsFromLogin &= content is Views.SettingsView`, потому что флаг описывает один
>   экран, а ручные сбросы закрывали один выход из четырёх — панель активного звонка
>   наследовала login-режим, и любое нажатие таба выбрасывало на логин посреди разговора.
> - `NavigateTo` начинается со стража `if (tab == _currentTab) return;`. Без него нажатие
>   уже активного таба пересобирало экран и молча теряло несохранённые настройки SIP.
> - Выбор иконки первого слота вынесен в чистый `OrbitalSIP/Services/NavTabIcon.cs` рядом
>   с `NavPulse` и покрыт тестами; login-режим выигрывает у состояния звонка во всех
>   четырёх решениях, а метод называется `RefreshTabVisuals`, а не `RefreshInCallVisuals`.
>
> Смотреть по коммитам `74d92c9`, `a0a3934`, `bdafa79`, `ee399e8`.


UI, тестами не покрывается — проверяется сборкой и прогоном приложения на Task 4.

**Files:**
- Modify: `OrbitalSIP/Views/BottomNavControl.axaml` (полная замена)
- Modify: `OrbitalSIP/Views/BottomNavControl.axaml.cs` (полная замена)

- [ ] **Step 1: Заменить разметку**

`OrbitalSIP/Views/BottomNavControl.axaml` целиком:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:i18n="clr-namespace:OrbitalSIP.Services"
             xmlns:materialIcons="clr-namespace:Material.Icons.Avalonia;assembly=Material.Icons.Avalonia"
             x:Class="OrbitalSIP.Views.BottomNavControl">

  <UserControl.Styles>
    <!-- The active/inactive look used to be four pairs of hand-written Opacity and
         SolidColorBrush assignments in code-behind. Here it is once. -->
    <Style Selector="Button.nav-tab">
      <Setter Property="Background" Value="Transparent" />
      <Setter Property="BorderThickness" Value="0" />
      <Setter Property="Padding" Value="0" />
      <Setter Property="Cursor" Value="Hand" />
      <Setter Property="HorizontalAlignment" Value="Stretch" />
      <Setter Property="VerticalAlignment" Value="Stretch" />
      <Setter Property="HorizontalContentAlignment" Value="Center" />
      <Setter Property="VerticalContentAlignment" Value="Center" />
      <Setter Property="Opacity" Value="0.65" />
    </Style>

    <Style Selector="Button.nav-tab:pointerover">
      <Setter Property="Opacity" Value="0.85" />
    </Style>

    <Style Selector="Button.nav-tab.active">
      <Setter Property="Opacity" Value="1.0" />
    </Style>

    <Style Selector="Button.nav-tab:disabled">
      <Setter Property="Opacity" Value="0.28" />
    </Style>

    <!-- Breathes the tab while a call runs off-screen. A looping animation has to be
         declared on a style rather than started from code: Avalonia 11's
         Animation.RunAsync rejects IterationCount.Infinite outright, and Animation.Apply
         — the call the style system makes here — is internal. The class is the switch,
         and removing it is what stops the animation; see BottomNavControl.SetPulse. -->
    <Style Selector="Button.nav-tab.pulse">
      <Style.Animations>
        <Animation Duration="0:0:1.4" IterationCount="INFINITE" Easing="SineEaseInOut">
          <KeyFrame Cue="0%">
            <Setter Property="Opacity" Value="1.0" />
          </KeyFrame>
          <KeyFrame Cue="50%">
            <Setter Property="Opacity" Value="0.45" />
          </KeyFrame>
          <KeyFrame Cue="100%">
            <Setter Property="Opacity" Value="1.0" />
          </KeyFrame>
        </Animation>
      </Style.Animations>
    </Style>

    <Style Selector="Button.nav-tab materialIcons|MaterialIcon">
      <Setter Property="Foreground" Value="#8AA0B8" />
    </Style>

    <Style Selector="Button.nav-tab.active materialIcons|MaterialIcon">
      <Setter Property="Foreground" Value="#60A5FA" />
    </Style>

    <Style Selector="Button.nav-tab.in-call materialIcons|MaterialIcon">
      <Setter Property="Foreground" Value="#22C55E" />
    </Style>

    <Style Selector="Border.nav-badge">
      <Setter Property="Background" Value="#3B82F6" />
      <Setter Property="CornerRadius" Value="9" />
      <Setter Property="MinWidth" Value="18" />
      <Setter Property="Height" Value="18" />
      <Setter Property="Padding" Value="4,0" />
      <Setter Property="HorizontalAlignment" Value="Right" />
      <Setter Property="VerticalAlignment" Value="Top" />
      <Setter Property="Margin" Value="0,-6,-8,0" />
    </Style>

    <Style Selector="Border.nav-badge.alert">
      <Setter Property="Background" Value="#EF4444" />
    </Style>
  </UserControl.Styles>

  <Border Background="#0F172A" Height="46" BorderBrush="#1E293B" BorderThickness="1,1,1,0" VerticalAlignment="Bottom">
    <Grid ColumnDefinitions="*,*,*,*">

      <Button Grid.Column="0" Name="DialerBtn" Classes="nav-tab active" ToolTip.Tip="{i18n:I18n Dialer}">
        <materialIcons:MaterialIcon Name="DialerIcon" Kind="Dialpad" Width="20" Height="20" />
      </Button>

      <Button Grid.Column="1" Name="RecentsBtn" Classes="nav-tab" ToolTip.Tip="{i18n:I18n Recents}">
        <Panel>
          <materialIcons:MaterialIcon Name="RecentsIcon" Kind="ClockOutline" Width="22" Height="22" />
          <Border Name="RecentsBadge" Classes="nav-badge" IsVisible="False">
            <TextBlock Name="RecentsBadgeText" Text="" FontSize="10" FontWeight="Bold"
                       Foreground="#FFFFFF" HorizontalAlignment="Center" VerticalAlignment="Center" />
          </Border>
        </Panel>
      </Button>

      <Button Grid.Column="2" Name="TasksBtn" Classes="nav-tab" ToolTip.Tip="{i18n:I18n Tasks}">
        <Panel>
          <materialIcons:MaterialIcon Name="TasksIcon" Kind="FormatListChecks" Width="22" Height="22" />
          <Border Name="TasksBadge" Classes="nav-badge" IsVisible="False">
            <TextBlock Name="TasksBadgeText" Text="" FontSize="10" FontWeight="Bold"
                       Foreground="#FFFFFF" HorizontalAlignment="Center" VerticalAlignment="Center" />
          </Border>
        </Panel>
      </Button>

      <Button Grid.Column="3" Name="SettingsBtn" Classes="nav-tab" ToolTip.Tip="{i18n:I18n Settings}">
        <Panel>
          <materialIcons:MaterialIcon Name="SettingsIcon" Kind="CogOutline" Width="22" Height="22" />
          <!-- Update available indicator dot -->
          <Ellipse Name="UpdateDot" Width="8" Height="8"
                   Fill="#17E0A0"
                   HorizontalAlignment="Right" VerticalAlignment="Top"
                   Margin="0,0,-1,-1"
                   IsVisible="False" />
        </Panel>
      </Button>

    </Grid>
  </Border>
</UserControl>
```

- [ ] **Step 2: Заменить code-behind**

`OrbitalSIP/Views/BottomNavControl.axaml.cs` целиком:

```csharp
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using System;
using System.Collections.Generic;
using Material.Icons;
using Material.Icons.Avalonia;
using OrbitalSIP.Models;
using OrbitalSIP.Services;

namespace OrbitalSIP.Views
{
    /// <summary>
    /// The bottom tab bar. It reports which tab was pressed and draws the state it is
    /// given; it does not know what any tab leads to.
    ///
    /// It used to raise four separate OnXxxRequested events, and each screen wired up
    /// whichever subset its author remembered. Settings never wired Recents, the call
    /// screen never wired the dialer, and Contacts was wired by nobody at all. Routing
    /// now lives in MainWindow.NavigateTo, in one switch nobody can partially implement.
    /// </summary>
    public partial class BottomNavControl : UserControl
    {
        /// <summary>Raised on every tab press, including a press on the active tab.</summary>
        public event EventHandler<NavTab>? TabSelected;

        private readonly Dictionary<NavTab, Button> _buttons = new();
        private NavTab _activeTab = NavTab.Dialer;
        private bool _inCall;

        public BottomNavControl()
        {
            InitializeComponent();
            WireButtons();

            // Show dot immediately if the silent startup check already found an update.
            if (App.Updater.HasUpdate)
                ShowUpdateDot(true);

            // Show dot if the update is discovered while this control is on screen.
            App.Updater.UpdateAvailable += OnUpdateAvailable;
        }

        /// <summary>
        /// Releases the subscription above. App.Updater lives for the whole process, and
        /// MainWindow builds a fresh view — and therefore a fresh one of these — on every
        /// screen change, so without this each navigation pinned an entire control tree to
        /// a static event for the rest of the shift. That is hundreds over a shift, and the
        /// active-call panel among them holds the caller's lead, name and number in its own
        /// fields, which ForgetCachedCall does not reach.
        /// </summary>
        protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            App.Updater.UpdateAvailable -= OnUpdateAvailable;
            base.OnDetachedFromVisualTree(e);
        }

        /// <summary>
        /// The animated screen swap parents a view into OverlayHost and then moves it to
        /// Host — a detach/attach pair — so the detach above is not always the end of this
        /// control's life. Re-subscribe, and keep it idempotent: -= on an absent handler is
        /// a no-op, but += twice would fire twice.
        /// </summary>
        protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            App.Updater.UpdateAvailable -= OnUpdateAvailable;
            App.Updater.UpdateAvailable += OnUpdateAvailable;
            if (App.Updater.HasUpdate) ShowUpdateDot(true);
        }

        private void OnUpdateAvailable()
        {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => ShowUpdateDot(true));
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void WireButtons()
        {
            Register(NavTab.Dialer,   "DialerBtn");
            Register(NavTab.Recents,  "RecentsBtn");
            Register(NavTab.Tasks,    "TasksBtn");
            Register(NavTab.Settings, "SettingsBtn");

            void Register(NavTab tab, string name)
            {
                var button = this.FindControl<Button>(name);
                if (button == null) return;
                _buttons[tab] = button;
                button.Click += (_, __) => TabSelected?.Invoke(this, tab);
            }
        }

        /// <summary>Which tab reads as current. Set by MainWindow, never inferred here.</summary>
        public NavTab ActiveTab
        {
            get => _activeTab;
            set
            {
                _activeTab = value;
                foreach (var (tab, button) in _buttons)
                    button.Classes.Set("active", tab == value);
                RefreshInCallVisuals();
            }
        }

        /// <summary>
        /// Swaps the Dialer tab to a "you are on a call" affordance.
        ///
        /// ShowDialer() has always redirected to the call screen while a call is up, so the
        /// tab already meant "back to the call" — it just never said so, and the highlight
        /// claimed the operator was looking at a dialpad they could not reach.
        /// </summary>
        public void SetInCall(bool inCall)
        {
            _inCall = inCall;
            RefreshInCallVisuals();
        }

        private void RefreshInCallVisuals()
        {
            if (!_buttons.TryGetValue(NavTab.Dialer, out var dialerBtn)) return;
            var icon = this.FindControl<MaterialIcon>("DialerIcon");

            dialerBtn.Classes.Set("in-call", _inCall);
            if (icon != null)
                icon.Kind = _inCall ? MaterialIconKind.PhoneInTalk : MaterialIconKind.Dialpad;

            ToolTip.SetTip(dialerBtn, I18nService.Instance.Get(_inCall ? "NavInCall" : "Dialer"));

            SetPulse(dialerBtn, NavPulse.ShouldPulse(_inCall, _activeTab));
        }

        /// <summary>
        /// Breathes the tab while a call runs off-screen.
        ///
        /// A Transition would be wrong here: it animates a value that changes, and this
        /// value does not. It takes an Animation with an infinite iteration count, and in
        /// Avalonia 11 only a style can start one of those — Animation.RunAsync throws on
        /// IterationCount.Infinite ("Looping animations must not use the Run method") and
        /// Animation.Apply, the call the style system makes on its behalf, is internal.
        ///
        /// So the class is the switch, and clearing it is what stops the animation: left
        /// running it keeps the compositor busy on a window that is otherwise perfectly
        /// still. The resting opacity comes back from the styles rather than from an
        /// assignment here, which is why a local Opacity is never written — one would
        /// outrank .active, :disabled and :pointerover for the rest of this control's life.
        /// </summary>
        private static void SetPulse(Button button, bool pulse) =>
            button.Classes.Set("pulse", pulse);

        /// <summary>
        /// Disables what a signed-out operator cannot reach.
        ///
        /// Settings is reachable from the login screen, and from there Recents, Tasks and
        /// the dialer all lead nowhere. The Dialer slot becomes a back arrow instead;
        /// MainWindow routes any tab press back to login while this is on.
        /// </summary>
        public void SetLoginMode(bool loginMode)
        {
            if (_buttons.TryGetValue(NavTab.Recents, out var recents)) recents.IsEnabled = !loginMode;
            if (_buttons.TryGetValue(NavTab.Tasks, out var tasks)) tasks.IsEnabled = !loginMode;

            // RefreshInCallVisuals owns the icon, so leaving login mode restores whichever
            // of Dialpad/PhoneInTalk is right. Setting the arrow here and nothing there
            // would leave the icon depending on the order AttachNav happens to call these.
            RefreshInCallVisuals();

            var icon = this.FindControl<MaterialIcon>("DialerIcon");
            if (icon != null && loginMode) icon.Kind = MaterialIconKind.ArrowLeft;
        }

        /// <summary>Shows the count pill on a tab. Zero or less hides it.</summary>
        public void SetBadge(NavTab tab, int count, bool alert)
        {
            var (badgeName, textName) = tab switch
            {
                NavTab.Recents => ("RecentsBadge", "RecentsBadgeText"),
                NavTab.Tasks   => ("TasksBadge", "TasksBadgeText"),
                _              => (string.Empty, string.Empty),
            };
            if (badgeName.Length == 0) return;

            var badge = this.FindControl<Border>(badgeName);
            var text = this.FindControl<TextBlock>(textName);
            if (badge == null || text == null) return;

            var label = NavBadgeState.FormatCount(count);
            text.Text = label;
            badge.Classes.Set("alert", alert);
            badge.IsVisible = label.Length > 0;
        }

        /// <summary>Show or hide the green update-available dot on the Settings button.</summary>
        public void ShowUpdateDot(bool visible)
        {
            var dot = this.FindControl<Ellipse>("UpdateDot");
            if (dot != null) dot.IsVisible = visible;
        }
    }
}
```

- [ ] **Step 3: Добавить ключи локализации, без которых тултипы покажут сырые имена**

В каждый из `OrbitalSIP/Assets/i18n/{ru,uz,kk,tg}.json` добавить перед закрывающей скобкой (`Dialer` там уже есть, не дублировать):

`ru.json`:
```json
  "Recents": "История",
  "Tasks": "Задачи",
  "Settings": "Настройки",
  "NavInCall": "Идёт разговор",
```

`uz.json`:
```json
  "Recents": "Tarix",
  "Tasks": "Vazifalar",
  "Settings": "Sozlamalar",
  "NavInCall": "Suhbat davom etmoqda",
```

`kk.json`:
```json
  "Recents": "Тарих",
  "Tasks": "Тапсырмалар",
  "Settings": "Параметрлер",
  "NavInCall": "Сөйлесу жүріп жатыр",
```

`tg.json`:
```json
  "Recents": "Таърих",
  "Tasks": "Вазифаҳо",
  "Settings": "Танзимот",
  "NavInCall": "Сӯҳбат идома дорад",
```

Ключ называется `NavInCall`, а не `InCall`: `InCall` уже занят во всех четырёх файлах под подпись статуса активного звонка («В РАЗГОВОРЕ»). Дубликат не падает — `JsonSerializer.Deserialize<Dictionary<string,string>>` берёт последнее значение молча, — поэтому переиспользование ключа не сломалось бы с ошибкой, а тихо переименовало бы ту подпись. Перед добавлением любого ключа проверить, что его ещё нет:

```bash
grep -n '"Settings"\|"Recents"\|"Tasks"\|"NavInCall"' OrbitalSIP/Assets/i18n/*.json
```

- [ ] **Step 4: Собрать — ожидаются ошибки в четырёх экранах**

```bash
dotnet build OrbitalSIP/OrbitalSIP.csproj --nologo
```

Ожидаемо: FAIL. `error CS1061: 'BottomNavControl' does not contain a definition for 'OnSettingsRequested'` и подобные — в `ExpandedView.axaml.cs`, `RecentsView.axaml.cs`, `SettingsView.axaml.cs`, `ActiveCallView.axaml.cs`. Это ожидаемо: подписчиков сносит Task 4. Не коммитить, пока не собирается — переходить сразу к Task 4.

---

## Task 4: Навигация в одной точке и чистка экранов

Заканчивает то, что Task 3 сломал намеренно. Коммит один на обе задачи — промежуточное состояние не собирается.

**Files:**
- Modify: `OrbitalSIP/MainWindow.axaml.cs`
- Modify: `OrbitalSIP/Views/ExpandedView.axaml.cs:29-33,140-142`
- Modify: `OrbitalSIP/Views/RecentsView.axaml.cs:26-27,74-80`
- Modify: `OrbitalSIP/Views/SettingsView.axaml.cs:327-332,410`
- Modify: `OrbitalSIP/Views/ActiveCallView.axaml.cs:331-340,1168-1170`

- [ ] **Step 1: Снять навигацию с `ExpandedView`**

В `OrbitalSIP/Views/ExpandedView.axaml.cs` заменить блок в `WireButtons`:

```csharp
            var bottomNav = this.FindControl<BottomNavControl>("BottomNav");
            if (bottomNav != null) bottomNav.OnSettingsRequested += (_, __) => OnSettingsRequested?.Invoke(this, EventArgs.Empty);
            if (bottomNav != null) bottomNav.OnRecentsRequested += (_, __) => OnRecentsRequested?.Invoke(this, EventArgs.Empty);
            bottomNav?.SetActiveTab("Dialer");
            BindAsync("CopyBtn", CopyDisplayedNumberAsync);
```

на:

```csharp
            BindAsync("CopyBtn", CopyDisplayedNumberAsync);
```

И удалить из блока событий внизу файла две строки:

```csharp
        public event System.EventHandler?        OnSettingsRequested;
        public event System.EventHandler?        OnRecentsRequested;
```

- [ ] **Step 2: Снять навигацию с `RecentsView`**

В `OrbitalSIP/Views/RecentsView.axaml.cs` заменить в `WireButtons`:

```csharp
            var bottomNav = this.FindControl<BottomNavControl>("BottomNav");
            if (bottomNav != null)
            {
                bottomNav.OnSettingsRequested += (_, __) => OnSettingsRequested?.Invoke(this, EventArgs.Empty);
                bottomNav.OnDialerRequested += (_, __) => OnDialerRequested?.Invoke(this, EventArgs.Empty);
                bottomNav.SetActiveTab("Recents");
            }
```

на пустоту (удалить блок целиком). И удалить два объявления событий:

```csharp
        public event EventHandler? OnSettingsRequested;
        public event EventHandler? OnDialerRequested;
```

- [ ] **Step 3: Снять навигацию с `SettingsView`**

В `OrbitalSIP/Views/SettingsView.axaml.cs` заменить:

```csharp
            var bottomNav = this.FindControl<BottomNavControl>("BottomNav");
            if (bottomNav != null)
            {
                bottomNav.OnDialerRequested += (_, __) => OnBackRequested?.Invoke(this, System.EventArgs.Empty);
                bottomNav.SetActiveTab("Settings");
            }
```

на пустоту. И удалить объявление:

```csharp
        public event System.EventHandler? OnBackRequested;
```

- [ ] **Step 4: Снять навигацию с `ActiveCallView`**

В `OrbitalSIP/Views/ActiveCallView.axaml.cs` заменить:

```csharp
            var bottomNav = this.FindControl<BottomNavControl>("BottomNav");
            if (bottomNav != null)
            {
                bottomNav.OnSettingsRequested += (_, __) => OnSettingsRequested?.Invoke(this, EventArgs.Empty);
                // Recents was wired at the MainWindow end (WireActiveCallView subscribes
                // to it) but never raised here, so the button in this panel's bottom nav
                // did nothing at all — the compiler had been reporting it as CS0067 on an
                // event with no publisher. ExpandedView forwards it the same way.
                bottomNav.OnRecentsRequested += (_, __) => OnRecentsRequested?.Invoke(this, EventArgs.Empty);
            }
            if (copy != null)
```

на:

```csharp
            if (copy != null)
```

И удалить два объявления событий (`OnKeypadRequested` пока оставить — им займётся Task 9):

```csharp
        public event EventHandler?        OnSettingsRequested;
        public event EventHandler?        OnRecentsRequested;
```

- [ ] **Step 5: Добавить в `MainWindow` точку привязки и маршрутизацию**

В `OrbitalSIP/MainWindow.axaml.cs` добавить в начало using-ов, если их нет:

```csharp
using Avalonia.VisualTree;
using OrbitalSIP.Models;
```

Рядом с прочими полями окна:

```csharp
        /// <summary>
        /// Which tab the bottom bar should read as current. Held here rather than asked of
        /// the control, because the control is rebuilt on every screen change and would
        /// have nothing to remember it with.
        /// </summary>
        private NavTab _currentTab = NavTab.Dialer;

        /// <summary>
        /// True while Settings is open from the login screen. Nothing else is reachable
        /// without a session, so every tab press goes back to login instead.
        /// </summary>
        private bool _settingsFromLogin;
```

Добавить методы рядом с `SetMainContent`:

```csharp
        /// <summary>
        /// Hands a freshly built screen's bottom bar its handler and its state.
        ///
        /// Called from both places content reaches the window — SetMainContent and
        /// CompleteAnimatedContentSwap. Missing either one leaves a bar whose buttons do
        /// nothing, which is the whole class of bug this replaced.
        ///
        /// No leak: the reference runs from the control to this window, and the control is
        /// discarded with its screen. That is the opposite direction from the static
        /// App.Updater subscription the control has to unhook by hand.
        /// </summary>
        private void AttachNav(object? content)
        {
            if (content is not Control control) return;
            var nav = control.FindLogicalDescendantOfType<Views.BottomNavControl>();
            if (nav == null) return;      // Widget, Login and Incoming have no bottom bar

            nav.TabSelected += OnNavTabSelected;
            nav.ActiveTab = _currentTab;
            nav.SetInCall(App.SipService.State is CallState.Active or CallState.OnHold);
            nav.SetLoginMode(_settingsFromLogin);
        }

        /// <summary>
        /// The bottom bar of whatever is on screen, or null for the screens that have none
        /// (Widget, Login, Incoming).
        /// </summary>
        private Views.BottomNavControl? CurrentNav() =>
            (this.FindControl<ContentControl>("Host")?.Content as Control)
                ?.FindLogicalDescendantOfType<Views.BottomNavControl>();

        /// <summary>
        /// Re-tells the bar whether a call is up.
        ///
        /// AttachNav answers that question once, when a screen is built. Settings is the
        /// screen that outlives the answer: OnCallStateChanged deliberately leaves it in
        /// place when a call ends, so without this the tab kept advertising a call that was
        /// over — and kept an infinite animation running on an idle window.
        /// </summary>
        private void RefreshNavCallState() =>
            CurrentNav()?.SetInCall(App.SipService.State is CallState.Active or CallState.OnHold);

        private void OnNavTabSelected(object? sender, NavTab tab) => NavigateTo(tab);

        /// <summary>The only place a tab press turns into a screen.</summary>
        private void NavigateTo(NavTab tab)
        {
            if (_settingsFromLogin)
            {
                ShowLogin();
                return;
            }

            _currentTab = tab;

            switch (tab)
            {
                case NavTab.Dialer:   ShowDialer();   break;
                case NavTab.Recents:  ShowRecents();  break;
                case NavTab.Tasks:    ShowTasks();    break;
                case NavTab.Settings: ShowSettings(); break;
            }
        }
```

- [ ] **Step 6: Вызвать `AttachNav` из обоих путей вставки контента**

В `SetMainContent` заменить:

```csharp
            if (host != null) { host.Content = content; host.Opacity = 1; }
            if (overlay != null) { overlay.Content = null; overlay.Opacity = 0; }
            _pendingContent = null;
```

на:

```csharp
            if (host != null) { host.Content = content; host.Opacity = 1; }
            if (overlay != null) { overlay.Content = null; overlay.Opacity = 0; }
            _pendingContent = null;
            AttachNav(content);
```

В `CompleteAnimatedContentSwap` заменить:

```csharp
            if (host != null && nextContent != null) { host.Content = nextContent; host.Opacity = 1; }
            else if (host != null) host.Opacity = 1;
            if (overlay != null) { overlay.Opacity = 0; overlay.IsVisible = false; }
            _pendingContent = null;
```

на:

```csharp
            if (host != null && nextContent != null) { host.Content = nextContent; host.Opacity = 1; }
            else if (host != null) host.Opacity = 1;
            if (overlay != null) { overlay.Opacity = 0; overlay.IsVisible = false; }
            _pendingContent = null;
            AttachNav(nextContent);
```

- [ ] **Step 7: Убрать снятые подписки из `MainWindow` и завести флаг логина**

`ShowRecents` — удалить две строки и отметить таб:

```csharp
        private void ShowRecents()
        {
            var r = new Views.RecentsView();
            r.OnCloseRequested += (_, __) => ToggleExpanded();
            r.OnExitAppRequested += (_, __) => ShutdownApp();
            r.OutgoingCallRequested += (sender, num) => StartOutgoingCall(num);

            _currentTab = NavTab.Recents;
            SetMainContent(r);
        }
```

`ShowSettings` — снять `OnBackRequested`, выставлять и сбрасывать флаг:

```csharp
        private void ShowSettings(bool isFromLogin = false)
        {
            _settingsFromLogin = isFromLogin;
            _currentTab = NavTab.Settings;

            var settingsView = new Views.SettingsView();
            settingsView.OnMinimizeRequested += (_, __) => CollapseWidget();
            settingsView.OnExitAppRequested += (_, __) => ShutdownApp();
            settingsView.OnAvatarClicked += (_, __) => ShowStatusPopup();
            settingsView.OnSaveRequested += (_, __) =>
            {
                var settings = SipSettings.Load();
                var current = App.SipService.CurrentSettings;
                // Every session-scoped field, not a hand-maintained subset of them — the
                // inline list this replaced had already gone stale against RefreshToken.
                if (!string.IsNullOrEmpty(current.Username))
                    settings.CopySessionFrom(current);

                // Before the view swap below: the screens that follow are all sized from
                // _uiScale, so changing it afterwards would leave them in a window built
                // for the old scale until the next expand or collapse.
                RescaleWindow(settings.WidgetScalePercent);

                if (isFromLogin) ShowLogin();
                else
                {
                    App.SipService.Start(settings);
                    ShowDialer();
                }
            };
            SetMainContent(settingsView);
        }
```

`ShowLogin` — сбросить флаг, иначе после возврата с настроек он остался бы взведён на всю сессию:

```csharp
        private void ShowLogin()
        {
            _settingsFromLogin = false;
            _currentTab = NavTab.Dialer;

            var login = new Views.LoginView();
            login.OnLoginSuccess += (_, __) =>
            {
                _isExpanded = false;
                _preferredMode = PreferredMode.Widget;
                StartAnimation(Width, Height, WidgetSize, WidgetSize, new Views.WidgetView());
            };
            login.OnSettingsRequested += (_, __) => ShowSettings(isFromLogin: true);
            SetMainContent(login);
        }
```

`ShowDialer` — отметить таб:

```csharp
        private void ShowDialer()
        {
            _currentTab = NavTab.Dialer;

            if (App.SipService.State == CallState.Active || App.SipService.State == CallState.OnHold)
            {
                var elapsed = App.SipService.ActiveCallStartedAt.HasValue
                    ? DateTime.Now - App.SipService.ActiveCallStartedAt.Value
                    : TimeSpan.Zero;
                ShowActiveCallView(App.SipService.ActiveCallerId, elapsed);
            }
            else
            {
                SetMainContent(CreateDialerView());
            }
        }
```

`CreateDialerView` — удалить строки:

```csharp
            dialer.OnSettingsRequested += (_, __) => ShowSettings();
            dialer.OnRecentsRequested += (_, __) => ShowRecents();
```

`WireActiveCallView` — удалить строки:

```csharp
            callView.OnSettingsRequested += (_, __) => ShowSettings();
            callView.OnRecentsRequested += (_, __) => ShowRecents();
```

`ShowActiveCallView` и `ShowActiveCallWidgetView` — в начало каждого добавить:

```csharp
            _currentTab = NavTab.Dialer;
```

`ExpandWidget` и `ReturnToPreferredMode` — то же самое. Обе открывают диалер напрямую через
`StartAnimation(..., CreateDialerView())`, минуя `ShowDialer`, поэтому без этой строки
свернуть виджет из «Истории» и развернуть обратно означало подсветить «Историю» над
диалпадом.

`CollapseWidget` — сбросить `_settingsFromLogin`. Иначе «свернуть» из настроек,
открытых до логина, и развернуть обратно даёт рабочий диалпад, у которого «История» и
«Задачи» погашены, вместо иконки набора стрелка назад, а любое нажатие уводит на логин.

`OnCallStateChanged` — вызвать `RefreshNavCallState()` при **любой** смене состояния, после
раннего выхода по истёкшей сессии. Не только в ветке `Idle`: экран настроек переживает и
начало звонка тоже, и без этого таб не позеленеет.

- [ ] **Step 8: Добавить заглушку `ShowTasks`**

Экран появится в Task 7; сейчас нужен компилируемый маршрут. Добавить рядом с `ShowRecents`:

```csharp
        private void ShowTasks()
        {
            // Real screen lands with the tasks task; until then the tab must not throw.
            _currentTab = NavTab.Tasks;
            SetMainContent(CreateDialerView());
        }
```

- [ ] **Step 9: Собрать**

```bash
dotnet build OrbitalSIP/OrbitalSIP.csproj --nologo
```

Ожидаемо: PASS, 0 errors. Предупреждения CS0067 про неиспользуемые события — не ожидаются; если появились, значит осталось объявление события, у которого больше нет ни подписчика, ни публикатора: удалить его.

- [ ] **Step 10: Прогнать весь набор тестов**

```bash
dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q
```

Ожидаемо: PASS, 537 тестов (501 базовых + 19 из Task 1 + 8 из Task 2 + 9 из `NavTabIcon`, добавленных по ревью Task 3+4).

- [ ] **Step 11: Проверить руками**

```bash
dotnet run --project OrbitalSIP/OrbitalSIP.csproj
```

Пройти по чек-листу:
1. Из «Набора» → «История» → «Настройки» → «История». Раньше последний переход был невозможен.
2. Из «Настроек» → «Набор». Возврат работает.
3. Позвонить себе; во время звонка нажать «История». Таб «Набор» стал зелёной трубкой и мигает. Нажать его — вернулись в звонок.
4. Разлогиниться, на экране логина открыть «Настройки». «История» и «Задачи» серые, иконка первого таба — стрелка назад, нажатие возвращает на логин.
5. Свернуть в виджет и развернуть обратно (анимированная смена контента) — табы по-прежнему нажимаются. Это проверка `CompleteAnimatedContentSwap`.

- [ ] **Step 12: Коммит**

```bash
git add OrbitalSIP/Views/BottomNavControl.axaml OrbitalSIP/Views/BottomNavControl.axaml.cs OrbitalSIP/MainWindow.axaml.cs OrbitalSIP/Views/ExpandedView.axaml.cs OrbitalSIP/Views/RecentsView.axaml.cs OrbitalSIP/Views/SettingsView.axaml.cs OrbitalSIP/Views/ActiveCallView.axaml.cs OrbitalSIP/Assets/i18n
git commit -m "refactor(nav): route every tab press through one switch in MainWindow"
```

---

## Task 5: Модели задач и `TaskItemPresenter`

**Files:**
- Modify: `OrbitalSIP/Models/TaskModels.cs`
- Create: `OrbitalSIP/Services/TaskItemPresenter.cs`
- Test: `OrbitalSIP.Tests/TaskItemPresenterTests.cs`

- [ ] **Step 1: Добавить модели**

В конец `OrbitalSIP/Models/TaskModels.cs`, внутри `namespace OrbitalSIP.Models`:

```csharp
    /// <summary>A row from GET /api/tasks.</summary>
    public class TaskItem
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        /// <summary>pending, in_progress, done, overdue — or null on older rows.</summary>
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        /// <summary>low, medium, high, urgent — or null.</summary>
        [JsonPropertyName("priority")]
        public string? Priority { get; set; }

        [JsonPropertyName("dueDate")]
        public DateTimeOffset? DueDate { get; set; }

        [JsonPropertyName("taskType")]
        public TaskTypeItem? TaskType { get; set; }

        [JsonPropertyName("createdAt")]
        public DateTimeOffset? CreatedAt { get; set; }
    }

    /// <summary>The envelope GET /api/tasks answers with.</summary>
    public class TaskListResponse
    {
        [JsonPropertyName("data")]
        public List<TaskItem> Data { get; set; } = new();

        [JsonPropertyName("total")]
        public int Total { get; set; }
    }

    /// <summary>GET /api/tasks/stats.</summary>
    public class TaskStats
    {
        [JsonPropertyName("total")]
        public int Total { get; set; }

        [JsonPropertyName("pending")]
        public int Pending { get; set; }

        [JsonPropertyName("inProgress")]
        public int InProgress { get; set; }

        [JsonPropertyName("done")]
        public int Done { get; set; }

        [JsonPropertyName("overdue")]
        public int Overdue { get; set; }
    }

```

И добавить в шапку файла недостающие using:

```csharp
using System;
using System.Collections.Generic;
```

- [ ] **Step 2: Написать падающие тесты**

`OrbitalSIP.Tests/TaskItemPresenterTests.cs`:

```csharp
using System;
using OrbitalSIP.Models;
using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

public class TaskItemPresenterTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 21, 14, 0, 0, TimeSpan.FromHours(5));

    private static TaskItem Task(string? status = "pending", DateTimeOffset? due = null, string? priority = null) =>
        new() { Id = 1, Title = "Перезвонить", Status = status, DueDate = due, Priority = priority };

    [Fact]
    public void TaskPastItsDueDateIsOverdue()
    {
        Assert.True(TaskItemPresenter.IsOverdue(Task(due: Now.AddHours(-1)), Now));
    }

    [Fact]
    public void TaskDueLaterIsNotOverdue()
    {
        Assert.False(TaskItemPresenter.IsOverdue(Task(due: Now.AddHours(1)), Now));
    }

    /// <summary>
    /// The backend's own predicate is a strict "dueDate < NOW()", so a task due at this
    /// exact instant is still on time. Matching it keeps the row list and the badge from
    /// disagreeing by one.
    /// </summary>
    [Fact]
    public void TaskDueExactlyNowIsNotYetOverdue()
    {
        Assert.False(TaskItemPresenter.IsOverdue(Task(due: Now), Now));
    }

    [Theory]
    [InlineData("done")]
    [InlineData("completed")]
    public void FinishedTaskIsNeverOverdue(string status)
    {
        Assert.False(TaskItemPresenter.IsOverdue(Task(status, Now.AddDays(-9)), Now));
    }

    [Fact]
    public void TaskWithNoDeadlineIsNeverOverdue()
    {
        Assert.False(TaskItemPresenter.IsOverdue(Task(due: null), Now));
    }

    [Fact]
    public void MissingDeadlineHasNoBucket()
    {
        Assert.Equal(DueBucket.None, TaskItemPresenter.Bucket(Task(due: null), Now));
    }

    [Fact]
    public void PastDeadlineBucketsAsOverdue()
    {
        Assert.Equal(DueBucket.Overdue, TaskItemPresenter.Bucket(Task(due: Now.AddMinutes(-5)), Now));
    }

    [Fact]
    public void DeadlineLaterTodayBucketsAsToday()
    {
        Assert.Equal(DueBucket.Today, TaskItemPresenter.Bucket(Task(due: Now.AddHours(2)), Now));
    }

    [Fact]
    public void DeadlineTomorrowBucketsAsTomorrow()
    {
        Assert.Equal(DueBucket.Tomorrow, TaskItemPresenter.Bucket(Task(due: Now.AddDays(1)), Now));
    }

    [Fact]
    public void DeadlineFurtherOutBucketsAsLater()
    {
        Assert.Equal(DueBucket.Later, TaskItemPresenter.Bucket(Task(due: Now.AddDays(4)), Now));
    }

    /// <summary>
    /// A finished task keeps its bucket off Overdue even with a deadline long gone —
    /// otherwise the "Все" filter would paint every closed task red.
    /// </summary>
    [Fact]
    public void FinishedTaskWithAnOldDeadlineDoesNotBucketAsOverdue()
    {
        Assert.NotEqual(DueBucket.Overdue, TaskItemPresenter.Bucket(Task("done", Now.AddDays(-3)), Now));
    }

    [Theory]
    [InlineData("urgent", "#EF4444")]
    [InlineData("high", "#F59E0B")]
    [InlineData("medium", "#60A5FA")]
    [InlineData("low", "#64748B")]
    public void PriorityPicksItsStripeColour(string priority, string expected)
    {
        Assert.Equal(expected, TaskItemPresenter.PriorityColor(priority));
    }

    /// <summary>The CRM has no enum behind this column, so casing is not guaranteed.</summary>
    [Fact]
    public void PriorityIsMatchedRegardlessOfCasing()
    {
        Assert.Equal("#EF4444", TaskItemPresenter.PriorityColor("URGENT"));
    }

    /// <summary>
    /// A stripe is always drawn, so rows keep the same width whatever the CRM puts in
    /// this column next.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("whatever-the-crm-adds-next")]
    public void UnknownPriorityFallsBackToTheQuietColour(string? priority)
    {
        Assert.Equal("#64748B", TaskItemPresenter.PriorityColor(priority));
    }

    [Fact]
    public void SameDayDeadlineShowsOnlyTheTime()
    {
        Assert.Equal("16:30", TaskItemPresenter.TimeText(Now.AddHours(2).AddMinutes(30), Now));
    }

    [Fact]
    public void DistantDeadlineShowsDayAndTime()
    {
        var due = new DateTimeOffset(2026, 9, 12, 9, 5, 0, TimeSpan.FromHours(5));
        Assert.Equal("12.09 09:05", TaskItemPresenter.TimeText(due, Now));
    }

    [Fact]
    public void MissingDeadlineHasNoTimeText()
    {
        Assert.Equal(string.Empty, TaskItemPresenter.TimeText(null, Now));
    }
}
```

- [ ] **Step 3: Прогнать — падает**

```bash
dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q --filter "FullyQualifiedName~TaskItemPresenterTests"
```

Ожидаемо: FAIL, `error CS0103: The name 'TaskItemPresenter' does not exist`.

- [ ] **Step 4: Реализовать**

`OrbitalSIP/Services/TaskItemPresenter.cs`:

```csharp
using System;
using OrbitalSIP.Models;

namespace OrbitalSIP.Services
{
    /// <summary>How soon a task is due, in the buckets the row label distinguishes.</summary>
    public enum DueBucket
    {
        None,
        Overdue,
        Today,
        Tomorrow,

        /// <summary>
        /// Everything the row label renders as a plain date with no relative word: a
        /// deadline more than a day out, AND a finished task whose deadline has already
        /// passed. IsOverdue excludes done/completed, so a closed task with an old
        /// deadline lands here — Later is defined by what the renderer does with it, not
        /// by when it happens, and that suits both cases.
        /// </summary>
        Later,
    }

    /// <summary>
    /// Everything a task row displays that can be worked out from the task itself.
    ///
    /// Kept apart from the view model — and free of I18nService — so the awkward parts
    /// (what counts as overdue, where "today" ends) are testable without Avalonia. The
    /// view model layers the translated words on top.
    /// </summary>
    public static class TaskItemPresenter
    {
        private const string ColorUrgent = "#EF4444";
        private const string ColorHigh   = "#F59E0B";
        private const string ColorMedium = "#60A5FA";
        private const string ColorLow    = "#64748B";

        /// <summary>
        /// Mirrors the backend's own predicate: not done or completed, and strictly past
        /// the deadline. Strict, so a task due at this exact second is still on time — the
        /// badge counts it the same way and the two must not disagree.
        /// </summary>
        public static bool IsOverdue(TaskItem task, DateTimeOffset now) =>
            task.DueDate is { } due
            && due < now
            && !IsFinished(task.Status);

        public static DueBucket Bucket(TaskItem task, DateTimeOffset now)
        {
            if (task.DueDate is not { } due) return DueBucket.None;
            if (IsOverdue(task, now)) return DueBucket.Overdue;

            var dueDay = DateAt(due, now);
            var today  = now.Date;

            if (dueDay == today) return DueBucket.Today;
            if (dueDay == today.AddDays(1)) return DueBucket.Tomorrow;
            return DueBucket.Later;
        }

        /// <summary>Time alone for today, day and time for anything further out.</summary>
        public static string TimeText(DateTimeOffset? due, DateTimeOffset now)
        {
            if (due is not { } value) return string.Empty;

            var local = value.ToOffset(now.Offset);
            return DateAt(value, now) == now.Date
                ? local.ToString("HH:mm")
                : local.ToString("dd.MM HH:mm");
        }

        /// <summary>
        /// The calendar day an instant falls on for whoever is holding the phone.
        ///
        /// ToOffset(now.Offset), not ToLocalTime(): the latter resolves against
        /// TimeZoneInfo.Local — the machine's setting — and discards the offset the value
        /// carries, so the answer changes with the host and the tests pass only on a box
        /// set to the deployment's zone. CallHistoryWindow already paid for that lesson:
        /// a UTC day boundary cost a night shift the first five hours of its own call
        /// history, and its summary says outright that the point of taking the instant is
        /// to be testable at offsets this machine is not set to.
        ///
        /// Shared rather than written twice, because Bucket and TimeText disagreeing about
        /// which day a row belongs to is a bug nothing would report.
        /// </summary>
        private static DateTime DateAt(DateTimeOffset instant, DateTimeOffset now) =>
            instant.ToOffset(now.Offset).Date;

        /// <summary>
        /// Stripe colour down the left of a row. An unknown or absent priority gets the
        /// quiet colour rather than no stripe, so rows stay the same width.
        /// </summary>
        public static string PriorityColor(string? priority) => priority?.ToLowerInvariant() switch
        {
            "urgent" => ColorUrgent,
            "high"   => ColorHigh,
            "medium" => ColorMedium,
            _        => ColorLow,
        };

        private static bool IsFinished(string? status) =>
            status is "done" or "completed";
    }
}
```

- [ ] **Step 5: Прогнать — зелёные**

```bash
dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q --filter "FullyQualifiedName~TaskItemPresenterTests"
```

Ожидаемо: PASS, 28 тестов — 23 из списка выше плюс пять, которые добавила проверка: ни один из 23 не подавал `status: null` вместе с прошедшим сроком; ни один не проверял независимость от зоны машины; ни один не гонял `Bucket` и `TimeText` от одного срока; ни один не пинил `Later` для закрытой просроченной задачи и регистр `IsFinished`.

- [ ] **Step 6: Коммит**

```bash
git add OrbitalSIP/Models/TaskModels.cs OrbitalSIP/Services/TaskItemPresenter.cs OrbitalSIP.Tests/TaskItemPresenterTests.cs
git commit -m "feat(tasks): model task rows and work out what each one displays"
```

---

## Task 6: `TaskService` — внедряемый клиент и три метода

**Files:**
- Modify: `OrbitalSIP/Services/TaskService.cs`
- Test: `OrbitalSIP.Tests/TaskServiceTests.cs`

- [ ] **Step 1: Написать падающие тесты**

`OrbitalSIP.Tests/TaskServiceTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

public class TaskServiceTests
{
    [Fact]
    public async Task GetMyTasksAsync_AsksForTheOperatorsOwnTasksWithBearerToken()
    {
        using var handler = new RecordingHandler(_ => JsonResponse("""
            { "data": [{ "id": 7, "title": "Перезвонить", "status": "pending" }], "total": 1 }
            """));
        using var service = CreateService(handler);

        var result = await service.GetMyTasksAsync("pending");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "https://crm.example/api/tasks?assignedToId=42&page=1&limit=50&status=pending",
            request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Authorization!.Scheme);
        Assert.Equal("widget-token", request.Authorization.Parameter);

        Assert.NotNull(result);
        Assert.Equal(1, result!.Total);
        var task = Assert.Single(result.Data);
        Assert.Equal(7, task.Id);
        Assert.Equal("Перезвонить", task.Title);
    }

    [Fact]
    public async Task GetMyTasksAsync_OmitsTheStatusFilterWhenNoneIsAskedFor()
    {
        using var handler = new RecordingHandler(_ => JsonResponse("""{ "data": [], "total": 0 }"""));
        using var service = CreateService(handler);

        await service.GetMyTasksAsync(null);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(
            "https://crm.example/api/tasks?assignedToId=42&page=1&limit=50",
            request.RequestUri!.ToString());
    }

    /// <summary>
    /// tasks:read is a separate ability from the tasks:create the widget already uses, so
    /// a role without it is a real deployment. The screen has to tell "you may not look at
    /// this" apart from "the backend is down", and only the 403 latches.
    /// </summary>
    [Fact]
    public async Task GetMyTasksAsync_ForbiddenLatchesTheNoAccessFlag()
    {
        using var handler = new RecordingHandler(_ => ErrorResponse(HttpStatusCode.Forbidden));
        using var service = CreateService(handler);

        var result = await service.GetMyTasksAsync("pending");

        Assert.Null(result);
        Assert.True(service.TasksForbidden);
    }

    [Fact]
    public async Task GetMyTasksAsync_ServerErrorDoesNotLatchTheNoAccessFlag()
    {
        using var handler = new RecordingHandler(_ => ErrorResponse(HttpStatusCode.InternalServerError));
        using var service = CreateService(handler);

        var result = await service.GetMyTasksAsync("pending");

        Assert.Null(result);
        Assert.False(service.TasksForbidden);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{}")]
    public async Task GetMyTasksAsync_SurvivesAnEmptyOrShapelessBody(string body)
    {
        using var handler = new RecordingHandler(_ => JsonResponse(body));
        using var service = CreateService(handler);

        var result = await service.GetMyTasksAsync(null);

        Assert.True(result is null || result.Data.Count == 0);
    }

    [Fact]
    public async Task GetMyStatsAsync_AsksForTheOperatorsOwnCounters()
    {
        using var handler = new RecordingHandler(_ => JsonResponse("""
            { "total": 9, "pending": 3, "inProgress": 2, "done": 4, "overdue": 1 }
            """));
        using var service = CreateService(handler);

        var stats = await service.GetMyStatsAsync();

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://crm.example/api/tasks/stats?assigneeId=42", request.RequestUri!.ToString());
        Assert.NotNull(stats);
        Assert.Equal(3, stats!.Pending);
        Assert.Equal(2, stats.InProgress);
        Assert.Equal(1, stats.Overdue);
    }

    [Fact]
    public async Task SetStatusAsync_PatchesTheTaskAndReportsSuccess()
    {
        using var handler = new RecordingHandler(_ => JsonResponse("""{ "id": 7, "status": "done" }"""));
        using var service = CreateService(handler);

        var ok = await service.SetStatusAsync(7, "done");

        Assert.True(ok);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Patch, request.Method);
        Assert.Equal("https://crm.example/api/tasks/7", request.RequestUri!.ToString());
        Assert.Contains("\"status\":\"done\"", request.Body);
    }

    [Fact]
    public async Task SetStatusAsync_ReportsFailureWithoutThrowing()
    {
        using var handler = new RecordingHandler(_ => ErrorResponse(HttpStatusCode.BadRequest));
        using var service = CreateService(handler);

        Assert.False(await service.SetStatusAsync(7, "done"));
    }

    private static TaskService CreateService(HttpMessageHandler handler) => new(
        new HttpClient(handler),
        SettingsProvider,
        ownsHttpClient: true);

    private static SipSettings SettingsProvider() => new()
    {
        BackendUrl = "https://crm.example/",
        AccessToken = "widget-token",
        // The numeric user id the tasks API assigns by; TaskService reads it off the JWT.
        // JwtPayload, not "DecodedToken" — that is the property name, not the type.
        DecodedToken = new JwtPayload { Sub = "42" },
    };

    private static HttpResponseMessage JsonResponse(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage ErrorResponse(HttpStatusCode code) => new(code)
    {
        Content = new StringContent("""{ "message": "nope" }""", Encoding.UTF8, "application/json"),
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri,
                request.Headers.Authorization,
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
            return responder(request);
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, Uri? RequestUri, System.Net.Http.Headers.AuthenticationHeaderValue? Authorization, string Body);
}
```

> Тип проверен: `SipSettings.DecodedToken` имеет тип `JwtPayload`
> (`OrbitalSIP/Services/JwtDecoder.cs:9`), а `Sub` — `string?` с конвертером
> `NumberOrStringConverter`, потому что локальный HS256-логин подписывает `sub` числом,
> а ID-токен Zitadel — строкой. Для теста достаточно `new JwtPayload { Sub = "42" }`.

- [ ] **Step 2: Прогнать — падает**

```bash
dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q --filter "FullyQualifiedName~TaskServiceTests"
```

Ожидаемо: FAIL, `error CS1729: 'TaskService' does not contain a constructor that takes 3 arguments`.

- [ ] **Step 3: Переписать `TaskService`**

`OrbitalSIP/Services/TaskService.cs` целиком:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using OrbitalSIP.Models;

namespace OrbitalSIP.Services
{
    /// <summary>
    /// CRM task API client. Mirrors LeadService: bearer-authed calls to the
    /// backend derived from the current SIP settings. Used to create a task
    /// straight off an active call, and to list the operator's own tasks.
    /// </summary>
    public class TaskService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly Func<SipSettings> _settingsProvider;
        private readonly bool _ownsHttpClient;

        private static readonly JsonSerializerOptions _writeOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private static readonly JsonSerializerOptions _readOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public TaskService()
            : this(BackendHttp.Client, () => App.SipService?.CurrentSettings ?? SipSettings.Load(), ownsHttpClient: false)
        {
        }

        /// <summary>
        /// Injectable overload, same shape as SmsService and ScriptService already use, so
        /// the request URLs and failure handling below are reachable from tests.
        /// </summary>
        public TaskService(HttpClient httpClient, Func<SipSettings> settingsProvider, bool ownsHttpClient = false)
        {
            _httpClient = httpClient;
            _settingsProvider = settingsProvider;
            _ownsHttpClient = ownsHttpClient;
        }

        /// <summary>
        /// Latched by the first 403 from the tasks API.
        ///
        /// tasks:read is a separate ability from the tasks:create this widget already
        /// relies on, so a role without it is a live possibility rather than a hypothesis.
        /// The screen shows "no access" on this instead of an empty list, and the badge
        /// poll stops for the session — a permission that is missing now will still be
        /// missing in two minutes.
        /// </summary>
        public bool TasksForbidden { get; private set; }

        /// <summary>Creates a task via POST /api/tasks. Returns true on 2xx.</summary>
        public async Task<bool> CreateTaskAsync(CreateTaskRequest task)
        {
            var response = await SendAsync(HttpMethod.Post, "/api/tasks", task);
            return response is not null;
        }

        /// <summary>
        /// Fetches active task types via GET /api/task-types for the picker.
        /// Returns an empty list on any failure (the dialog stays usable without types).
        /// </summary>
        public async Task<List<TaskTypeItem>> GetTaskTypesAsync()
        {
            var items = await SendAsync<List<TaskTypeItem>>(HttpMethod.Get, "/api/task-types");
            if (items == null) return new List<TaskTypeItem>();

            return items
                .Where(t => t.IsActive)
                .OrderBy(t => t.SortOrder)
                .ThenBy(t => t.Name)
                .ToList();
        }

        /// <summary>
        /// The operator's own tasks. <paramref name="status"/> is passed straight through
        /// to the backend filter; null asks for everything.
        ///
        /// Note the caller's burden: "open" is pending + in_progress, which the backend
        /// treats as disjoint sets, so listing open tasks means calling this twice.
        /// </summary>
        public async Task<TaskListResponse?> GetMyTasksAsync(string? status, CancellationToken ct = default)
        {
            var assignee = AssignedToId();
            if (assignee == null) return null;

            var path = $"/api/tasks?assignedToId={assignee}&page=1&limit=50";
            if (!string.IsNullOrWhiteSpace(status))
                path += $"&status={Uri.EscapeDataString(status)}";

            return await SendAsync<TaskListResponse>(HttpMethod.Get, path, ct: ct);
        }

        /// <summary>Counters behind the Tasks badge.</summary>
        public async Task<TaskStats?> GetMyStatsAsync(CancellationToken ct = default)
        {
            var assignee = AssignedToId();
            if (assignee == null) return null;

            return await SendAsync<TaskStats>(HttpMethod.Get, $"/api/tasks/stats?assigneeId={assignee}", ct: ct);
        }

        /// <summary>Moves one task to a new status via PATCH /api/tasks/{id}.</summary>
        public async Task<bool> SetStatusAsync(int taskId, string status)
        {
            var response = await SendAsync(HttpMethod.Patch, $"/api/tasks/{taskId}", new { status });
            return response is not null;
        }

        /// <summary>The numeric user id the tasks API assigns by, or null if the JWT has none.</summary>
        private int? AssignedToId()
        {
            var sub = _settingsProvider()?.DecodedToken?.Sub;
            if (int.TryParse(sub, out var userId)) return userId;

            AppLogger.Log("TaskService",
                $"No assignee — JWT sub is not a user id (sub: {(sub == null ? "<absent>" : $"'{sub}'")}).");
            return null;
        }

        /// <summary>
        /// The part every call used to repeat: settings, base URL, bearer header, status
        /// check, log, notifier. Two copies of it were tolerable; five were not.
        ///
        /// Returns null for every failure. Callers that need to tell a missing permission
        /// apart from a dead backend read <see cref="TasksForbidden"/>.
        /// </summary>
        private async Task<string?> SendAsync(HttpMethod method, string path, object? body = null,
                                              CancellationToken ct = default)
        {
            var url = string.Empty;
            try
            {
                var settings = _settingsProvider();
                var backendUrl = settings?.BackendUrl?.TrimEnd('/');

                if (string.IsNullOrEmpty(backendUrl) || string.IsNullOrEmpty(settings?.AccessToken))
                    return null;

                url = backendUrl + path;

                using var request = new HttpRequestMessage(method, url);
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.AccessToken);
                if (body != null)
                    request.Content = JsonContent.Create(body, options: _writeOptions);

                using var response = await _httpClient.SendAsync(request, ct);

                if (response.IsSuccessStatusCode)
                    return await response.Content.ReadAsStringAsync(ct);

                if (response.StatusCode == HttpStatusCode.Forbidden)
                    TasksForbidden = true;

                var errorBody = await response.Content.ReadAsStringAsync(ct);
                AppLogger.Log("TaskService", $"{method} {path} failed. Status: {response.StatusCode}. Body: {errorBody}");
                HttpErrorNotifier.NotifyHttpError("TaskService", url, response.StatusCode, errorBody);
                return null;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // The caller cancelled — that is not a failure to report. Without this the
                // generic catch below logs a stack trace and raises a banner, so switching
                // the tasks filter would tell the operator their own tap had gone wrong.
                // Guarded on the token: an OperationCanceledException from an HttpClient
                // timeout is a real failure and must still be reported as one.
                throw;
            }
            catch (Exception ex)
            {
                var details = $"Error on {method} {path}: {ex.GetType().Name}: {ex.Message}";
                if (ex.InnerException != null)
                    details += $" | Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
                details += $" | StackTrace: {ex.StackTrace}";
                AppLogger.Log("TaskService", details);
                HttpErrorNotifier.NotifyException("TaskService", ex);
                return null;
            }
        }

        /// <summary>
        /// Same as above, then deserialized. A body that parses to nothing is a failure,
        /// not an empty result: an empty 200 used to surface as "you have no tasks".
        /// </summary>
        private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body = null,
                                            CancellationToken ct = default) where T : class
        {
            var raw = await SendAsync(method, path, body, ct);
            if (string.IsNullOrWhiteSpace(raw)) return null;

            try
            {
                return JsonSerializer.Deserialize<T>(raw, _readOptions);
            }
            catch (JsonException ex)
            {
                AppLogger.Log("TaskService", $"Could not read the {method} {path} response: {ex.Message}");
                return null;
            }
        }

        public void Dispose()
        {
            if (_ownsHttpClient) _httpClient.Dispose();
        }
    }
}
```

- [ ] **Step 4: Прогнать — зелёные**

```bash
dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q --filter "FullyQualifiedName~TaskServiceTests"
```

Ожидаемо: PASS, 19 тестов — 10 из списка выше плюс девять, которые добавила проверка: не-числовой `sub` (две теории), проброс отмены, `Dispose` в обе стороны, сброс защёлки при смене токена, молчание `GetMyStatsAsync` и два на `GetTaskTypesAsync`.

- [ ] **Step 5: Прогнать весь набор — `CreateTaskAsync` и `GetTaskTypesAsync` переписаны, они под тестами в `TaskModelsTests`**

```bash
dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q
```

Ожидаемо: PASS, 584 теста.

- [ ] **Step 6: Коммит**

```bash
git add OrbitalSIP/Services/TaskService.cs OrbitalSIP.Tests/TaskServiceTests.cs
git commit -m "feat(tasks): list, count and close tasks through one HTTP path"
```

---

## Task 7: `NavBadgeService` и один опрос вместо двух

**Files:**
- Create: `OrbitalSIP/Services/NavBadgeService.cs`
- Modify: `OrbitalSIP/App.axaml.cs`
- Modify: `OrbitalSIP/MainWindow.axaml.cs`
- Modify: `OrbitalSIP/Views/OperatorStatsControl.axaml.cs`

Идёт перед экраном задач намеренно: экран дёргает `App.NavBadges.RefreshNowAsync()` после отметки «выполнено», а бейджи работают и без экрана — числа приходят из API, а не из списка на нём.

- [ ] **Step 1: Написать сервис**

`OrbitalSIP/Services/NavBadgeService.cs`:

```csharp
using Avalonia.Threading;
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using OrbitalSIP.Models;
using OrbitalSIP.Views;

namespace OrbitalSIP.Services
{
    /// <summary>
    /// Keeps the bottom-nav badge numbers fresh, and owns the only timer that does so.
    ///
    /// The timer cannot live in BottomNavControl: MainWindow rebuilds the whole screen —
    /// and with it a new control — on every navigation, so a timer there would restart on
    /// every tab press. The same asymmetry already froze OperatorStatsControl's two-minute
    /// refresh roughly 280 ms into the screen-swap animation.
    ///
    /// It also serves OperatorStatsControl, which used to poll the same URL on its own
    /// schedule. One request now, not two.
    /// </summary>
    public sealed class NavBadgeService : IDisposable
    {
        private static readonly TimeSpan Healthy = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan MaxInterval = TimeSpan.FromMinutes(10);

        private readonly NavBadgeState _state = new();
        private readonly HttpClient _httpClient = BackendHttp.Client;
        private DispatcherTimer? _timer;
        private int _consecutiveFailures;

        /// <summary>Raised on the UI thread whenever a number changed.</summary>
        public event Action? Changed;

        /// <summary>Latest operator stats, for whoever wants more than a badge out of them.</summary>
        public OperatorStats? OperatorStats { get; private set; }

        public void Start()
        {
            if (_timer != null) return;

            _timer = new DispatcherTimer { Interval = Healthy };
            _timer.Tick += async (_, __) => await PollAsync();
            _timer.Start();

            _ = PollAsync();
        }

        public void Stop()
        {
            _timer?.Stop();
            _timer = null;
        }

        /// <summary>Polls right now — after ticking a task off, or after a call ends.</summary>
        public Task RefreshNowAsync() => PollAsync();

        public void MarkRecentsSeen()
        {
            _state.MarkRecentsSeen();
            Raise();
        }

        /// <summary>Pushes the current numbers into a freshly built bar.</summary>
        public void ApplyTo(BottomNavControl nav)
        {
            nav.SetBadge(NavTab.Tasks, _state.OpenTasks, _state.HasOverdueTasks);
            nav.SetBadge(NavTab.Recents, _state.NewMissed, alert: false);
        }

        private async Task PollAsync()
        {
            var ok = true;

            // Ask TaskService rather than keeping a latch of our own. Its TasksForbidden is
            // scoped to the access token that drew the 403, so it clears itself when the
            // session changes — and a local bool here would outlive that, which is the
            // exact bug the token scoping was added to fix, just one layer up: we would
            // stop calling GetMyStatsAsync, so the token behind the latch would never be
            // re-examined, and an operator who does have tasks:read would keep seeing a
            // dead badge until the app restarted.
            if (!App.TaskService.TasksForbidden)
            {
                var stats = await App.TaskService.GetMyStatsAsync();
                if (stats != null)
                {
                    _state.SetTasks(stats.Pending, stats.InProgress, stats.Overdue);
                }
                else if (App.TaskService.TasksForbidden)
                {
                    // A permission this role does not have will not appear in two minutes,
                    // so stop asking until the session changes.
                    _state.SetTasks(0, 0, 0);
                    AppLogger.Log("NavBadges", "Tasks polling paused: the backend refused tasks:read.");
                }
                else
                {
                    ok = false;
                }
            }

            var missed = await LoadMissedCallsAsync();
            if (missed.HasValue) _state.SetMissed(missed.Value);
            else ok = false;

            // Failures are logged, never raised as a banner. A badge is not worth
            // interrupting a call over, and a backend down all shift would otherwise put
            // one on screen every two minutes.
            //
            // Silence has to be asked for, not assumed: the banner is raised inside
            // TaskService.SendAsync, synchronously, so there is nothing here to intercept.
            // GetMyStatsAsync passes notifyErrors: false for that reason, and
            // LoadMissedCallsAsync below — which does not go through TaskService — keeps
            // the same rule by hand, logging and nothing more.
            _consecutiveFailures = ok ? 0 : _consecutiveFailures + 1;
            if (_timer != null)
                _timer.Interval = PollBackoff.Next(_consecutiveFailures, Healthy, MaxInterval);

            Raise();
        }

        /// <summary>
        /// The same endpoint OperatorStatsControl used to call on its own timer. Returns
        /// null on any failure, so the badge keeps its last known value: a stale number is
        /// a smaller lie than a zero.
        /// </summary>
        private async Task<int?> LoadMissedCallsAsync()
        {
            try
            {
                var settings = App.SipService?.CurrentSettings ?? SipSettings.Load();
                var operatorId = settings.DecodedToken?.Operator?.Username ?? settings.Username;
                var backendUrl = settings.BackendUrl?.TrimEnd('/');

                if (string.IsNullOrEmpty(operatorId) || string.IsNullOrEmpty(backendUrl))
                    return null;

                var url = $"{backendUrl}/api/contact-center/operators/{Uri.EscapeDataString(operatorId)}/details?range=today";

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (!string.IsNullOrEmpty(settings.AccessToken))
                    request.Headers.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.AccessToken);

                using var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    AppLogger.Log("NavBadges", $"Operator details failed: {response.StatusCode}");
                    return null;
                }

                var data = await response.Content.ReadFromJsonAsync<OperatorDetailsResponse>();
                if (data?.Stats == null) return null;

                OperatorStats = data.Stats;
                return data.Stats.MissedCalls;
            }
            catch (Exception ex)
            {
                AppLogger.Log("NavBadges", $"Operator details error: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        private void Raise() => Dispatcher.UIThread.InvokeAsync(() => Changed?.Invoke());

        public void Dispose() => Stop();
    }
}
```

- [ ] **Step 2: Зарегистрировать сервис**

В `OrbitalSIP/App.axaml.cs`, рядом с остальными статическими сервисами:

```csharp
        public static readonly NavBadgeService NavBadges = new NavBadgeService();
```

- [ ] **Step 3: Подключить в `MainWindow`**

В `AttachNav`, последней строкой перед закрывающей скобкой:

```csharp
            App.NavBadges.ApplyTo(nav);
```

В конструкторе `MainWindow`, там же, где стартуют прочие сессионные подписки, добавить перерисовку по событию:

```csharp
            App.NavBadges.Changed += () =>
            {
                var nav = CurrentNav();
                if (nav != null) App.NavBadges.ApplyTo(nav);
            };
```

В `NavigateTo`, в ветке `NavTab.Recents`, перед `ShowRecents()`:

```csharp
                case NavTab.Recents:  App.NavBadges.MarkRecentsSeen(); ShowRecents();  break;
```

Запуск и остановка. В том месте, где уже стартует сессия после успешного логина (`login.OnLoginSuccess` в `ShowLogin`), добавить:

```csharp
                App.NavBadges.Start();
```

В `ShowLoginAfterSessionExpiry` и в `ShutdownApp`, первой строкой:

```csharp
            App.NavBadges.Stop();
```

А также в конструкторе `MainWindow`, в ветке, где уже есть сохранённая сессия и показывается виджет (рядом с `SetMainContent(new Views.WidgetView());`):

```csharp
                App.NavBadges.Start();
```

- [ ] **Step 4: Снять таймер с `OperatorStatsControl`**

В `OrbitalSIP/Views/OperatorStatsControl.axaml.cs` заменить в конструкторе:

```csharp
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(2)
            };
            _timer.Tick += async (_, __) => await LoadStatsAsync();
            _timer.Start();

            // Initial load
            _ = LoadStatsAsync();
```

на:

```csharp
            // Polling moved to NavBadgeService: it hits the same endpoint for the Recents
            // badge, and it survives the screen-swap animation that used to stop the timer
            // that lived here.
            App.NavBadges.Changed += OnBadgesChanged;
            if (App.NavBadges.OperatorStats is { } stats) UpdateUI(stats);
```

Заменить `OnDetachedFromVisualTree` и `OnAttachedToVisualTree` целиком на:

```csharp
        protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            App.NavBadges.Changed -= OnBadgesChanged;
        }

        /// <summary>
        /// The screen-swap animation parents this control into OverlayHost and then moves
        /// it to Host — a detach/attach pair — so the detach above is not the end of its
        /// life. Idempotent re-subscribe: -= on an absent handler is a no-op, += twice
        /// would repaint twice.
        /// </summary>
        protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            App.NavBadges.Changed -= OnBadgesChanged;
            App.NavBadges.Changed += OnBadgesChanged;
            if (App.NavBadges.OperatorStats is { } stats) UpdateUI(stats);
        }

        private void OnBadgesChanged()
        {
            if (App.NavBadges.OperatorStats is { } stats)
                Dispatcher.UIThread.InvokeAsync(() => UpdateUI(stats));
        }
```

Заменить тело `LoadStatsAsync` — кнопка «обновить» теперь дёргает общий опрос:

```csharp
        public Task LoadStatsAsync() => App.NavBadges.RefreshNowAsync();
```

Удалить ставшее ненужным поле:

```csharp
        private DispatcherTimer? _timer;
        private static readonly HttpClient _httpClient = Services.BackendHttp.Client;
```

- [ ] **Step 5: Собрать и прогнать тесты**

```bash
dotnet build OrbitalSIP/OrbitalSIP.csproj --nologo
dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q
```

Ожидаемо: сборка чистая, 584 теста зелёные. Если `OperatorDetailsResponse` окажется недоступен из `Services` (он объявлен рядом с `OperatorStats`) — проверить пространство имён:

```bash
grep -rn "class OperatorDetailsResponse" OrbitalSIP/Models/OperatorStats.cs
```

- [ ] **Step 6: Проверить руками**

```bash
dotnet run --project OrbitalSIP/OrbitalSIP.csproj
```

1. Есть открытые задачи — на табе «Задачи» цифра. Есть просроченная — цифра красная.
2. Пропустить звонок → на табе «История» цифра. Открыть «Историю» → цифра пропала. Пропустить ещё один → цифра 1.
3. Панель статистики в диалере по-прежнему показывает числа и обновляется по своей кнопке.
4. Выключить бэкенд на пару минут: баннеров об ошибке не появляется, бейджи держат последние значения.

- [ ] **Step 7: Коммит**

```bash
git add OrbitalSIP/Services/NavBadgeService.cs OrbitalSIP/App.axaml.cs OrbitalSIP/MainWindow.axaml.cs OrbitalSIP/Views/OperatorStatsControl.axaml.cs
git commit -m "feat(nav): poll badge counts once per session instead of once per screen"
```

---

## Task 8: Экран `TasksView`

**Files:**
- Create: `OrbitalSIP/ViewModels/TaskItemViewModel.cs`
- Create: `OrbitalSIP/Views/TasksView.axaml`
- Create: `OrbitalSIP/Views/TasksView.axaml.cs`
- Modify: `OrbitalSIP/MainWindow.axaml.cs` (заменить заглушку `ShowTasks`)
- Modify: `OrbitalSIP/Assets/i18n/{ru,uz,kk,tg}.json`

- [ ] **Step 1: Добавить ключи локализации**

`ru.json`:
```json
  "TasksOpen": "Открытые",
  "TasksAll": "Все",
  "TasksEmpty": "Задач нет",
  "TasksNoAccess": "Нет доступа к задачам",
  "TaskDone": "Выполнено",
  "TaskDoneFailed": "Не удалось изменить статус задачи",
  "DueToday": "сегодня",
  "DueTomorrow": "завтра",
  "DueOverdue": "просрочено",
```

`uz.json`:
```json
  "TasksOpen": "Ochiq",
  "TasksAll": "Barchasi",
  "TasksEmpty": "Vazifalar yo'q",
  "TasksNoAccess": "Vazifalarga ruxsat yo'q",
  "TaskDone": "Bajarildi",
  "TaskDoneFailed": "Vazifa holatini o'zgartirib bo'lmadi",
  "DueToday": "bugun",
  "DueTomorrow": "ertaga",
  "DueOverdue": "muddati o'tgan",
```

`kk.json`:
```json
  "TasksOpen": "Ашық",
  "TasksAll": "Барлығы",
  "TasksEmpty": "Тапсырмалар жоқ",
  "TasksNoAccess": "Тапсырмаларға рұқсат жоқ",
  "TaskDone": "Орындалды",
  "TaskDoneFailed": "Тапсырма күйін өзгерту мүмкін болмады",
  "DueToday": "бүгін",
  "DueTomorrow": "ертең",
  "DueOverdue": "мерзімі өтті",
```

`tg.json`:
```json
  "TasksOpen": "Кушода",
  "TasksAll": "Ҳама",
  "TasksEmpty": "Вазифаҳо нестанд",
  "TasksNoAccess": "Дастрасӣ ба вазифаҳо нест",
  "TaskDone": "Иҷро шуд",
  "TaskDoneFailed": "Ҳолати вазифаро тағйир додан нашуд",
  "DueToday": "имрӯз",
  "DueTomorrow": "фардо",
  "DueOverdue": "мӯҳлаташ гузашт",
```

- [ ] **Step 2: Написать view model**

`OrbitalSIP/ViewModels/TaskItemViewModel.cs`:

```csharp
using System;
using OrbitalSIP.Models;
using OrbitalSIP.Services;

namespace OrbitalSIP.ViewModels
{
    /// <summary>
    /// One row of the tasks list, pre-computed for binding.
    ///
    /// Same division of labour as CdrItemViewModel: everything the XAML needs is a plain
    /// property here, so the markup stays free of converters. The arithmetic itself lives
    /// in TaskItemPresenter, which is where the tests can reach it.
    /// </summary>
    public class TaskItemViewModel
    {
        public TaskItem Task { get; }

        public int Id => Task.Id;
        public string Title { get; }
        public string Subtitle { get; }
        public string PriorityColor { get; }
        public string SubtitleColor { get; }

        public TaskItemViewModel(TaskItem task, DateTimeOffset now)
        {
            Task = task;

            Title = string.IsNullOrWhiteSpace(task.Title) ? "—" : task.Title.Trim();
            PriorityColor = TaskItemPresenter.PriorityColor(task.Priority);

            var i18n = I18nService.Instance;
            var bucket = TaskItemPresenter.Bucket(task, now);
            var time = TaskItemPresenter.TimeText(task.DueDate, now);

            var due = bucket switch
            {
                DueBucket.Overdue  => $"{i18n.Get("DueOverdue")} {time}".Trim(),
                DueBucket.Today    => $"{i18n.Get("DueToday")} {time}".Trim(),
                DueBucket.Tomorrow => $"{i18n.Get("DueTomorrow")} {time}".Trim(),
                DueBucket.Later    => time,
                _                  => string.Empty,
            };

            var type = task.TaskType?.Name?.Trim() ?? string.Empty;

            Subtitle = (type.Length, due.Length) switch
            {
                (> 0, > 0) => $"{type} · {due}",
                (> 0, _)   => type,
                (_, > 0)   => due,
                _          => string.Empty,
            };

            // The deadline is the only part of the subtitle worth alarming about, so a row
            // with no deadline keeps the quiet colour even when it is the only text there.
            SubtitleColor = bucket == DueBucket.Overdue ? "#FCA5A5" : "#6E859D";
        }
    }
}
```

- [ ] **Step 3: Написать разметку**

`OrbitalSIP/Views/TasksView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:i18n="clr-namespace:OrbitalSIP.Services"
             xmlns:Views="clr-namespace:OrbitalSIP.Views"
             xmlns:materialIcons="clr-namespace:Material.Icons.Avalonia;assembly=Material.Icons.Avalonia"
             x:Class="OrbitalSIP.Views.TasksView">

  <UserControl.Styles>
    <Style Selector="Button.filter-chip">
      <Setter Property="Background" Value="#152132" />
      <Setter Property="BorderBrush" Value="#1D3050" />
      <Setter Property="BorderThickness" Value="1" />
      <Setter Property="CornerRadius" Value="10" />
      <Setter Property="Padding" Value="10,4" />
      <Setter Property="FontSize" Value="11" />
      <Setter Property="FontWeight" Value="SemiBold" />
      <Setter Property="Foreground" Value="#7B92AA" />
      <Setter Property="Cursor" Value="Hand" />
    </Style>
    <Style Selector="Button.filter-chip.selected">
      <Setter Property="Background" Value="#1E4270" />
      <Setter Property="BorderBrush" Value="#2C5A96" />
      <Setter Property="Foreground" Value="#DDE7F3" />
    </Style>
  </UserControl.Styles>

  <Border Width="320"
          Background="#0F172A"
          BorderBrush="#1E293B"
          BorderThickness="1"
          CornerRadius="16"
          ClipToBounds="True">
    <Grid RowDefinitions="Auto,Auto,*,Auto">

      <Views:TopBarControl Name="TopBar" />

      <StackPanel Grid.Row="1" Spacing="8" Margin="14,10,14,0">
        <Grid ColumnDefinitions="*,Auto">
          <TextBlock Text="{i18n:I18n Tasks}" FontSize="12" FontWeight="Bold" LetterSpacing="1.2"
                     Foreground="#7B92AA" VerticalAlignment="Center" />
          <Button Grid.Column="1" Name="RefreshTasksBtn" Width="24" Height="24"
                  Background="Transparent" Padding="0" Margin="0" Cursor="Hand" BorderThickness="0">
            <materialIcons:MaterialIcon Kind="Refresh" Width="16" Height="16" Foreground="#60A5FA" />
          </Button>
        </Grid>
        <StackPanel Orientation="Horizontal" Spacing="6">
          <Button Name="OpenFilterBtn" Classes="filter-chip selected" Content="{i18n:I18n TasksOpen}" />
          <Button Name="AllFilterBtn" Classes="filter-chip" Content="{i18n:I18n TasksAll}" />
        </StackPanel>
      </StackPanel>

      <ScrollViewer Grid.Row="2" Margin="14,10,14,10"
                    HorizontalScrollBarVisibility="Disabled" VerticalScrollBarVisibility="Auto">
        <ItemsControl Name="TaskItemsControl">
          <ItemsControl.ItemTemplate>
            <DataTemplate>
              <Grid ColumnDefinitions="Auto,*,Auto" Margin="0,4,0,4">
                <Border Width="3" CornerRadius="2" Background="{Binding PriorityColor}" Margin="0,2,10,2" />
                <StackPanel Grid.Column="1" Spacing="2" VerticalAlignment="Center">
                  <TextBlock Text="{Binding Title}" FontSize="13" FontWeight="SemiBold"
                             Foreground="#E2E8F0" TextWrapping="Wrap" MaxLines="2" TextTrimming="CharacterEllipsis" />
                  <TextBlock Text="{Binding Subtitle}" FontSize="11" Foreground="{Binding SubtitleColor}" />
                </StackPanel>
                <Button Grid.Column="2"
                        Width="32" Height="32"
                        Background="Transparent" BorderThickness="0" Padding="6" Cursor="Hand"
                        Click="OnTaskDoneClicked"
                        Tag="{Binding}"
                        ToolTip.Tip="{i18n:I18n TaskDone}"
                        VerticalAlignment="Center">
                  <materialIcons:MaterialIcon Kind="CheckCircleOutline" Width="20" Height="20" Foreground="#22C55E" />
                </Button>
              </Grid>
            </DataTemplate>
          </ItemsControl.ItemTemplate>
        </ItemsControl>
      </ScrollViewer>

      <TextBlock Grid.Row="2" Name="TasksMessageLabel" IsVisible="False"
                 VerticalAlignment="Center" HorizontalAlignment="Center" Margin="14,0"
                 FontSize="12" Foreground="#6E859D" TextWrapping="Wrap" TextAlignment="Center" />

      <Views:BottomNavControl Grid.Row="3" Name="BottomNav" />

    </Grid>
  </Border>
</UserControl>
```

- [ ] **Step 4: Написать code-behind**

`OrbitalSIP/Views/TasksView.axaml.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using OrbitalSIP.Models;
using OrbitalSIP.Services;
using OrbitalSIP.ViewModels;

namespace OrbitalSIP.Views
{
    /// <summary>
    /// The operator's own tasks. Read-only apart from one action — tick a task off —
    /// because everything richer needs the CRM, and this panel is 320 pixels wide.
    /// </summary>
    public partial class TasksView : UserControl
    {
        private readonly ObservableCollection<TaskItemViewModel> _items = new();
        private bool _openOnly = true;
        private bool _isLoading;

        public event EventHandler? OnCloseRequested;
        public event EventHandler? OnExitAppRequested;

        public TasksView()
        {
            InitializeComponent();
            WireButtons();

            var list = this.FindControl<ItemsControl>("TaskItemsControl");
            if (list != null) list.ItemsSource = _items;

            _ = LoadAsync();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void WireButtons()
        {
            var topBar = this.FindControl<TopBarControl>("TopBar");
            if (topBar != null)
            {
                topBar.OnMinimizeRequested += (_, __) => OnCloseRequested?.Invoke(this, EventArgs.Empty);
                topBar.OnCloseRequested += (_, __) => OnExitAppRequested?.Invoke(this, EventArgs.Empty);
            }

            var refresh = this.FindControl<Button>("RefreshTasksBtn");
            if (refresh != null) refresh.Click += (_, __) => _ = LoadAsync();

            var openBtn = this.FindControl<Button>("OpenFilterBtn");
            if (openBtn != null) openBtn.Click += (_, __) => SetFilter(openOnly: true);

            var allBtn = this.FindControl<Button>("AllFilterBtn");
            if (allBtn != null) allBtn.Click += (_, __) => SetFilter(openOnly: false);
        }

        private void SetFilter(bool openOnly)
        {
            if (_openOnly == openOnly) return;
            _openOnly = openOnly;

            this.FindControl<Button>("OpenFilterBtn")?.Classes.Set("selected", openOnly);
            this.FindControl<Button>("AllFilterBtn")?.Classes.Set("selected", !openOnly);

            _ = LoadAsync();
        }

        /// <summary>
        /// Loads the current filter.
        ///
        /// "Open" is two requests, not one: the backend's pending filter is literally
        /// NOT IN ('in_progress', 'done', 'completed'), so asking for pending alone would
        /// hide every task the operator has already started — and disagree with the badge,
        /// which counts both.
        /// </summary>
        private async Task LoadAsync()
        {
            if (_isLoading) return;
            _isLoading = true;

            try
            {
                var service = App.TaskService;
                var now = DateTimeOffset.Now;

                var tasks = new List<TaskItem>();
                if (_openOnly)
                {
                    var pending = await service.GetMyTasksAsync("pending");
                    var running = await service.GetMyTasksAsync("in_progress");
                    if (pending?.Data != null) tasks.AddRange(pending.Data);
                    if (running?.Data != null) tasks.AddRange(running.Data);
                    tasks = tasks
                        .GroupBy(t => t.Id)
                        .Select(g => g.First())
                        .OrderByDescending(t => t.CreatedAt ?? DateTimeOffset.MinValue)
                        .ToList();
                }
                else
                {
                    var all = await service.GetMyTasksAsync(null);
                    if (all?.Data != null) tasks.AddRange(all.Data);
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _items.Clear();
                    foreach (var task in tasks)
                        _items.Add(new TaskItemViewModel(task, now));

                    ShowMessage(tasks.Count > 0
                        ? null
                        : I18nService.Instance.Get(service.TasksForbidden ? "TasksNoAccess" : "TasksEmpty"));
                });
            }
            finally
            {
                _isLoading = false;
            }
        }

        private void ShowMessage(string? message)
        {
            var label = this.FindControl<TextBlock>("TasksMessageLabel");
            if (label == null) return;

            label.Text = message ?? string.Empty;
            label.IsVisible = message != null;
        }

        /// <summary>
        /// Ticks a task off. The row goes immediately and comes back if the PATCH fails —
        /// the operator is usually mid-call here, and waiting on a round trip to find out
        /// whether the tap registered is the wrong trade.
        /// </summary>
        private async void OnTaskDoneClicked(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: TaskItemViewModel row }) return;

            var index = _items.IndexOf(row);
            if (index < 0) return;

            _items.RemoveAt(index);
            if (_items.Count == 0) ShowMessage(I18nService.Instance.Get("TasksEmpty"));

            var ok = await App.TaskService.SetStatusAsync(row.Id, "done");
            if (ok)
            {
                await App.NavBadges.RefreshNowAsync();
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _items.Insert(Math.Min(index, _items.Count), row);
                ShowMessage(I18nService.Instance.Get("TaskDoneFailed"));
            });
        }
    }
}
```

- [ ] **Step 5: Подключить экран в `MainWindow`**

Заменить заглушку `ShowTasks` из Task 4:

```csharp
        private void ShowTasks()
        {
            var tasks = new Views.TasksView();
            tasks.OnCloseRequested += (_, __) => ToggleExpanded();
            tasks.OnExitAppRequested += (_, __) => ShutdownApp();

            SetMainContent(tasks);
        }
```

`_currentTab` здесь не присваивается: после Task 4 он выводится из типа контента внутри
`AttachNav`. Туда же добавить недостающую ветку — до Task 8 её не было, потому что типа
не существовало:

```csharp
                Views.TasksView    => NavTab.Tasks,
```

И снять `TODO(task-8):` с комментария заглушки.

- [ ] **Step 6: Собрать и прогнать тесты**

```bash
dotnet build OrbitalSIP/OrbitalSIP.csproj --nologo
dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q
```

Ожидаемо: сборка без ошибок, 584 теста зелёные.

- [ ] **Step 7: Проверить руками**

```bash
dotnet run --project OrbitalSIP/OrbitalSIP.csproj
```

1. Открыть таб «Задачи» — грузится список.
2. Переключить на «Все» — появляются закрытые задачи, у просроченных подпись красная.
3. Нажать ✓ — строка исчезает. Обновить — она не вернулась.
4. Если бэкенд не выдал `tasks:read`, вместо списка — «Нет доступа к задачам».

- [ ] **Step 8: Коммит**

```bash
git add OrbitalSIP/ViewModels/TaskItemViewModel.cs OrbitalSIP/Views/TasksView.axaml OrbitalSIP/Views/TasksView.axaml.cs OrbitalSIP/MainWindow.axaml.cs OrbitalSIP/Assets/i18n
git commit -m "feat(tasks): give the fourth nav slot a screen worth opening"
```

---

## Task 9: Починить `KeypadBtn`

**Files:**
- Modify: `OrbitalSIP/Views/ActiveCallView.axaml` (добавить панель после `TransferPanel`)
- Modify: `OrbitalSIP/Views/ActiveCallView.axaml.cs:268-270,1166`
- Modify: `OrbitalSIP/MainWindow.axaml.cs` (удалить подписку)

- [ ] **Step 1: Добавить панель DTMF в разметку**

В `OrbitalSIP/Views/ActiveCallView.axaml` сразу после закрывающего тега `Border` с `Name="TransferPanel"` вставить:

```xml
          <Border Name="DtmfPanel" IsVisible="False"
                  Background="#0F1A28" BorderBrush="#1B2B3F" BorderThickness="1"
                  CornerRadius="14" Padding="14,12">
            <StackPanel Spacing="8">
              <TextBlock Name="DtmfEchoLabel" Text="" FontSize="14" FontWeight="SemiBold"
                         Foreground="#F8FAFC" HorizontalAlignment="Center" MinHeight="18" />
              <UniformGrid Name="DtmfGrid" Columns="3" Rows="4" />
            </StackPanel>
          </Border>
```

- [ ] **Step 2: Заменить обработчик кнопки**

В `OrbitalSIP/Views/ActiveCallView.axaml.cs` заменить:

```csharp
            var keypad = this.FindControl<Button>("KeypadBtn");
            if (keypad != null)
                keypad.Click += (_, __) => OnKeypadRequested?.Invoke(this, EventArgs.Empty);
```

на:

```csharp
            var keypad = this.FindControl<Button>("KeypadBtn");
            if (keypad != null)
                keypad.Click += (_, __) => ToggleDtmfPanel();
```

- [ ] **Step 3: Добавить панель и отправку тонов**

В `OrbitalSIP/Views/ActiveCallView.axaml.cs` рядом с `ShowTransferPanel`:

```csharp
        /// <summary>
        /// Opens the in-call DTMF pad.
        ///
        /// This button used to raise OnKeypadRequested, which MainWindow wired to
        /// ShowDialer() — and ShowDialer() redirects to the call screen whenever a call is
        /// up. So the only thing pressing it ever did was rebuild the screen it was on,
        /// and the operator could not send a single tone into an IVR.
        /// </summary>
        private void ToggleDtmfPanel()
        {
            var panel = this.FindControl<Border>("DtmfPanel");
            if (panel == null) return;

            if (!panel.IsVisible) BuildDtmfPad();
            panel.IsVisible = !panel.IsVisible;

            if (!panel.IsVisible)
            {
                var echo = this.FindControl<TextBlock>("DtmfEchoLabel");
                if (echo != null) echo.Text = string.Empty;
            }
        }

        /// <summary>Fills the pad once; reopening reuses the buttons already there.</summary>
        private void BuildDtmfPad()
        {
            var grid = this.FindControl<UniformGrid>("DtmfGrid");
            if (grid == null || grid.Children.Count > 0) return;

            foreach (var digit in "123456789*0#")
            {
                var key = digit;
                var button = new Button
                {
                    Content = key.ToString(),
                    FontSize = 16,
                    FontWeight = Avalonia.Media.FontWeight.SemiBold,
                    Foreground = Avalonia.Media.Brushes.White,
                    Background = Avalonia.Media.Brush.Parse("#152132"),
                    BorderThickness = new Avalonia.Thickness(0),
                    CornerRadius = new Avalonia.CornerRadius(10),
                    Margin = new Avalonia.Thickness(3),
                    Height = 34,
                    HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                };
                button.Click += SafeHandler.Click("ActiveCallDtmf", () => SendDtmfAsync(key));
                grid.Children.Add(button);
            }
        }

        private async Task SendDtmfAsync(char digit)
        {
            var echo = this.FindControl<TextBlock>("DtmfEchoLabel");
            if (echo != null) echo.Text += digit;

            await App.SipService.SendDtmfAsync(digit);
        }
```

Добавить в шапку файла using, если его нет:

```csharp
using Avalonia.Controls.Primitives;
```

- [ ] **Step 4: Удалить осиротевшее событие**

В `OrbitalSIP/Views/ActiveCallView.axaml.cs` удалить объявление:

```csharp
        public event EventHandler?        OnKeypadRequested;
```

В `OrbitalSIP/MainWindow.axaml.cs`, в `WireActiveCallView`, удалить строку:

```csharp
            callView.OnKeypadRequested += (_, __) => ShowDialer();
```

- [ ] **Step 5: Собрать и прогнать тесты**

```bash
dotnet build OrbitalSIP/OrbitalSIP.csproj --nologo
dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q
```

Ожидаемо: сборка чистая, 584 теста зелёные.

- [ ] **Step 6: Проверить руками**

Позвонить на номер с IVR, нажать «Клавиши», набрать добавочный — тон уходит, цифры видны в строке над клавиатурой. Нажать «Клавиши» ещё раз — панель закрылась.

- [ ] **Step 7: Коммит**

```bash
git add OrbitalSIP/Views/ActiveCallView.axaml OrbitalSIP/Views/ActiveCallView.axaml.cs OrbitalSIP/MainWindow.axaml.cs
git commit -m "fix(call): send DTMF from the keypad button instead of rebuilding the screen"
```

---

## Финальная проверка

- [ ] **Полный прогон**

```bash
dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q
```

Ожидаемо: 584 теста, 0 упавших.

- [ ] **Сборка релизной конфигурации**

```bash
dotnet build OrbitalSIP/OrbitalSIP.csproj -c Release --nologo
```

Ожидаемо: 0 errors, 0 warnings. Особенно — ни одного `CS0067` («event is never used»): такое предупреждение означает осиротевшее событие, которое надо было удалить.

- [ ] **Убедиться, что от старого API навигации ничего не осталось**

```bash
grep -rn "OnSettingsRequested\|OnRecentsRequested\|OnDialerRequested\|OnContactsRequested\|OnBackRequested\|OnKeypadRequested\|SetActiveTab" --include="*.cs" --include="*.axaml" OrbitalSIP | grep -v obj | grep -v bin
```

Ожидаемо: только `LoginView.OnSettingsRequested` (у него свой смысл — открыть настройки до логина, к нижнему меню отношения не имеет).

- [ ] **Прогон по чек-листу целиком**

Пройти пункты из Task 4 Step 11, Task 7 Step 6, Task 8 Step 7 и Task 9 Step 6 подряд, на одной сессии.
