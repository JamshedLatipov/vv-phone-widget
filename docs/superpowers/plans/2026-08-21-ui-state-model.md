# Модель состояний UI — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Свести весь UI софтфона к одной записи состояния, которую меняет одна чистая функция, и дать активному звонку собственный route с плашкой возврата — чтобы таб «Набор» во время разговора оставался набором.

**Architecture:** Появляется `ShellRouter.Reduce(state, event, callState)` — чистая функция без ссылок на Avalonia, в которой живёт вся таблица переходов. `MainWindow` перестаёт быть логикой: он поднимает события через `Dispatch`, получает новое `UiState` и рисует разницу в `Apply`. Четыре ручных флага (`_isExpanded`, `_preferredMode`, `_settingsFromLogin`, `_currentTab`) исчезают, потому что становятся выводимыми из состояния. Хром панели (топбар, плашка возврата, нижнее меню) съезжает из пяти экранов в один `PanelShellView`.

**Tech Stack:** .NET 8 (`net8.0-windows10.0.17763`), Avalonia UI 11, Material.Icons.Avalonia, xUnit 2.5.

**Спека:** `docs/superpowers/specs/2026-08-21-ui-state-model-design.md` (коммит `ad9b38b`)

**Ветка:** создать `feat/ui-state-model` от текущей `feat/bottom-nav-rework`

---

## Как гонять тесты

Весь набор:

```bash
dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q
```

Один класс:

```bash
dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q --filter "FullyQualifiedName~ShellRouterTests"
```

Сборка приложения (обязательна после каждой задачи, которая трогает `Views/` или `MainWindow`):

```bash
dotnet build OrbitalSIP/OrbitalSIP.csproj --nologo
```

Запуск для ручной проверки:

```bash
dotnet run --project OrbitalSIP/OrbitalSIP.csproj
```

---

## Что уже есть в репозитории

Читать до начала, чтобы не изобретать заново:

- `OrbitalSIP/Models/NavTab.cs` — `enum NavTab { Dialer, Recents, Tasks, Settings }`, четыре слота меню.
- `OrbitalSIP/Services/SipService.cs:15` — `enum CallState { Idle, Ringing, IncomingRinging, Active, OnHold }`. `Ringing` — исходящий гудок, `IncomingRinging` — входящий.
- `OrbitalSIP/Models/NavBadgeState.cs`, `Models/TaskListOutcome.cs`, `Models/WidgetScale.cs` — образец того, как в этом проекте выглядит чистая модель под тесты: без сети, без таймеров, без Avalonia.
- `OrbitalSIP.Tests/TaskListOutcomeTests.cs` — образец стиля тестов: xUnit, `[Fact]`, короткий приватный хелпер-конструктор, XML-комментарий над тестом объясняет, какой баг тест ловит.
- `OrbitalSIP/MainWindow.axaml.cs` — то, что будет разбираться. Ключевые места: `_uiScale` и производные размеры (строки 19–43), `StartAnimation` (892), `SetMainContent` (967), `AttachNav` (1003), `NavigateTo` (1108), `CompleteAnimatedContentSwap` (1143).

---

## Task 1: `Shell`, `NavRoute` и геометрия поверхностей

**Files:**
- Create: `OrbitalSIP/Models/Shell.cs`
- Create: `OrbitalSIP/Models/NavRoute.cs`
- Create: `OrbitalSIP/Models/ShellGeometry.cs`
- Test: `OrbitalSIP.Tests/ShellGeometryTests.cs`

Размеры сейчас живут константами в `MainWindow` (`BaseWidgetSize`, `BaseExpandedWidth`, `BaseExpandedHeight`, `BaseIncomingWidth`, `BaseIncomingHeight`) и раздаются по девятнадцати вызовам вручную. Здесь они получают одного владельца и функцию от поверхности.

- [ ] **Step 1: Написать падающий тест**

Создать `OrbitalSIP.Tests/ShellGeometryTests.cs`:

```csharp
using OrbitalSIP.Models;
using Xunit;

namespace OrbitalSIP.Tests;

/// <summary>
/// Размер и способ размещения окна — свойство поверхности, а не того метода, который
/// её строит. Логин центрируется по экрану, всё остальное держится за нижне-правый
/// угол; сегодня это правило записано только внутри двух методов, которые строят
/// логин, и любой третий путь к нему его теряет.
/// </summary>
public class ShellGeometryTests
{
    [Theory]
    [InlineData(Shell.Login)]
    [InlineData(Shell.LoginSettings)]
    public void LoginSurfacesArePanelSizedAndCentered(Shell shell)
    {
        var box = ShellGeometry.For(shell);

        Assert.Equal(320, box.Width);
        Assert.Equal(600, box.Height);
        Assert.Equal(ShellPlacement.CenterOnScreen, box.Placement);
    }

    [Fact]
    public void PanelIsTheSameSizeAsLoginButAnchored()
    {
        var box = ShellGeometry.For(Shell.Panel);

        Assert.Equal(320, box.Width);
        Assert.Equal(600, box.Height);
        Assert.Equal(ShellPlacement.AnchorBottomRight, box.Placement);
    }

    [Fact]
    public void CollapsedIsTheSquareWidget()
    {
        var box = ShellGeometry.For(Shell.Collapsed);

        Assert.Equal(96, box.Width);
        Assert.Equal(96, box.Height);
        Assert.Equal(ShellPlacement.AnchorBottomRight, box.Placement);
    }

    /// <summary>
    /// Входящий и мини-звонок — одна и та же полоска. Разъехавшись, они дали бы
    /// анимацию размера на переходе «ответил», которого в модели нет.
    /// </summary>
    [Fact]
    public void IncomingAndCallBarShareTheStripGeometry()
    {
        Assert.Equal(ShellGeometry.For(Shell.Incoming), ShellGeometry.For(Shell.CallBar));
        Assert.Equal(436, ShellGeometry.For(Shell.Incoming).Width);
        Assert.Equal(132, ShellGeometry.For(Shell.Incoming).Height);
    }

    /// <summary>
    /// Ни одна поверхность не остаётся без размера. Ветка по умолчанию, возвращающая
    /// что-нибудь правдоподобное, дала бы новому Shell тихий 320×600 вместо ошибки.
    /// </summary>
    [Fact]
    public void EveryShellHasGeometry()
    {
        foreach (Shell shell in Enum.GetValues<Shell>())
        {
            var box = ShellGeometry.For(shell);
            Assert.True(box.Width > 0 && box.Height > 0, $"{shell} has no geometry");
        }
    }
}
```

- [ ] **Step 2: Запустить и убедиться, что падает**

Run: `dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q --filter "FullyQualifiedName~ShellGeometryTests"`
Expected: FAIL — `error CS0246: The type or namespace name 'Shell' could not be found`.

- [ ] **Step 3: Написать минимальную реализацию**

`OrbitalSIP/Models/Shell.cs`:

```csharp
namespace OrbitalSIP.Models;

/// <summary>
/// Поверхности окна — то, чем оно является целиком: размер, способ размещения и
/// наличие хрома.
///
/// Заменяет собой пару флагов, из которых состояние выводилось раньше: _isExpanded
/// (true в девяти местах, в том числе для полоски 436×132, панелью не являющейся) и
/// _settingsFromLogin, который однажды уже протёк в панель отвеченного звонка.
/// Поверхность протечь не может — из неё ведут только перечисленные переходы.
/// </summary>
public enum Shell
{
    /// <summary>Экран входа. Сессии нет, нижнего меню нет.</summary>
    Login,

    /// <summary>Настройки, открытые до входа. Единственный выход — обратно в <see cref="Login"/>.</summary>
    LoginSettings,

    /// <summary>Плавающий виджет 96×96.</summary>
    Collapsed,

    /// <summary>Панель 320×600 с топбаром и нижним меню. Что именно в ней — решает <see cref="NavRoute"/>.</summary>
    Panel,

    /// <summary>Полоска входящего вызова.</summary>
    Incoming,

    /// <summary>Полоска идущего разговора — «свёрнутый» эквивалент панели во время звонка.</summary>
    CallBar,
}
```

`OrbitalSIP/Models/NavRoute.cs`:

```csharp
namespace OrbitalSIP.Models;

/// <summary>
/// Что показано внутри <see cref="Shell.Panel"/>.
///
/// Отдельный тип, а не расширенный <see cref="NavTab"/>: у меню четыре слота и
/// пятого не будет. На <see cref="Call"/> попадают только через плашку возврата,
/// разворот <see cref="Shell.CallBar"/> или начало звонка — кнопки в меню для него
/// нет, и подсвечивать на нём нечего.
/// </summary>
public enum NavRoute
{
    Dialer,
    Recents,
    Tasks,
    Settings,

    /// <summary>Экран разговора. Допустим только при живом звонке — см. <c>ShellRouter</c>.</summary>
    Call,
}
```

`OrbitalSIP/Models/ShellGeometry.cs`:

```csharp
using System;

namespace OrbitalSIP.Models;

/// <summary>Как окно встаёт на экран, когда переходит на поверхность.</summary>
public enum ShellPlacement
{
    /// <summary>Держится за нижне-правый угол — там, где оператор его припарковал.</summary>
    AnchorBottomRight,

    /// <summary>Встаёт по центру рабочей области. Только для экранов входа.</summary>
    CenterOnScreen,
}

/// <summary>Размер поверхности в базовых единицах, до умножения на масштаб виджета.</summary>
public readonly record struct ShellBox(double Width, double Height, ShellPlacement Placement);

/// <summary>
/// Размер и размещение окна как функция от поверхности.
///
/// Константы пришли из MainWindow, где раздавались по девятнадцати вызовам
/// StartAnimation вручную. Масштаб (<c>_uiScale</c>) здесь не применяется намеренно:
/// он свойство экрана и настройки, а не поверхности, и остаётся за окном.
/// </summary>
public static class ShellGeometry
{
    public const double WidgetSize  = 96;
    public const double PanelWidth  = 320;
    public const double PanelHeight = 600;
    public const double StripWidth  = 436;
    public const double StripHeight = 132;

    public static ShellBox For(Shell shell) => shell switch
    {
        Shell.Login or Shell.LoginSettings =>
            new ShellBox(PanelWidth, PanelHeight, ShellPlacement.CenterOnScreen),

        Shell.Panel =>
            new ShellBox(PanelWidth, PanelHeight, ShellPlacement.AnchorBottomRight),

        Shell.Collapsed =>
            new ShellBox(WidgetSize, WidgetSize, ShellPlacement.AnchorBottomRight),

        Shell.Incoming or Shell.CallBar =>
            new ShellBox(StripWidth, StripHeight, ShellPlacement.AnchorBottomRight),

        _ => throw new ArgumentOutOfRangeException(nameof(shell), shell, "Поверхность без геометрии"),
    };
}
```

- [ ] **Step 4: Запустить и убедиться, что проходит**

Run: `dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q --filter "FullyQualifiedName~ShellGeometryTests"`
Expected: PASS, 6 тестов.

- [ ] **Step 5: Закоммитить**

```bash
git add OrbitalSIP/Models/Shell.cs OrbitalSIP/Models/NavRoute.cs OrbitalSIP/Models/ShellGeometry.cs OrbitalSIP.Tests/ShellGeometryTests.cs
git commit -m "feat(ui): surfaces, routes, and the geometry that follows from them"
```

---

## Task 2: `UiState` и его инварианты

**Files:**
- Create: `OrbitalSIP/Models/UiState.cs`
- Test: `OrbitalSIP.Tests/UiStateTests.cs`

Запись состояния плюс нормализация: два инварианта, которые редьюсер применяет к своему результату, а не к каждой строке таблицы отдельно.

- [ ] **Step 1: Написать падающий тест**

Создать `OrbitalSIP.Tests/UiStateTests.cs`:

```csharp
using OrbitalSIP.Models;
using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

/// <summary>
/// Инварианты состояния — то, что верно про любой его экземпляр, независимо от того,
/// какое событие его породило. Проверяются здесь, чтобы таблица переходов не носила
/// их в каждой строке.
/// </summary>
public class UiStateTests
{
    [Fact]
    public void WithoutCredentialsTheAppStartsOnLogin()
    {
        var s = UiState.Initial(hasCredentials: false);

        Assert.Equal(Shell.Login, s.Shell);
    }

    [Fact]
    public void WithCredentialsTheAppStartsCollapsedAndCallsCollapsedHome()
    {
        var s = UiState.Initial(hasCredentials: true);

        Assert.Equal(Shell.Collapsed, s.Shell);
        Assert.Equal(Shell.Collapsed, s.Home);
        Assert.Equal(NavRoute.Dialer, s.Route);
        Assert.Equal(NavRoute.Dialer, s.LastNonCall);
    }

    /// <summary>
    /// Экран разговора без разговора. Достижим гонкой: оператор разворачивает виджет в
    /// тот же момент, когда собеседник кладёт трубку. Инвариант снимает вопрос со всех
    /// строк таблицы сразу.
    /// </summary>
    [Fact]
    public void CallRouteIsImpossibleWithoutACall()
    {
        var s = UiState.Initial(true) with { Shell = Shell.Panel, Route = NavRoute.Call, LastNonCall = NavRoute.Tasks };

        var normalized = s.Normalize(CallState.Idle);

        Assert.Equal(NavRoute.Tasks, normalized.Route);
    }

    [Fact]
    public void CallRouteSurvivesWhileTheCallDoes()
    {
        var s = UiState.Initial(true) with { Shell = Shell.Panel, Route = NavRoute.Call, LastNonCall = NavRoute.Tasks };

        Assert.Equal(NavRoute.Call, s.Normalize(CallState.Active).Route);
        Assert.Equal(NavRoute.Call, s.Normalize(CallState.OnHold).Route);
        Assert.Equal(NavRoute.Call, s.Normalize(CallState.Ringing).Route);
    }

    /// <summary>
    /// LastNonCall, ставший Call, превратил бы откат после звонка в возврат в звонок —
    /// то есть в бесконечный экран разговора, из которого нет выхода.
    /// </summary>
    [Fact]
    public void LastNonCallNeverPointsAtTheCall()
    {
        var s = UiState.Initial(true) with { LastNonCall = NavRoute.Call };

        Assert.Equal(NavRoute.Dialer, s.Normalize(CallState.Active).LastNonCall);
    }

    /// <summary>
    /// Порядок нормализации имеет значение: сначала чинится LastNonCall, потом на него
    /// падает Route. В обратном порядке Route получил бы Call обратно.
    /// </summary>
    [Fact]
    public void BothInvariantsAppliedTogetherLandOnTheDialer()
    {
        var s = UiState.Initial(true) with { Route = NavRoute.Call, LastNonCall = NavRoute.Call };

        var normalized = s.Normalize(CallState.Idle);

        Assert.Equal(NavRoute.Dialer, normalized.Route);
        Assert.Equal(NavRoute.Dialer, normalized.LastNonCall);
    }
}
```

- [ ] **Step 2: Запустить и убедиться, что падает**

Run: `dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q --filter "FullyQualifiedName~UiStateTests"`
Expected: FAIL — `error CS0246: The type or namespace name 'UiState' could not be found`.

- [ ] **Step 3: Написать минимальную реализацию**

`OrbitalSIP/Models/UiState.cs`:

```csharp
using OrbitalSIP.Services;

namespace OrbitalSIP.Models;

/// <summary>
/// Всё, что определяет вид окна, одной записью.
///
/// Заменяет собой пять независимых переменных MainWindow (_preferredMode,
/// _isExpanded, _currentTab, _settingsFromLogin и зеркало CallState), произведение
/// которых давало около двух сотен комбинаций, осмысленных из которых были единицы.
///
/// CallState здесь отсутствует намеренно: его единственный источник — SipService, и
/// зеркало состояния звонка внутри UI уже однажды стоило DTMF-панели. Функции,
/// которым он нужен, получают его параметром.
/// </summary>
public sealed record UiState(
    Shell    Shell,
    NavRoute Route,
    NavRoute LastNonCall,
    Shell    Home,
    bool     StatusPopup)
{
    /// <summary>
    /// Состояние на старте процесса. Дом — виджет: приложение всегда открывалось
    /// свёрнутым, и вход в систему это не меняет.
    /// </summary>
    public static UiState Initial(bool hasCredentials) => new(
        Shell:       hasCredentials ? Shell.Collapsed : Shell.Login,
        Route:       NavRoute.Dialer,
        LastNonCall: NavRoute.Dialer,
        Home:        Shell.Collapsed,
        StatusPopup: false);

    /// <summary>
    /// Приводит состояние к своим инвариантам. Вызывается редьюсером на результате,
    /// а не на каждой строке таблицы.
    ///
    /// Порядок обязателен: LastNonCall чинится первым, потому что Route падает на него.
    /// </summary>
    public UiState Normalize(CallState call)
    {
        var state = this;

        if (state.LastNonCall == NavRoute.Call)
            state = state with { LastNonCall = NavRoute.Dialer };

        if (call == CallState.Idle && state.Route == NavRoute.Call)
            state = state with { Route = state.LastNonCall };

        return state;
    }
}
```

- [ ] **Step 4: Запустить и убедиться, что проходит**

Run: `dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q --filter "FullyQualifiedName~UiStateTests"`
Expected: PASS, 6 тестов.

- [ ] **Step 5: Закоммитить**

```bash
git add OrbitalSIP/Models/UiState.cs OrbitalSIP.Tests/UiStateTests.cs
git commit -m "feat(ui): the whole window as one record, with two invariants"
```

---

## Task 3: События и переходы сессии

**Files:**
- Create: `OrbitalSIP/Models/UiEvent.cs`
- Create: `OrbitalSIP/Models/ShellRouter.cs`
- Test: `OrbitalSIP.Tests/ShellRouterSessionTests.cs`

Первая треть таблицы: старт, вход, настройки до входа, истечение сессии.

- [ ] **Step 1: Написать падающий тест**

Создать `OrbitalSIP.Tests/ShellRouterSessionTests.cs`:

```csharp
using OrbitalSIP.Models;
using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

/// <summary>
/// Переходы, связанные с сессией. Отдельным классом от остальной таблицы, потому что
/// это единственная её часть, которая может оставить оператора без всего остального.
/// </summary>
public class ShellRouterSessionTests
{
    private static UiState Reduce(UiState state, UiEvent e, CallState call = CallState.Idle) =>
        ShellRouter.Reduce(state, e, call);

    private static UiState Panel(NavRoute route) =>
        UiState.Initial(true) with { Shell = Shell.Panel, Route = route, LastNonCall = route, Home = Shell.Panel };

    /// <summary>
    /// Панель на экране звонка. Отдельно от Panel(), потому что LastNonCall обязан
    /// указывать на не-звонковый route — иначе Normalize его поправит, и сравнение
    /// «ничего не изменилось» провалится по причине, к тесту не относящейся.
    /// </summary>
    private static UiState PanelOnCall(NavRoute cameFrom = NavRoute.Dialer) =>
        UiState.Initial(true) with
        {
            Shell = Shell.Panel, Route = NavRoute.Call, LastNonCall = cameFrom, Home = Shell.Panel
        };

    [Fact]
    public void LoginSucceededLandsOnTheCollapsedWidget()
    {
        var s = Reduce(UiState.Initial(false), new UiEvent.LoginSucceeded());

        Assert.Equal(Shell.Collapsed, s.Shell);
        Assert.Equal(Shell.Collapsed, s.Home);
        Assert.Equal(NavRoute.Dialer, s.Route);
        Assert.Equal(NavRoute.Dialer, s.LastNonCall);
    }

    [Fact]
    public void SettingsOpenedFromLoginIsItsOwnSurface()
    {
        var s = Reduce(UiState.Initial(false), new UiEvent.LoginSettingsRequested());

        Assert.Equal(Shell.LoginSettings, s.Shell);
    }

    /// <summary>
    /// Из настроек до входа ведёт ровно один выход, и любая кнопка меню — он же.
    /// Раньше это был флаг, который снимался только на тех выходах, о которых кто-то
    /// вспомнил, и панель отвеченного звонка могла унаследовать режим логина.
    /// </summary>
    [Theory]
    [InlineData(NavTab.Dialer)]
    [InlineData(NavTab.Recents)]
    [InlineData(NavTab.Tasks)]
    [InlineData(NavTab.Settings)]
    public void EveryTabLeavesLoginSettingsBackToLogin(NavTab tab)
    {
        var s = Reduce(UiState.Initial(false) with { Shell = Shell.LoginSettings },
                       new UiEvent.TabPressed(tab));

        Assert.Equal(Shell.Login, s.Shell);
    }

    [Fact]
    public void SavingSettingsOpenedFromLoginGoesBackToLogin()
    {
        var s = Reduce(UiState.Initial(false) with { Shell = Shell.LoginSettings },
                       new UiEvent.SettingsSaved());

        Assert.Equal(Shell.Login, s.Shell);
    }

    [Fact]
    public void SavingSettingsInsideASessionKeepsTheScreen()
    {
        var before = Panel(NavRoute.Settings);

        Assert.Equal(before, Reduce(before, new UiEvent.SettingsSaved()));
    }

    [Theory]
    [InlineData(NavRoute.Dialer)]
    [InlineData(NavRoute.Recents)]
    [InlineData(NavRoute.Tasks)]
    [InlineData(NavRoute.Settings)]
    public void AnExpiredSessionReplacesAnyScreenWithLogin(NavRoute route)
    {
        var s = Reduce(Panel(route), new UiEvent.SessionExpired());

        Assert.Equal(Shell.Login, s.Shell);
    }

    /// <summary>
    /// Логин, поставленный поверх идущего разговора, унёс бы кнопки сброса, микрофона и
    /// удержания у оператора, который ещё говорит. Диспетчер дожидается конца звонка и
    /// шлёт это же событие повторно — поэтому здесь достаточно ничего не делать.
    /// </summary>
    [Theory]
    [InlineData(CallState.Ringing)]
    [InlineData(CallState.IncomingRinging)]
    [InlineData(CallState.Active)]
    [InlineData(CallState.OnHold)]
    public void AnExpiredSessionWaitsForTheCallToEnd(CallState call)
    {
        var before = PanelOnCall();

        Assert.Equal(before, Reduce(before, new UiEvent.SessionExpired(), call));
    }
}
```

- [ ] **Step 2: Запустить и убедиться, что падает**

Run: `dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q --filter "FullyQualifiedName~ShellRouterSessionTests"`
Expected: FAIL — `error CS0246: The type or namespace name 'UiEvent' could not be found`.

- [ ] **Step 3: Написать минимальную реализацию**

`OrbitalSIP/Models/UiEvent.cs`:

```csharp
using OrbitalSIP.Services;

namespace OrbitalSIP.Models;

/// <summary>
/// Всё, от чего меняется вид окна. Ничего больше: нажатие кнопки, ответ сервиса,
/// решение оператора.
///
/// Хоткеи сюда не входят намеренно. Они адресуются звонку через SipService, а окно
/// следует за CallStateChanged, как за любым другим изменением состояния звонка —
/// единственное исключение расписано в MainWindow, где ответ на входящий поднимает
/// CallStarted после успешного AnswerAsync.
/// </summary>
public abstract record UiEvent
{
    public sealed record LoginSucceeded            : UiEvent;
    public sealed record SessionExpired            : UiEvent;
    public sealed record LoginSettingsRequested    : UiEvent;
    public sealed record SettingsSaved             : UiEvent;
    public sealed record TabPressed(NavTab Tab)    : UiEvent;
    public sealed record ReturnStripPressed        : UiEvent;
    public sealed record ExpandRequested           : UiEvent;
    public sealed record CollapseRequested         : UiEvent;
    public sealed record IncomingCall              : UiEvent;
    public sealed record IncomingDeclined          : UiEvent;

    /// <summary>Ответ на входящий или начатый исходящий — с точки зрения окна это одно и то же.</summary>
    public sealed record CallStarted               : UiEvent;

    public sealed record CallStateChanged(CallState State) : UiEvent;

    public sealed record StatusPopupToggled(bool Open)     : UiEvent;
}
```

`OrbitalSIP/Models/ShellRouter.cs`:

```csharp
using OrbitalSIP.Services;

namespace OrbitalSIP.Models;

/// <summary>
/// Единственное место, где состояние UI меняется.
///
/// Чистая функция: та же тройка на входе всегда даёт то же состояние на выходе.
/// Побочные эффекты — CallAsync, Hangup, SetStateAsync, открытие окон — остаются в
/// MainWindow; здесь о них не знают. Это и делает таблицу переходов проверяемой без
/// окна, как NavBadgeState и TaskListOutcome в этой же папке.
/// </summary>
public static class ShellRouter
{
    public static UiState Reduce(UiState state, UiEvent e, CallState call)
    {
        var next = Route(state, e, call);
        if (next.Shell != state.Shell || next.Route != state.Route)
            next = next with { StatusPopup = false };
        return next.Normalize(call);
    }

    private static UiState Route(UiState s, UiEvent e, CallState call) => e switch
    {
        UiEvent.LoginSucceeded => s with
        {
            Shell       = Shell.Collapsed,
            Home        = Shell.Collapsed,
            Route       = NavRoute.Dialer,
            LastNonCall = NavRoute.Dialer,
        },

        UiEvent.LoginSettingsRequested when s.Shell == Shell.Login =>
            s with { Shell = Shell.LoginSettings },

        UiEvent.SettingsSaved when s.Shell == Shell.LoginSettings =>
            s with { Shell = Shell.Login },

        UiEvent.TabPressed when s.Shell == Shell.LoginSettings =>
            s with { Shell = Shell.Login },

        // Живой звонок откладывает логин: диспетчер дождётся Idle и пришлёт это
        // событие ещё раз.
        UiEvent.SessionExpired when call == CallState.Idle =>
            s with { Shell = Shell.Login },

        _ => s,
    };
}
```

- [ ] **Step 4: Запустить и убедиться, что проходит**

Run: `dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q --filter "FullyQualifiedName~ShellRouterSessionTests"`
Expected: PASS, 14 тестов (`[Theory]` разворачивается в набор случаев).

- [ ] **Step 5: Закоммитить**

```bash
git add OrbitalSIP/Models/UiEvent.cs OrbitalSIP/Models/ShellRouter.cs OrbitalSIP.Tests/ShellRouterSessionTests.cs
git commit -m "feat(ui): the router, and the rows a dead session decides"
```

---

## Task 4: Табы, разворот и сворачивание

**Files:**
- Modify: `OrbitalSIP/Models/ShellRouter.cs`
- Test: `OrbitalSIP.Tests/ShellRouterNavigationTests.cs`

- [ ] **Step 1: Написать падающий тест**

Создать `OrbitalSIP.Tests/ShellRouterNavigationTests.cs`:

```csharp
using OrbitalSIP.Models;
using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

/// <summary>Навигация внутри сессии: четыре таба, разворот и сворачивание окна.</summary>
public class ShellRouterNavigationTests
{
    private static UiState Reduce(UiState state, UiEvent e, CallState call = CallState.Idle) =>
        ShellRouter.Reduce(state, e, call);

    private static UiState Panel(NavRoute route) =>
        UiState.Initial(true) with { Shell = Shell.Panel, Route = route, LastNonCall = route, Home = Shell.Panel };

    [Fact]
    public void ATabPressOpensThePanelOnThatTab()
    {
        var s = Reduce(UiState.Initial(true), new UiEvent.TabPressed(NavTab.Tasks));

        Assert.Equal(Shell.Panel, s.Shell);
        Assert.Equal(NavRoute.Tasks, s.Route);
        Assert.Equal(NavRoute.Tasks, s.LastNonCall);
    }

    /// <summary>
    /// Тап по уже горящему табу инертен. Иначе он пересобрал бы экран и унёс всё, что
    /// на нём не сохранено: host, учётные данные, язык и масштаб в настройках,
    /// наполовину набранный номер в наборе.
    /// </summary>
    [Theory]
    [InlineData(NavTab.Dialer,   NavRoute.Dialer)]
    [InlineData(NavTab.Recents,  NavRoute.Recents)]
    [InlineData(NavTab.Tasks,    NavRoute.Tasks)]
    [InlineData(NavTab.Settings, NavRoute.Settings)]
    public void PressingTheLitTabChangesNothing(NavTab tab, NavRoute route)
    {
        var before = Panel(route);

        Assert.Equal(before, Reduce(before, new UiEvent.TabPressed(tab)));
    }

    [Fact]
    public void ExpandingTheWidgetOpensThePanelAndMakesItHome()
    {
        var s = Reduce(UiState.Initial(true) with { Route = NavRoute.Recents },
                       new UiEvent.ExpandRequested());

        Assert.Equal(Shell.Panel, s.Shell);
        Assert.Equal(Shell.Panel, s.Home);
        Assert.Equal(NavRoute.Recents, s.Route);
    }

    [Fact]
    public void CollapsingWithoutACallGoesBackToTheWidget()
    {
        var s = Reduce(Panel(NavRoute.Tasks), new UiEvent.CollapseRequested());

        Assert.Equal(Shell.Collapsed, s.Shell);
        Assert.Equal(Shell.Collapsed, s.Home);
    }

    /// <summary>
    /// Route переживает уход в виджет: развернувшись обратно, оператор попадает туда,
    /// где был, а не на набор. Сегодня ReturnToPreferredMode строит набор всегда.
    /// </summary>
    [Fact]
    public void TheRouteSurvivesARoundTripThroughTheWidget()
    {
        var s = Reduce(Panel(NavRoute.Recents), new UiEvent.CollapseRequested());
        s = Reduce(s, new UiEvent.ExpandRequested());

        Assert.Equal(NavRoute.Recents, s.Route);
    }

    /// <summary>
    /// Всплывашка статусов не переживает смену экрана. Сегодня это побочный эффект
    /// SetMainContent; здесь — правило, действующее на любом переходе.
    /// </summary>
    [Fact]
    public void ChangingScreensClosesTheStatusPopup()
    {
        var before = Panel(NavRoute.Dialer) with { StatusPopup = true };

        var s = Reduce(before, new UiEvent.TabPressed(NavTab.Tasks));

        Assert.False(s.StatusPopup);
    }

    [Fact]
    public void TheStatusPopupOpensAndClosesOnItsOwnEvent()
    {
        var s = Reduce(Panel(NavRoute.Dialer), new UiEvent.StatusPopupToggled(true));
        Assert.True(s.StatusPopup);

        Assert.False(Reduce(s, new UiEvent.StatusPopupToggled(false)).StatusPopup);
    }

    /// <summary>
    /// Дом всегда одна из двух поверхностей, куда можно вернуться. Панель звонка или
    /// полоска, попавшие сюда опечаткой в одной строке таблицы, дали бы возврат в
    /// звонок, которого нет.
    /// </summary>
    [Fact]
    public void HomeIsAlwaysCollapsedOrPanel()
    {
        UiEvent[] events =
        {
            new UiEvent.LoginSucceeded(),
            new UiEvent.SessionExpired(),
            new UiEvent.LoginSettingsRequested(),
            new UiEvent.SettingsSaved(),
            new UiEvent.TabPressed(NavTab.Recents),
            new UiEvent.ReturnStripPressed(),
            new UiEvent.ExpandRequested(),
            new UiEvent.CollapseRequested(),
            new UiEvent.IncomingCall(),
            new UiEvent.IncomingDeclined(),
            new UiEvent.CallStarted(),
            new UiEvent.CallStateChanged(CallState.Idle),
            new UiEvent.StatusPopupToggled(true),
        };

        foreach (Shell shell in Enum.GetValues<Shell>())
        foreach (var e in events)
        foreach (CallState call in Enum.GetValues<CallState>())
        {
            var home = ShellRouter.Reduce(UiState.Initial(true) with { Shell = shell }, e, call).Home;
            Assert.True(home is Shell.Collapsed or Shell.Panel, $"{shell} + {e.GetType().Name} + {call} → Home={home}");
        }
    }
}
```

- [ ] **Step 2: Запустить и убедиться, что падает**

Run: `dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q --filter "FullyQualifiedName~ShellRouterNavigationTests"`
Expected: FAIL — `ATabPressOpensThePanelOnThatTab` падает на `Assert.Equal(Shell.Panel, s.Shell)`, потому что `Route` пока возвращает состояние без изменений.

- [ ] **Step 3: Написать минимальную реализацию**

В `OrbitalSIP/Models/ShellRouter.cs` добавить отображение таба в route и четыре строки в `Route`, **выше** ветки `_ => s`:

```csharp
    /// <summary>Слот меню, который читается как текущий, или null — для экрана звонка.</summary>
    public static NavTab? TabFor(NavRoute route) => route switch
    {
        NavRoute.Dialer   => NavTab.Dialer,
        NavRoute.Recents  => NavTab.Recents,
        NavRoute.Tasks    => NavTab.Tasks,
        NavRoute.Settings => NavTab.Settings,
        _                 => null,
    };

    private static NavRoute RouteFor(NavTab tab) => tab switch
    {
        NavTab.Dialer   => NavRoute.Dialer,
        NavTab.Recents  => NavRoute.Recents,
        NavTab.Tasks    => NavRoute.Tasks,
        NavTab.Settings => NavRoute.Settings,
        _               => NavRoute.Dialer,
    };
```

и в `Route`:

```csharp
        UiEvent.TabPressed t when s.Shell == Shell.Panel && RouteFor(t.Tab) == s.Route => s,

        UiEvent.TabPressed t => s with
        {
            Shell       = Shell.Panel,
            Route       = RouteFor(t.Tab),
            LastNonCall = RouteFor(t.Tab),
        },

        UiEvent.ExpandRequested when s.Shell == Shell.Collapsed =>
            s with { Shell = Shell.Panel, Home = Shell.Panel },

        UiEvent.CollapseRequested =>
            s with { Shell = Shell.Collapsed, Home = Shell.Collapsed },

        UiEvent.StatusPopupToggled p => s with { StatusPopup = p.Open },
```

Строка `TabPressed` для `LoginSettings` из Task 3 обязана остаться **выше** обеих новых строк с `TabPressed`: в режиме логина таб уводит на вход, а не открывает панель.

- [ ] **Step 4: Запустить и убедиться, что проходит**

Run: `dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q --filter "FullyQualifiedName~ShellRouter"`
Expected: PASS — оба класса тестов роутера.

- [ ] **Step 5: Закоммитить**

```bash
git add OrbitalSIP/Models/ShellRouter.cs OrbitalSIP.Tests/ShellRouterNavigationTests.cs
git commit -m "feat(ui): tabs, expand, collapse — and a route that survives the widget"
```

---

## Task 5: Звонок

**Files:**
- Modify: `OrbitalSIP/Models/ShellRouter.cs`
- Test: `OrbitalSIP.Tests/ShellRouterCallTests.cs`

Оставшаяся треть таблицы, ради которой всё затевалось.

- [ ] **Step 1: Написать падающий тест**

Создать `OrbitalSIP.Tests/ShellRouterCallTests.cs`:

```csharp
using OrbitalSIP.Models;
using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

/// <summary>
/// Звонок как отдельный route, а не как подмена таба «Набор».
///
/// Сегодня ShowDialer() во время разговора отдаёт экран звонка, поэтому «Набор» молча
/// означает «вернуться в звонок», а взять набор для второй линии или для адреса
/// перевода негде.
/// </summary>
public class ShellRouterCallTests
{
    private static UiState Reduce(UiState state, UiEvent e, CallState call) =>
        ShellRouter.Reduce(state, e, call);

    private static UiState Panel(NavRoute route, Shell home = Shell.Panel) =>
        UiState.Initial(true) with { Shell = Shell.Panel, Route = route, LastNonCall = route, Home = home };

    /// <summary>
    /// Панель на экране звонка. Отдельно от Panel(), потому что LastNonCall обязан
    /// указывать на не-звонковый route — иначе Normalize его поправит, и сравнение
    /// «ничего не изменилось» провалится по причине, к тесту не относящейся.
    /// </summary>
    private static UiState PanelOnCall(NavRoute cameFrom = NavRoute.Dialer) =>
        UiState.Initial(true) with
        {
            Shell = Shell.Panel, Route = NavRoute.Call, LastNonCall = cameFrom, Home = Shell.Panel
        };

    [Fact]
    public void APanelHomeAnswersTheCallOnTheCallRoute()
    {
        var s = Reduce(Panel(NavRoute.Tasks), new UiEvent.CallStarted(), CallState.Active);

        Assert.Equal(Shell.Panel, s.Shell);
        Assert.Equal(NavRoute.Call, s.Route);
        Assert.Equal(NavRoute.Tasks, s.LastNonCall);
    }

    [Fact]
    public void AWidgetHomeAnswersTheCallOnTheStrip()
    {
        var s = Reduce(UiState.Initial(true), new UiEvent.CallStarted(), CallState.Active);

        Assert.Equal(Shell.CallBar, s.Shell);
    }

    [Fact]
    public void AnIncomingCallTakesOverTheScreen()
    {
        var s = Reduce(Panel(NavRoute.Recents), new UiEvent.IncomingCall(), CallState.IncomingRinging);

        Assert.Equal(Shell.Incoming, s.Shell);
    }

    /// <summary>
    /// Второй вызов, пришедший во время разговора, не снимает оператора с текущего.
    /// Что при этом делает SipService — не забота этой таблицы.
    /// </summary>
    [Fact]
    public void ASecondIncomingCallDoesNotDisturbTheFirst()
    {
        var before = PanelOnCall();

        Assert.Equal(before, Reduce(before, new UiEvent.IncomingCall(), CallState.Active));
    }

    [Fact]
    public void DecliningGoesBackHome()
    {
        var s = Reduce(UiState.Initial(true) with { Shell = Shell.Incoming, Home = Shell.Collapsed },
                       new UiEvent.IncomingDeclined(), CallState.Idle);

        Assert.Equal(Shell.Collapsed, s.Shell);
    }

    [Fact]
    public void AMissedCallGoesBackHomeToo()
    {
        var s = Reduce(UiState.Initial(true) with { Shell = Shell.Incoming, Home = Shell.Panel },
                       new UiEvent.CallStateChanged(CallState.Idle), CallState.Idle);

        Assert.Equal(Shell.Panel, s.Shell);
    }

    [Fact]
    public void TheReturnStripBringsTheCallBack()
    {
        var s = Reduce(Panel(NavRoute.Tasks), new UiEvent.ReturnStripPressed(), CallState.Active);

        Assert.Equal(NavRoute.Call, s.Route);
    }

    [Fact]
    public void TheReturnStripDoesNothingWithoutACall()
    {
        var before = Panel(NavRoute.Tasks);

        Assert.Equal(before, Reduce(before, new UiEvent.ReturnStripPressed(), CallState.Idle));
    }

    /// <summary>
    /// Во время звонка «Набор» остаётся набором — вся причина существования этой работы.
    /// </summary>
    [Fact]
    public void TheDialerTabIsStillADialerDuringACall()
    {
        var s = Reduce(PanelOnCall(cameFrom: NavRoute.Tasks),
                       new UiEvent.TabPressed(NavTab.Dialer), CallState.Active);

        Assert.Equal(NavRoute.Dialer, s.Route);
    }

    /// <summary>
    /// Конец звонка на экране звонка возвращает туда, откуда оператор в него ушёл, а не
    /// в «дом». Сегодня для этого держится список исключений: Login и Settings остаются,
    /// остальные — нет, и каждый новый экран требует решения, в какой половине он лежит.
    /// </summary>
    [Fact]
    public void EndingTheCallReturnsToWhereTheOperatorCameFrom()
    {
        var s = Panel(NavRoute.Tasks);
        s = Reduce(s, new UiEvent.CallStarted(), CallState.Active);
        s = Reduce(s, new UiEvent.CallStateChanged(CallState.Idle), CallState.Idle);

        Assert.Equal(Shell.Panel, s.Shell);
        Assert.Equal(NavRoute.Tasks, s.Route);
    }

    /// <summary>
    /// А если оператор во время разговора ушёл на другой таб — конец звонка не должен
    /// его оттуда снимать. Это та строка таблицы, которая заменяет список исключений.
    /// </summary>
    [Theory]
    [InlineData(NavRoute.Recents)]
    [InlineData(NavRoute.Tasks)]
    [InlineData(NavRoute.Settings)]
    public void EndingTheCallLeavesAnyOtherScreenAlone(NavRoute route)
    {
        var s = Panel(NavRoute.Dialer);
        s = Reduce(s, new UiEvent.CallStarted(), CallState.Active);
        s = Reduce(s, new UiEvent.TabPressed(ShellRouter.TabFor(route)!.Value), CallState.Active);

        var after = Reduce(s, new UiEvent.CallStateChanged(CallState.Idle), CallState.Idle);

        Assert.Equal(Shell.Panel, after.Shell);
        Assert.Equal(route, after.Route);
    }

    [Fact]
    public void EndingTheCallOnTheStripCollapsesTheWidget()
    {
        var s = Reduce(UiState.Initial(true), new UiEvent.CallStarted(), CallState.Active);
        s = Reduce(s, new UiEvent.CallStateChanged(CallState.Idle), CallState.Idle);

        Assert.Equal(Shell.Collapsed, s.Shell);
    }

    [Fact]
    public void ExpandingTheCallStripOpensTheCallRoute()
    {
        var s = Reduce(UiState.Initial(true), new UiEvent.CallStarted(), CallState.Active);
        s = Reduce(s, new UiEvent.ExpandRequested(), CallState.Active);

        Assert.Equal(Shell.Panel, s.Shell);
        Assert.Equal(NavRoute.Call, s.Route);
        Assert.Equal(Shell.Panel, s.Home);
    }

    /// <summary>
    /// Свернуть во время звонка — тот же жест, что и свернуть без него, и он обязан так
    /// же переставить «дом». Иначе после конца звонка окно садится в виджет, а Home
    /// остаётся панелью, и следующий звонок разворачивает панель тому, кто её свернул.
    /// </summary>
    [Fact]
    public void CollapsingDuringACallMovesHomeToo()
    {
        var s = Reduce(Panel(NavRoute.Dialer), new UiEvent.CallStarted(), CallState.Active);
        s = Reduce(s, new UiEvent.CollapseRequested(), CallState.Active);

        Assert.Equal(Shell.CallBar, s.Shell);
        Assert.Equal(Shell.Collapsed, s.Home);

        s = Reduce(s, new UiEvent.CallStateChanged(CallState.Idle), CallState.Idle);
        Assert.Equal(Shell.Collapsed, s.Shell);
    }

    /// <summary>
    /// Промежуточные состояния звонка экрана не двигают: их дело — надписи и кнопки на
    /// уже открытом экране.
    /// </summary>
    [Theory]
    [InlineData(CallState.Ringing)]
    [InlineData(CallState.Active)]
    [InlineData(CallState.OnHold)]
    public void MidCallStateChangesDoNotMoveTheScreen(CallState call)
    {
        var before = PanelOnCall();

        Assert.Equal(before, Reduce(before, new UiEvent.CallStateChanged(call), call));
    }
}
```

- [ ] **Step 2: Запустить и убедиться, что падает**

Run: `dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q --filter "FullyQualifiedName~ShellRouterCallTests"`
Expected: FAIL — `APanelHomeAnswersTheCallOnTheCallRoute` падает: `Route` пока не знает `CallStarted`.

- [ ] **Step 3: Написать минимальную реализацию**

В `Route` добавить строки звонка. Порядок внутри `switch` важен: `CollapseRequested` для звонка должен стоять **выше** общей строки `CollapseRequested` из Task 4.

```csharp
        UiEvent.IncomingCall when call is CallState.Idle or CallState.IncomingRinging =>
            s with { Shell = Shell.Incoming },

        UiEvent.IncomingDeclined =>
            s with { Shell = s.Home },

        UiEvent.CallStarted =>
            s.Home == Shell.Panel
                ? s with { Shell = Shell.Panel, Route = NavRoute.Call }
                : s with { Shell = Shell.CallBar },

        UiEvent.ReturnStripPressed when call != CallState.Idle =>
            s with { Shell = Shell.Panel, Route = NavRoute.Call },

        UiEvent.ExpandRequested when s.Shell == Shell.CallBar =>
            s with { Shell = Shell.Panel, Route = NavRoute.Call, Home = Shell.Panel },

        UiEvent.CollapseRequested when call != CallState.Idle =>
            s with { Shell = Shell.CallBar, Home = Shell.Collapsed },

        UiEvent.CallStateChanged { State: CallState.Idle } when s.Shell == Shell.CallBar =>
            s with { Shell = Shell.Collapsed },

        UiEvent.CallStateChanged { State: CallState.Idle } when s.Shell == Shell.Incoming =>
            s with { Shell = s.Home },
```

Строки для `CallStateChanged(Idle)` на `Shell.Panel` нет намеренно: инвариант `Normalize` сам уводит `Route` с `Call` на `LastNonCall`, а панель на любом другом route остаётся нетронутой. Это и есть замена сегодняшнему списку исключений.

Ветка `IncomingCall` пропускает `IncomingRinging` наравне с `Idle`, потому что `SipService` успевает выставить состояние до того, как событие дойдёт до окна.

- [ ] **Step 4: Запустить и убедиться, что проходит**

Run: `dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q --filter "FullyQualifiedName~ShellRouter"`
Expected: PASS — все три класса тестов роутера.

- [ ] **Step 5: Закоммитить**

```bash
git add OrbitalSIP/Models/ShellRouter.cs OrbitalSIP.Tests/ShellRouterCallTests.cs
git commit -m "feat(ui): the call gets a route of its own, and the dialer stays a dialer"
```

---

## Task 6: Предикат плашки возврата

**Files:**
- Modify: `OrbitalSIP/Models/ShellRouter.cs`
- Test: `OrbitalSIP.Tests/ShellRouterStripTests.cs`

Плашку рисует `PanelShellView` (Task 10), но решение «показывать или нет» обязано быть проверяемым без UI.

- [ ] **Step 1: Написать падающий тест**

Создать `OrbitalSIP.Tests/ShellRouterStripTests.cs`:

```csharp
using OrbitalSIP.Models;
using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

/// <summary>
/// Плашка «идёт звонок — вернуться» — единственное, что связывает оператора с
/// разговором, пока он смотрит на другой таб. Погасшая зря, она оставляет его без
/// пути назад; горящая зря — уводит на экран звонка, которого нет.
/// </summary>
public class ShellRouterStripTests
{
    private static UiState Panel(NavRoute route) =>
        UiState.Initial(true) with { Shell = Shell.Panel, Route = route, LastNonCall = route, Home = Shell.Panel };

    [Theory]
    [InlineData(CallState.Ringing)]
    [InlineData(CallState.Active)]
    [InlineData(CallState.OnHold)]
    public void ALiveCallOnAnotherTabShowsTheStrip(CallState call)
    {
        Assert.True(ShellRouter.ShowReturnStrip(Panel(NavRoute.Tasks), call));
    }

    [Fact]
    public void NoCallMeansNoStrip()
    {
        Assert.False(ShellRouter.ShowReturnStrip(Panel(NavRoute.Tasks), CallState.Idle));
    }

    /// <summary>На самом экране звонка возвращаться некуда.</summary>
    [Fact]
    public void TheCallRouteNeedsNoStrip()
    {
        var s = Panel(NavRoute.Dialer) with { Route = NavRoute.Call };

        Assert.False(ShellRouter.ShowReturnStrip(s, CallState.Active));
    }

    /// <summary>
    /// Полоски и виджета плашка не касается — их рисует не PanelShellView. Входящий
    /// живёт на собственной поверхности, где панели нет вовсе.
    /// </summary>
    [Theory]
    [InlineData(Shell.Collapsed)]
    [InlineData(Shell.CallBar)]
    [InlineData(Shell.Incoming)]
    [InlineData(Shell.Login)]
    [InlineData(Shell.LoginSettings)]
    public void SurfacesWithoutAPanelNeverShowIt(Shell shell)
    {
        var s = Panel(NavRoute.Tasks) with { Shell = shell };

        Assert.False(ShellRouter.ShowReturnStrip(s, CallState.Active));
    }
}
```

- [ ] **Step 2: Запустить и убедиться, что падает**

Run: `dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q --filter "FullyQualifiedName~ShellRouterStripTests"`
Expected: FAIL — `error CS0117: 'ShellRouter' does not contain a definition for 'ShowReturnStrip'`.

- [ ] **Step 3: Написать минимальную реализацию**

В `ShellRouter`:

```csharp
    /// <summary>
    /// Видна ли плашка возврата к звонку.
    ///
    /// «Звонок жив» здесь — «не Idle»: исходящий гудок уже даёт оператору что-то, к чему
    /// можно вернуться. IncomingRinging до этого предиката не доходит — входящий живёт
    /// на Shell.Incoming, где панели с плашкой нет.
    /// </summary>
    public static bool ShowReturnStrip(UiState state, CallState call) =>
        state.Shell == Shell.Panel &&
        state.Route != NavRoute.Call &&
        call != CallState.Idle;
```

- [ ] **Step 4: Запустить и убедиться, что проходит**

Run: `dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q`
Expected: PASS — весь набор, включая всё, что было до этой работы.

- [ ] **Step 5: Закоммитить**

```bash
git add OrbitalSIP/Models/ShellRouter.cs OrbitalSIP.Tests/ShellRouterStripTests.cs
git commit -m "feat(ui): when the return strip is up, decided where a test can reach it"
```

---

## Task 7: Меню умеет не подсвечивать ничего

**Files:**
- Modify: `OrbitalSIP/Views/BottomNavControl.axaml.cs:112-121`

`ActiveTab` сейчас `NavTab` — не-nullable, поэтому на экране звонка `AttachNav` относит его к `Dialer` через ветку по умолчанию, и подсветка врёт. `NavRoute.Call` подсветки не имеет вовсе.

- [ ] **Step 1: Поменять тип свойства**

Заменить объявление на:

```csharp
        /// <summary>
        /// Какой таб читается как текущий, или null — когда оператор не находится ни на
        /// одном из четырёх: экран звонка достижим только через плашку возврата и в меню
        /// слота не имеет.
        /// </summary>
        public NavTab? ActiveTab
        {
            get => _activeTab;
            set
            {
                _activeTab = value;
                foreach (var (tab, button) in _buttons)
                    button.Classes.Set("active", value.HasValue && tab == value.Value);
                RefreshTabVisuals();
            }
        }
```

и поле над ним — на `private NavTab? _activeTab;`.

- [ ] **Step 2: Починить остальные чтения поля**

Run: `dotnet build OrbitalSIP/OrbitalSIP.csproj --nologo`
Expected: FAIL со списком мест, где `_activeTab` сравнивается с `NavTab`. Поправить каждое на `_activeTab == NavTab.X` (сравнение `NavTab?` с `NavTab` компилируется и даёт false для null) либо на `_activeTab.HasValue`, по смыслу места.

- [ ] **Step 3: Собрать**

Run: `dotnet build OrbitalSIP/OrbitalSIP.csproj --nologo`
Expected: SUCCESS, 0 errors.

- [ ] **Step 4: Прогнать тесты**

Run: `dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q`
Expected: PASS. `NavTabIconTests` и `NavPulseTests` трогают ту же область — если упали, разбираться до коммита.

- [ ] **Step 5: Закоммитить**

```bash
git add OrbitalSIP/Views/BottomNavControl.axaml.cs
git commit -m "feat(nav): let the bar light nothing at all"
```

---

## Task 8: `Dispatch` и `Apply` для виджета и панели

**Files:**
- Modify: `OrbitalSIP/MainWindow.axaml.cs`

Самая аккуратная задача плана. Новая машинерия ставится **рядом** со старыми `Show*`, и на неё переводятся только две поверхности из шести. Остальные пока ходят по-старому.

- [ ] **Step 1: Завести состояние и диспетчер**

Добавить поля рядом с существующими (около строки 78, где сейчас `_currentTab`):

```csharp
        /// <summary>
        /// Всё, чем окно является. Меняется только через Dispatch и только на то, что
        /// вернул ShellRouter — присваивать напрямую нельзя, иначе возвращается ровно
        /// та россыпь ручных флагов, которую эта работа убирает.
        /// </summary>
        private UiState _state = UiState.Initial(hasCredentials: false);
```

и методы (рядом с `NavigateTo`, около строки 1105):

```csharp
        /// <summary>
        /// Единственный вход в изменение состояния. Считает следующее состояние, и если
        /// оно отличается — рисует разницу.
        /// </summary>
        private void Dispatch(UiEvent e)
        {
            var next = ShellRouter.Reduce(_state, e, App.SipService.State);
            if (next == _state) return;

            var prev = _state;
            _state = next;
            Apply(prev, next);
        }

        /// <summary>
        /// Рисует разницу между двумя состояниями. Ничего не решает — все решения уже
        /// приняты в ShellRouter.
        ///
        /// prev == null означает первую отрисовку: тогда перерисовывается всё.
        /// </summary>
        private void Apply(UiState? prev, UiState next)
        {
            var box = ShellGeometry.For(next.Shell);
            var width  = box.Width  * _uiScale;
            var height = box.Height * _uiScale;

            var contentChanged = prev is null
                              || prev.Shell != next.Shell
                              || prev.Route != next.Route;

            var content = contentChanged ? BuildContent(next) : null;

            if (box.Placement == ShellPlacement.CenterOnScreen)
            {
                // Экраны входа не анимируются и не держатся за угол: они встают по центру,
                // как встают при холодном старте без учётных данных. Идущая анимация тут
                // же гасится — её следующий тик переписал бы геометрию, поставленную ниже,
                // и оставил бы логин размером с виджет.
                CancelAnimation();
                if (content != null) SetMainContent(content);
                PlaceCentered(width, height);
            }
            else if (Math.Abs(Width - width) > 1 || Math.Abs(Height - height) > 1)
            {
                _anchorX = Position.X + (int)Width;
                _anchorY = Position.Y + (int)Height;
                StartAnimation(Width, Height, width, height, content);
            }
            else if (content != null)
            {
                SetMainContent(content);
            }

            RefreshChrome(next);
            ApplyStatusPopup(next);
        }

        /// <summary>Экран, соответствующий состоянию. Строится заново на каждой смене пары (Shell, Route).</summary>
        private object BuildContent(UiState s) => s.Shell switch
        {
            Shell.Collapsed => new Views.WidgetView(),
            Shell.Panel     => BuildPanelContent(s.Route),
            _               => throw new NotSupportedException($"{s.Shell} ещё не переведён на Apply"),
        };

        private object BuildPanelContent(NavRoute route) => route switch
        {
            NavRoute.Dialer   => CreateDialerView(),
            _                 => throw new NotSupportedException($"{route} ещё не переведён на Apply"),
        };

        /// <summary>Ставит окно по центру рабочей области экрана, на котором оно сейчас.</summary>
        private void PlaceCentered(double width, double height)
        {
            var area = (Screens?.ScreenFromWindow(this) ?? Screens?.Primary)?.WorkingArea
                       ?? new PixelRect(0, 0, 1920, 1080);

            var left = area.X + (area.Width  - (int)width)  / 2;
            var top  = area.Y + (area.Height - (int)height) / 2;

            Position = new PixelPoint(left, top);
            Width    = width;
            Height   = height;
            _anchorX = left + (int)width;
            _anchorY = top  + (int)height;
        }

        /// <summary>Гасит идущую анимацию, не досчитывая её.</summary>
        private void CancelAnimation()
        {
            _animTimer?.Stop();
            _animTimer     = null;
            _animStopwatch = null;
            _pendingContent = null;
        }

        /// <summary>
        /// Раздаёт нижнему меню его состояние. Замена AttachNav: та выводила таб и режим
        /// логина из типа экрана, здесь и то и другое уже есть в UiState.
        /// </summary>
        private void RefreshChrome(UiState s)
        {
            var nav = CurrentNav();
            if (nav == null) return;

            nav.TabSelected -= OnNavTabSelected;
            nav.TabSelected += OnNavTabSelected;
            nav.ActiveTab = ShellRouter.TabFor(s.Route);
            nav.SetInCall(App.SipService.State is CallState.Active or CallState.OnHold);
            nav.SetLoginMode(s.Shell == Shell.LoginSettings);
            ApplyBadges(nav);
        }

        private void ApplyStatusPopup(UiState s)
        {
            if (s.StatusPopup) ShowStatusPopup();
            else               HideStatusPopup();
        }
```

- [ ] **Step 2: Перевести на диспетчер три события**

`ApplyBadges` сегодня проверяет `_settingsFromLogin` — заменить на `_state.Shell == Shell.LoginSettings`.

`ToggleExpanded`, `ExpandOnDoubleTap`, `CollapseWidget` заменить телами:

```csharp
        private void ToggleExpanded() =>
            Dispatch(_state.Shell == Shell.Collapsed
                ? new UiEvent.ExpandRequested()
                : new UiEvent.CollapseRequested());

        private void ExpandOnDoubleTap()
        {
            if (_state.Shell != Shell.Collapsed) return;
            Dispatch(new UiEvent.ExpandRequested());
        }

        private void CollapseWidget() => Dispatch(new UiEvent.CollapseRequested());
```

`NavigateTo(NavTab tab)` заменить на `Dispatch(new UiEvent.TabPressed(tab))` — но пока только для `NavTab.Dialer`; для остальных трёх оставить старые `ShowRecents()`, `ShowTasks()`, `ShowSettings()`, иначе `BuildPanelContent` бросит. Task 9 снимет эту заглушку.

`ShowStatusPopup` вызывается из четырёх экранов через `OnAvatarClicked` — эти вызовы заменить на `Dispatch(new UiEvent.StatusPopupToggled(true))`, а `popup.OnCloseRequested` — на `Dispatch(new UiEvent.StatusPopupToggled(false))`. Внутри самих `ShowStatusPopup`/`HideStatusPopup` `Dispatch` не звать: они теперь исполнители, а не решения.

В конструкторе, в ветке «есть учётные данные», заменить `SetMainContent(new Views.WidgetView())` на:

```csharp
                _state = UiState.Initial(hasCredentials: true);
                Apply(null, _state);
```

оставив расчёт стартовой позиции виджета выше как есть.

- [ ] **Step 3: Собрать**

Run: `dotnet build OrbitalSIP/OrbitalSIP.csproj --nologo`
Expected: SUCCESS, 0 errors.

- [ ] **Step 4: Проверить руками**

Run: `dotnet run --project OrbitalSIP/OrbitalSIP.csproj`

Пройти по списку:
- Приложение открывается виджетом 96×96 в нижне-правом углу.
- Двойной клик по виджету разворачивает панель с набором; размер анимируется.
- Кнопка «свернуть» в топбаре возвращает виджет.
- Развернуть, уйти на «Историю» (старым путём), свернуть, развернуть — открывается «История», а не набор. Это новое поведение и главный видимый признак, что состояние работает.
- Клик по аватару открывает всплывашку статусов; уход на другой таб её гасит.

- [ ] **Step 5: Закоммитить**

```bash
git add OrbitalSIP/MainWindow.axaml.cs
git commit -m "refactor(ui): the window starts rendering a state instead of deciding one"
```

---

## Task 9: Остальные четыре поверхности и снос флагов

**Files:**
- Modify: `OrbitalSIP/MainWindow.axaml.cs`

- [ ] **Step 1: Достроить `BuildContent`**

Заменить обе заглушки на полные:

```csharp
        private object BuildContent(UiState s) => s.Shell switch
        {
            Shell.Login         => CreateLoginView(),
            Shell.LoginSettings => CreateSettingsView(fromLogin: true),
            Shell.Collapsed     => new Views.WidgetView(),
            Shell.Incoming      => CreateIncomingView(App.SipService.ActiveCallerId),
            Shell.CallBar       => CreateActiveCallWidgetView(),
            Shell.Panel         => BuildPanelContent(s.Route),
            _ => throw new ArgumentOutOfRangeException(nameof(s), s.Shell, "Поверхность без экрана"),
        };

        private object BuildPanelContent(NavRoute route) => route switch
        {
            NavRoute.Dialer   => CreateDialerView(),
            NavRoute.Recents  => CreateRecentsView(),
            NavRoute.Tasks    => CreateTasksView(),
            NavRoute.Settings  => CreateSettingsView(fromLogin: false),
            NavRoute.Call     => CreateActiveCallView(),
            _ => throw new ArgumentOutOfRangeException(nameof(route), route, "Route без экрана"),
        };
```

- [ ] **Step 2: Превратить `Show*` в `Create*`**

Каждый из `ShowLogin`, `ShowRecents`, `ShowTasks`, `ShowSettings`, `ShowIncomingCall`, `ShowActiveCallWidgetView`, `ShowActiveCallView` разбирается на две части: сборка с подпиской остаётся и переименовывается в `Create*`, возвращая экран; всё, что трогало геометрию, `_isExpanded`, `_preferredMode` и `SetMainContent`, — удаляется, потому что это теперь работа `Apply`.

`CreateDialerView` уже такой — он образец.

Событийные обработчики внутри них переводятся на `Dispatch`:

| Было | Стало |
|---|---|
| `login.OnLoginSuccess` → анимация в виджет | `App.NavBadges.Start(); Dispatch(new UiEvent.LoginSucceeded());` |
| `login.OnSettingsRequested` | `Dispatch(new UiEvent.LoginSettingsRequested())` |
| `settingsView.OnMinimizeRequested` | `Dispatch(new UiEvent.CollapseRequested())` |
| `settingsView.OnSaveRequested` → `ShowLogin()` / `ShowDialer()` | сохранение и `RescaleWindow` как есть, затем `Dispatch(new UiEvent.SettingsSaved())` |
| `incoming.OnDecline` → `ReturnToPreferredMode()` | `App.SipService.Decline(); Dispatch(new UiEvent.IncomingDeclined());` |
| `incoming.OnAnswer` → ветвление по `_preferredMode` | после успешного `AnswerAsync` — `Dispatch(new UiEvent.CallStarted())`, затем `MaybeAutoOpenSurveyAsync` |
| `widget.OnExpandRequested`, `widget.OnTransferRequested` | `Dispatch(new UiEvent.ExpandRequested())` |
| `callView.OnMinimizeRequested` | `Dispatch(new UiEvent.CollapseRequested())` |
| `callView.OnHangup`, `widget.OnHangup` → `ReturnToPreferredMode()` | только `App.SipService.Hangup()`; экран сменит `CallStateChanged` |
| `r.OnCloseRequested`, `tasks.OnCloseRequested` | `Dispatch(new UiEvent.CollapseRequested())` |

Плюс одно место вне `Create*`: `HotkeyHangup` (строка 359) в своей последней ветке —
той, что срабатывает, когда ни одного экрана звонка на виду нет, — зовёт
`ReturnToPreferredMode()`. Строку удалить целиком, ничем не заменяя: `Hangup()` и
`Decline()` выше неё поднимут `CallStateChanged(Idle)`, и экран сменится оттуда, как
у любого другого способа положить трубку. Остальные три хоткея трогать не надо — они
адресуются экрану или сервису и до навигации не доходят.

- [ ] **Step 3: Перевести оставшиеся входы**

`OnCallStateChanged(CallState state)` целиком заменяется на:

```csharp
        private void OnCallStateChanged(CallState state)
        {
            // Отложенный логин: сессия умерла во время разговора, и он ждал его конца.
            // Повторное SessionExpired, а не CallStateChanged — так решение остаётся
            // одной строкой таблицы, а не вторым путём к логину.
            if (state == CallState.Idle && _sessionExpiredPending)
            {
                _sessionExpiredPending = false;
                Dispatch(new UiEvent.SessionExpired());
                CloseDialogWindows();
                return;
            }

            // Конец звонка — единственный момент, когда счётчик пропущенных мог только
            // что сдвинуться, и он же момент, когда оператор снова смотрит на меню.
            if (state == CallState.Idle) _ = App.NavBadges.RefreshNowAsync();

            Dispatch(new UiEvent.CallStateChanged(state));

            // Надписи и кнопки на уже открытом экране звонка — не смена экрана, поэтому
            // мимо Dispatch.
            var host = this.FindControl<ContentControl>("Host");
            bool isOnHold = state == CallState.OnHold;
            if (host?.Content is Views.ActiveCallView av) { av.MarkConnected(); av.SetStatus(isOnHold); }
            else if (host?.Content is Views.ActiveCallWidgetView awv) awv.SetStatus(isOnHold);

            RefreshChrome(_state);
        }
```

`StartOutgoingCall` — оставить проверку `State != CallState.Idle` и `CallAsync`, а ветвление по `_preferredMode` заменить на `Dispatch(new UiEvent.CallStarted())` **до** `await CallAsync`, чтобы экран поднялся сразу.

`OnSessionExpired` — ветку `if (App.SipService.State != CallState.Idle) { _sessionExpiredPending = true; return; }` оставить; вместо `ShowLoginAfterSessionExpiry()` вызвать `Dispatch(new UiEvent.SessionExpired())` и `CloseDialogWindows()` (метод появится в Task 12; пока завести пустым).

`ShowLoginAfterSessionExpiry` удалить целиком — центрирование теперь делает `PlaceCentered` из `Apply`, гашение анимации — `CancelAnimation`.

Конструктор, ветка без учётных данных: удалить ручную геометрию и `ShowLogin()`, оставить

```csharp
                _state = UiState.Initial(hasCredentials: false);
                Apply(null, _state);
```

- [ ] **Step 4: Удалить то, что осталось без хозяина**

Удалить целиком: `_isExpanded`, `_preferredMode`, `enum PreferredMode`, `_settingsFromLogin`, `_currentTab`, `AttachNav`, `NavigateTo`, `ReturnToPreferredMode`, `ExpandWidget`, `ShowDialer`, `RefreshNavCallState`, константы `BaseWidgetSize`, `BaseExpandedWidth`, `BaseExpandedHeight`, `BaseIncomingWidth`, `BaseIncomingHeight` и свойства `WidgetSize`, `ExpandedWidth`, `ExpandedHeight`, `IncomingWidth`, `IncomingHeight` (их заменил `ShellGeometry`).

`OnNavTabSelected` теряет `NavigateTo` и становится:

```csharp
        private void OnNavTabSelected(object? sender, NavTab tab) => Dispatch(new UiEvent.TabPressed(tab));
```

`AttachNav` зовётся из трёх мест — строки 917 (парковка контента в оверлей на старте анимации), 976 (`SetMainContent`) и 1153 (`CompleteAnimatedContentSwap`). Все три заменить на `RefreshChrome(_state)`. Проверить, что ни одного не осталось:

```bash
git grep -n "AttachNav" -- OrbitalSIP
```

`RescaleWindow` продолжает считать `ratio` от текущих `Width`/`Height` — `ShellGeometry` ему не нужен, трогать не надо.

- [ ] **Step 5: Собрать и прогнать тесты**

Run: `dotnet build OrbitalSIP/OrbitalSIP.csproj --nologo`
Expected: SUCCESS, 0 errors, 0 warnings про недостижимый код.

Run: `dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q`
Expected: PASS.

- [ ] **Step 6: Проверить руками**

Run: `dotnet run --project OrbitalSIP/OrbitalSIP.csproj`

- Холодный старт без сохранённых данных — логин по центру экрана.
- «Настройки» из логина, любая кнопка меню — обратно на логин.
- Вход — виджет в углу.
- Входящий звонок — полоска; отбой возвращает виджет.
- Ответ при свёрнутом виджете — мини-полоска звонка; разворот — панель звонка.
- Ответ при развёрнутой панели — сразу панель звонка.
- Сброс — возврат туда, откуда пришёл звонок.
- Уйти во время разговора на «Задачи», положить трубку — «Задачи» остаются на экране.

- [ ] **Step 7: Закоммитить**

```bash
git add OrbitalSIP/MainWindow.axaml.cs
git commit -m "refactor(ui): four hand-kept flags, gone — the state already knew"
```

---

## Task 10: `PanelShellView` — хром в одном месте

**Files:**
- Create: `OrbitalSIP/Views/PanelShellView.axaml`, `OrbitalSIP/Views/PanelShellView.axaml.cs`
- Modify: `OrbitalSIP/Views/ExpandedView.axaml{,.cs}`, `RecentsView.axaml{,.cs}`, `TasksView.axaml{,.cs}`, `SettingsView.axaml{,.cs}`, `ActiveCallView.axaml{,.cs}`
- Modify: `OrbitalSIP/MainWindow.axaml.cs`

Единственная задача плана с риском визуальной регрессии. Пять экранов подогнаны под собственные копии топбара и меню, включая компенсирующий `Margin="-20,20,-20,-20"` в `SettingsView`. Поэтому — по экрану за коммит.

- [ ] **Step 1: Создать контейнер**

`OrbitalSIP/Views/PanelShellView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:Views="clr-namespace:OrbitalSIP.Views"
             x:Class="OrbitalSIP.Views.PanelShellView">
  <Grid RowDefinitions="Auto,Auto,*,Auto">
    <Views:TopBarControl     Name="TopBar"  Grid.Row="0" />
    <Views:CallReturnStrip   Name="Strip"   Grid.Row="1" IsVisible="False" />
    <ContentControl          Name="Body"    Grid.Row="2" />
    <Views:BottomNavControl  Name="Nav"     Grid.Row="3" />
  </Grid>
</UserControl>
```

`OrbitalSIP/Views/PanelShellView.axaml.cs`:

```csharp
using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace OrbitalSIP.Views
{
    /// <summary>
    /// Хром панели: топбар, плашка возврата к звонку, содержимое и нижнее меню.
    ///
    /// До этого топбар и меню были продублированы в разметке пяти экранов, и плашка
    /// потребовала бы шестого дубля — а вместе с ним и пяти мест, где её надо было бы
    /// научить показываться и прятаться.
    /// </summary>
    public partial class PanelShellView : UserControl
    {
        public PanelShellView() => InitializeComponent();

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        public object? Body
        {
            get => this.FindControl<ContentControl>("Body")?.Content;
            set { var host = this.FindControl<ContentControl>("Body"); if (host != null) host.Content = value; }
        }

        public TopBarControl?    TopBar => this.FindControl<TopBarControl>("TopBar");
        public BottomNavControl? Nav    => this.FindControl<BottomNavControl>("Nav");

        public void SetReturnStrip(bool visible, string caller, DateTime? startedAt)
        {
            var strip = this.FindControl<CallReturnStrip>("Strip");
            if (strip == null) return;

            strip.IsVisible = visible;
            if (visible) strip.Show(caller, startedAt);
            else         strip.Stop();
        }
    }
}
```

`CallReturnStrip` появится в Task 11 — до тех пор закомментировать его строку в XAML и тело `SetReturnStrip`, чтобы задача собиралась независимо.

- [ ] **Step 2: Перевести `RecentsView`**

`RecentsView.axaml:13` — `RowDefinitions="Auto,Auto,*,Auto"`. Строка 16 — `<Views:TopBarControl Name="TopBar" />` в нулевой строке сетки, `BottomNavControl` — в `Grid.Row="3"`. Удалить оба элемента, заменить определение на `RowDefinitions="Auto,*"` и сдвинуть оставшиеся: `Grid.Row="1"` → `0`, `Grid.Row="2"` → `1`.

В `RecentsView.axaml.cs` удалить блок подписки на `TopBar` (`OnMinimizeRequested`, `OnAvatarClicked`, `OnCloseRequested`) — теперь их поднимает `PanelShellView` — вместе с событиями `OnCloseRequested` и `OnExitAppRequested` самого экрана, если после этого на них никто не подписан. Найти блок:

```bash
git grep -n "TopBar" -- OrbitalSIP/Views/RecentsView.axaml.cs
```

В `MainWindow.BuildPanelContent` обернуть результат:

```csharp
        private object BuildPanelContent(NavRoute route)
        {
            var shell = new Views.PanelShellView { Body = CreateRouteBody(route) };

            if (shell.TopBar is { } bar)
            {
                bar.OnMinimizeRequested += (_, __) => Dispatch(new UiEvent.CollapseRequested());
                bar.OnAvatarClicked     += (_, __) => Dispatch(new UiEvent.StatusPopupToggled(true));
                bar.OnCloseRequested    += (_, __) => ShutdownApp();
            }

            return shell;
        }

        private object CreateRouteBody(NavRoute route) => route switch
        {
            NavRoute.Dialer   => CreateDialerView(),
            NavRoute.Recents  => CreateRecentsView(),
            NavRoute.Tasks    => CreateTasksView(),
            NavRoute.Settings => CreateSettingsView(fromLogin: false),
            NavRoute.Call     => CreateActiveCallView(),
            _ => throw new ArgumentOutOfRangeException(nameof(route), route, "Route без экрана"),
        };
```

`CurrentNav()` продолжает искать `BottomNavControl` по логическому дереву и находит его в `PanelShellView` — менять не надо.

`Shell.LoginSettings` оборачивать в `PanelShellView` **тоже** (нижнее меню на нём есть, оно в режиме логина), а `Shell.Login` — нет.

Run: `dotnet build OrbitalSIP/OrbitalSIP.csproj --nologo` → SUCCESS.
Run: `dotnet run --project OrbitalSIP/OrbitalSIP.csproj` → открыть «Историю», сверить с любым непереведённым экраном: отступы сверху и снизу, высота списка, положение аватара.

```bash
git add OrbitalSIP/Views/PanelShellView.axaml OrbitalSIP/Views/PanelShellView.axaml.cs OrbitalSIP/Views/RecentsView.axaml OrbitalSIP/Views/RecentsView.axaml.cs OrbitalSIP/MainWindow.axaml.cs
git commit -m "refactor(ui): one panel chrome, and Recents is the first to drop its copy"
```

- [ ] **Step 3: Перевести `TasksView`**

`TasksView.axaml:33` — `RowDefinitions="Auto,Auto,*,Auto"`, ровно та же форма, что у истории. Строка 35 — топбар в нулевой строке, меню — в `Grid.Row="3"`. Удалить оба, заменить на `RowDefinitions="Auto,*"`, сдвинуть `Grid.Row="1"` → `0`, `Grid.Row="2"` → `1`.

В `TasksView.axaml.cs` удалить подписку на `TopBar` тем же способом.

Собрать, открыть «Задачи», сверить высоту списка с непереведённым экраном, закоммитить:

```bash
git add OrbitalSIP/Views/TasksView.axaml OrbitalSIP/Views/TasksView.axaml.cs
git commit -m "refactor(ui): Tasks drops its copy of the chrome"
```

- [ ] **Step 4: Перевести `ExpandedView`**

`ExpandedView.axaml:14` — `RowDefinitions="Auto,Auto,Auto,Auto,Auto,*,Auto"`, семь строк: топбар в нулевой (строка 18), меню в `Grid.Row="6"`. Удалить оба, заменить на `RowDefinitions="Auto,Auto,Auto,Auto,*"` и сдвинуть все оставшиеся на единицу вверх: `1` → `0`, `2` → `1`, `3` → `2`, `4` → `3`, `5` → `4`.

`ExpandedView.axaml.cs:29` — подписка на топбар лежит одной строкой внутри конструктора, вместе с ней уходят собственные события экрана `OnCloseRequested`, `OnAvatarClicked`, `OnExitAppRequested` (строки 134–136), если после этого на них никто не подписан. `OutgoingCallRequested` остаётся — это набор, а не хром.

```bash
git add OrbitalSIP/Views/ExpandedView.axaml OrbitalSIP/Views/ExpandedView.axaml.cs
git commit -m "refactor(ui): the dialer drops its copy of the chrome"
```

- [ ] **Step 5: Перевести `SettingsView`**

`SettingsView.axaml:13` — `RowDefinitions="Auto,*,Auto,Auto"`. Здесь оба элемента хрома несут компенсирующие отрицательные отступы: топбар (строка 16) — `Grid.Row="0" Margin="-20,-20,-20,10"`, меню — `Grid.Row="3" Margin="-20,20,-20,-20"`. Они гасили внутренний `Padding` самого экрана, которого в `PanelShellView` нет, поэтому уходят вместе с элементами.

Заменить определение на `RowDefinitions="*,Auto"`, сдвинуть `Grid.Row="1"` → `0`, `Grid.Row="2"` → `1`.

Смотреть глазами внимательнее, чем на остальных четырёх: это единственный экран, где отступы были подогнаны в обе стороны. Проверить, что содержимое настроек не прилипло к краям, меню не наезжает на кнопку сохранения и не оторвалось от нижнего края.

```bash
git add OrbitalSIP/Views/SettingsView.axaml OrbitalSIP/Views/SettingsView.axaml.cs
git commit -m "refactor(ui): Settings drops its copy of the chrome, and the margins that patched it"
```

- [ ] **Step 6: Перевести `ActiveCallView`**

`ActiveCallView.axaml:9` — `RowDefinitions="*,Auto"`. Топбар (строка 13) сидит внутри контейнера нулевой строки с `Margin="0,0,0,12"` — уходит вместе с этим отступом; меню — `Grid.Row="1"`. После удаления обоих остаётся `RowDefinitions="*"`.

В `ActiveCallView.axaml.cs` удалить подписку на топбар, включая `OnAvatarClicked`, который сегодня уходит в `MainWindow` через `WireActiveCallView`. Соответствующую строку в `WireActiveCallView` тоже убрать — аватар теперь на `PanelShellView`.

```bash
git add OrbitalSIP/Views/ActiveCallView.axaml OrbitalSIP/Views/ActiveCallView.axaml.cs OrbitalSIP/MainWindow.axaml.cs
git commit -m "refactor(ui): the call screen drops its copy of the chrome"
```

- [ ] **Step 7: Прогнать всё**

Run: `dotnet build OrbitalSIP/OrbitalSIP.csproj --nologo` → SUCCESS
Run: `dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q` → PASS

---

## Task 11: Плашка возврата к звонку

**Files:**
- Create: `OrbitalSIP/Views/CallReturnStrip.axaml`, `OrbitalSIP/Views/CallReturnStrip.axaml.cs`
- Modify: `OrbitalSIP/Views/PanelShellView.axaml{,.cs}`, `OrbitalSIP/MainWindow.axaml.cs`

- [ ] **Step 1: Создать контрол**

`OrbitalSIP/Views/CallReturnStrip.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="OrbitalSIP.Views.CallReturnStrip">
  <Border Name="Root"
          Background="#1E7A3E"
          Padding="12,6"
          Cursor="Hand">
    <Grid ColumnDefinitions="*,Auto">
      <TextBlock Name="Caller"  Grid.Column="0" Foreground="White" FontSize="12"
                 TextTrimming="CharacterEllipsis" VerticalAlignment="Center" />
      <TextBlock Name="Elapsed" Grid.Column="1" Foreground="White" FontSize="12"
                 FontFamily="Consolas" VerticalAlignment="Center" />
    </Grid>
  </Border>
</UserControl>
```

`OrbitalSIP/Views/CallReturnStrip.axaml.cs`:

```csharp
using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace OrbitalSIP.Views
{
    /// <summary>
    /// «Идёт звонок — вернуться». Единственное, что связывает оператора с разговором,
    /// пока он смотрит на другой таб.
    ///
    /// Таймер считает от SipService.ActiveCallStartedAt, а не от собственной точки
    /// отсчёта: свой счётчик разъехался бы с таймером на экране звонка на всё время,
    /// что оператор потратил на дорогу до этого таба.
    /// </summary>
    public partial class CallReturnStrip : UserControl
    {
        private readonly DispatcherTimer _tick;
        private DateTime? _startedAt;

        public event EventHandler? OnReturnRequested;

        public CallReturnStrip()
        {
            InitializeComponent();

            _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _tick.Tick += (_, __) => Redraw();

            if (this.FindControl<Border>("Root") is { } root)
                root.PointerPressed += (_, __) => OnReturnRequested?.Invoke(this, EventArgs.Empty);

            // Таймер, переживший свой экран, держал бы ссылку на него до конца процесса.
            DetachedFromVisualTree += (_, __) => Stop();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        public void Show(string caller, DateTime? startedAt)
        {
            _startedAt = startedAt;
            if (this.FindControl<TextBlock>("Caller") is { } c) c.Text = caller;
            Redraw();
            _tick.Start();
        }

        public void Stop() => _tick.Stop();

        private void Redraw()
        {
            if (this.FindControl<TextBlock>("Elapsed") is not { } label) return;

            var elapsed = _startedAt.HasValue ? DateTime.Now - _startedAt.Value : TimeSpan.Zero;
            label.Text = $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";
        }
    }
}
```

- [ ] **Step 2: Включить в `PanelShellView`**

Раскомментировать строку `<Views:CallReturnStrip Name="Strip" .../>` и тело `SetReturnStrip` из Task 10, добавив проброс события:

```csharp
        public event EventHandler? OnReturnRequested;
```

и в конструкторе `PanelShellView`:

```csharp
            if (this.FindControl<CallReturnStrip>("Strip") is { } strip)
                strip.OnReturnRequested += (_, __) => OnReturnRequested?.Invoke(this, EventArgs.Empty);
```

- [ ] **Step 3: Показывать её из `Apply`**

В `BuildPanelContent` подписаться:

```csharp
            shell.OnReturnRequested += (_, __) => Dispatch(new UiEvent.ReturnStripPressed());
```

В `RefreshChrome` добавить в конец:

```csharp
            if ((this.FindControl<ContentControl>("Host")?.Content) is Views.PanelShellView panel)
                panel.SetReturnStrip(
                    ShellRouter.ShowReturnStrip(s, App.SipService.State),
                    App.SipService.ActiveCallerId,
                    App.SipService.ActiveCallStartedAt);
```

- [ ] **Step 4: Собрать и прогнать тесты**

Run: `dotnet build OrbitalSIP/OrbitalSIP.csproj --nologo` → SUCCESS
Run: `dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q` → PASS

- [ ] **Step 5: Проверить руками**

Run: `dotnet run --project OrbitalSIP/OrbitalSIP.csproj`

- Позвонить, уйти с экрана звонка на «Задачи» — сверху появилась плашка с номером и тикающим таймером.
- Таймер на плашке совпадает с таймером на экране звонка (вернуться и сверить).
- Тап по плашке возвращает на экран звонка; сама плашка там не видна.
- Нажать таб «Набор» во время звонка — открывается набор, а не экран звонка. Это то, ради чего работа делалась.
- Положить трубку с «Задач» — плашка исчезла, «Задачи» на месте.
- Списки на «Задачах» и «Истории» не обрезаны снизу из-за высоты плашки.

- [ ] **Step 6: Закоммитить**

```bash
git add OrbitalSIP/Views/CallReturnStrip.axaml OrbitalSIP/Views/CallReturnStrip.axaml.cs OrbitalSIP/Views/PanelShellView.axaml OrbitalSIP/Views/PanelShellView.axaml.cs OrbitalSIP/MainWindow.axaml.cs
git commit -m "feat(call): a way back to the call that is not the dialer tab"
```

---

## Task 12: Правила жизни окон-диалогов

**Files:**
- Modify: `OrbitalSIP/MainWindow.axaml.cs`
- Modify: `OrbitalSIP/Views/TaskWindowLauncher.cs`, `SurveyWindowLauncher.cs`, `ScriptsWindowLauncher.cs`

Пять немодальных окон: задача, анкета, скрипты, SMS с экрана звонка, SMS из истории. Смена экрана и конец звонка их не трогают — постобработка живёт дольше разговора. Истечение сессии закрывает все: их содержимое принадлежит сессии, которой больше нет.

- [ ] **Step 1: Дать лаунчерам возможность закрыть своё окно**

В каждый из трёх лаунчеров добавить:

```csharp
        /// <summary>Закрывает открытое окно, если оно есть. Зовётся при истечении сессии.</summary>
        public static void CloseIfOpen() => _current?.Close();
```

`SmsComposeDialog` открывается напрямую из `ActiveCallView` и `RecentsView`, минуя лаунчер, — его экземпляры хранятся в `_activeSmsDialog` и `_historySmsDialog` этих экранов. Их закрывает `Window.OwnedWindows`, см. следующий шаг.

- [ ] **Step 2: Закрывать всё при истечении сессии**

В `MainWindow` заполнить заглушку из Task 9:

```csharp
        /// <summary>
        /// Закрывает все окна-диалоги. Только при истечении сессии: их содержимое
        /// принадлежит сессии, которой больше нет, и отправить из них уже нечего.
        ///
        /// Смена экрана и конец звонка сюда не ходят намеренно — постобработка живёт
        /// дольше разговора, а недописанный черновик SMS дороже консистентности.
        /// </summary>
        private void CloseDialogWindows()
        {
            Views.TaskWindowLauncher.CloseIfOpen();
            Views.SurveyWindowLauncher.CloseIfOpen();
            Views.ScriptsWindowLauncher.CloseIfOpen();

            // SMS-окна открываются экранами напрямую, без лаунчера, но владельцем у них
            // всё равно это окно.
            foreach (var owned in OwnedWindows.ToArray())
                if (owned is Views.SmsComposeDialog) owned.Close();
        }
```

- [ ] **Step 3: Прятать их вместе с главным окном**

`this.Closing` уже перехватывает закрытие и прячет окно. Добавить туда же и в обработчик трея:

```csharp
        private void HideToTray()
        {
            foreach (var owned in OwnedWindows.ToArray()) owned.Hide();
            Hide();
        }

        private void ShowFromTray()
        {
            Show();
            Activate();
            foreach (var owned in OwnedWindows.ToArray()) owned.Show();
        }
```

и вызывать их из `App.axaml.cs` (`MenuHide_Click`, `MenuShow_Click`, `TrayIcon_Clicked`) вместо прямых `Hide()`/`Show()`.

- [ ] **Step 4: Собрать и прогнать тесты**

Run: `dotnet build OrbitalSIP/OrbitalSIP.csproj --nologo` → SUCCESS
Run: `dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q` → PASS. `SingleWindowGuardTests` трогают ту же область.

- [ ] **Step 5: Проверить руками**

- Открыть задачу с экрана звонка, положить трубку — окно задачи осталось.
- Уйти на «Историю» — окно задачи осталось.
- Свернуть в трей и вернуть — окно задачи вернулось вместе с главным.
- Открыть задачу второй раз — поднимается уже открытая, второй не появляется.

- [ ] **Step 6: Закоммитить**

```bash
git add OrbitalSIP/MainWindow.axaml.cs OrbitalSIP/Views/TaskWindowLauncher.cs OrbitalSIP/Views/SurveyWindowLauncher.cs OrbitalSIP/Views/ScriptsWindowLauncher.cs OrbitalSIP/App.axaml.cs
git commit -m "feat(windows): dialogs outlive the call, not the session"
```

---

## Task 13: Финальная проверка

**Files:** —

- [ ] **Step 1: Собрать оба проекта**

Run: `dotnet build vv-phone-widget.sln --nologo`
Expected: SUCCESS, 0 errors. Тестовый проект собирается вместе с приложением — сборка только `OrbitalSIP.csproj` его пропускает.

- [ ] **Step 2: Прогнать весь набор тестов**

Run: `dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q`
Expected: PASS. Записать итоговое число тестов — оно понадобится в описании PR.

- [ ] **Step 3: Убедиться, что флагов не осталось**

Run: `git grep -n "_isExpanded\|_preferredMode\|_settingsFromLogin\|_currentTab\|PreferredMode" -- OrbitalSIP`
Expected: пусто.

Run: `git grep -n "ShowDialer\|ReturnToPreferredMode\|AttachNav\|ShowLoginAfterSessionExpiry" -- OrbitalSIP`
Expected: пусто.

- [ ] **Step 4: Пройти сценарии целиком**

Run: `dotnet run --project OrbitalSIP/OrbitalSIP.csproj`

| Сценарий | Ожидание |
|---|---|
| Холодный старт без данных | Логин по центру экрана |
| «Настройки» из логина → любая кнопка меню | Обратно на логин |
| Вход | Виджет в нижне-правом углу |
| Двойной клик по виджету | Панель с набором |
| Уйти на «Задачи», свернуть, развернуть | «Задачи», а не набор |
| Входящий → отбой | Возврат туда, где был |
| Входящий → ответ при свёрнутом виджете | Мини-полоска звонка |
| Разворот мини-полоски | Панель звонка, ни один таб не подсвечен |
| Во время звонка нажать «Набор» | Набор, а не экран звонка |
| Во время звонка — плашка сверху | Номер и таймер, совпадающий с экраном звонка |
| Тап по плашке | Экран звонка |
| Сброс с «Задач» | «Задачи» на месте, плашки нет |
| Сброс с экрана звонка | Экран, с которого ушли в звонок |
| Свернуть во время звонка, дождаться конца | Виджет; следующий звонок — снова полоской |
| Открытая задача при истечении сессии | Окно закрылось, логин по центру |

- [ ] **Step 5: Обновить счётчик в спеке и отправить ветку**

```bash
git push -u origin feat/ui-state-model
```

---

## Что осталось за пределами плана

Записано, чтобы не всплыло как «забыли»:

- **Кэш экранов.** Экраны по-прежнему пересоздаются на каждом переходе; набранный номер и позиция скролла теряются. Решено сознательно в спеке.
- **Второй входящий.** Таблица говорит «не двигать экран», но что при этом делает `SipService`, не покрыто.
- **Вторая линия.** Набор во время звонка теперь доступен, но что произойдёт при нажатии «позвонить» с активной линией — вопрос к `SipService`, не к этой работе.
