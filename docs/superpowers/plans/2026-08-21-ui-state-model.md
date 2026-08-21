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

## Если сборка не может записать exe

`OrbitalSIP.exe` часто запущен — это рабочий софтфон владельца репозитория, иногда под
отладчиком Visual Studio. Пока он живёт, `dotnet build` и `dotnet test` в конфигурации
Debug не могут перезаписать свой вывод и падают на локе файла.

Не убивать процесс и не подставлять `-p:BaseOutputPath` — второе оставляет мусорные
каталоги в обоих проектах. Добавить `-c Release`: у неё отдельная директория вывода,
с отлаживаемым процессом она не пересекается, а код и csproj те же самые.

```bash
dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q -c Release
```

## Про окончания строк

Не тратить на них внимание. В репозитории нет `.gitattributes`, зато стоит
`core.autocrlf=true`: объектная база хранит LF для всех файлов без исключения, а CRLF
появляется на диске при checkout. Свежесозданный файл до первого checkout лежит с LF и
выглядит непохожим на соседей — это артефакт рабочей копии, а не расхождение в коммите.
Проверять `hexdump` рабочего дерева и гонять `unix2dos` не нужно ни исполнителю, ни
ревьюеру.

## Что уже есть в репозитории

Читать до начала, чтобы не изобретать заново:

- `OrbitalSIP/Models/NavTab.cs` — `enum NavTab { Dialer, Recents, Tasks, Settings }`, четыре слота меню.
- `OrbitalSIP/Services/SipService.cs:15` — `enum CallState { Idle, Ringing, IncomingRinging, Active, OnHold }`. `Ringing` — исходящий гудок, `IncomingRinging` — входящий.
- `OrbitalSIP/Models/NavBadgeState.cs`, `OrbitalSIP/Services/TaskListOutcome.cs`, `OrbitalSIP/Services/WidgetScale.cs` — образец того, как в этом проекте выглядит чистая модель под тесты: без сети, без таймеров, без Avalonia. Обратить внимание: граница между `Models` и `Services` здесь проведена нечётко, и два из трёх лежат в `Services`. Новые файлы этой работы идут в `Models` по образцу `NavBadgeState`.
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
/// The window's size and placement belong to the surface, not to whichever method builds
/// it. Login centres on the screen and everything else holds the bottom-right corner;
/// today that rule is written down only inside the two methods that build login, and any
/// third way in loses it.
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
    /// The incoming call and the in-call strip are the same strip. Letting them drift
    /// apart would animate a resize across the "answered" transition — a resize the model
    /// does not have.
    /// </summary>
    [Fact]
    public void IncomingAndCallBarShareTheStripGeometry()
    {
        Assert.Equal(ShellGeometry.For(Shell.Incoming), ShellGeometry.For(Shell.CallBar));
        Assert.Equal(436, ShellGeometry.For(Shell.Incoming).Width);
        Assert.Equal(132, ShellGeometry.For(Shell.Incoming).Height);
    }

    /// <summary>
    /// No surface is left without a size. A default branch returning something plausible
    /// would hand a newly added Shell a silent 320×600 instead of an error.
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
/// The window's surfaces — what it is taken as a whole: its size, how it is placed, and
/// whether it carries chrome.
///
/// Replaces the pair of flags the state used to be inferred from: _isExpanded, set true
/// in nine places including the 436×132 strip that is not a panel at all, and
/// _settingsFromLogin, which had already leaked into an answered call's panel once. A
/// surface cannot leak — the only ways out of one are the transitions written down.
/// </summary>
public enum Shell
{
    /// <summary>The login screen. No session, no bottom bar.</summary>
    Login,

    /// <summary>Settings opened before signing in. The one way out is back to <see cref="Login"/>.</summary>
    LoginSettings,

    /// <summary>The floating 96×96 widget.</summary>
    Collapsed,

    /// <summary>The 320×600 panel, with a top bar and a bottom bar. What is inside it is <see cref="NavRoute"/>'s decision.</summary>
    Panel,

    /// <summary>The incoming-call strip.</summary>
    Incoming,

    /// <summary>The strip for a call in progress — the "collapsed" equivalent of the panel while a call runs.</summary>
    CallBar,
}
```

`OrbitalSIP/Models/NavRoute.cs`:

```csharp
namespace OrbitalSIP.Models;

/// <summary>
/// What is on screen inside <see cref="Shell.Panel"/>.
///
/// A type of its own rather than a widened <see cref="NavTab"/>: the bar has four slots
/// and there will not be a fifth. <see cref="Call"/> is reached only through the return
/// strip, by expanding <see cref="Shell.CallBar"/>, or by a call starting — the bar has
/// no button for it, and while it is up there is nothing to light.
/// </summary>
public enum NavRoute
{
    Dialer,
    Recents,
    Tasks,
    Settings,

    /// <summary>The call screen. Legal only while a call is live — see <c>ShellRouter</c>.</summary>
    Call,
}
```

`OrbitalSIP/Models/ShellGeometry.cs`:

```csharp
using System;

namespace OrbitalSIP.Models;

/// <summary>How the window puts itself on screen when it moves to a surface.</summary>
public enum ShellPlacement
{
    /// <summary>Holds the bottom-right corner — where the operator parked it.</summary>
    AnchorBottomRight,

    /// <summary>Centres on the work area. Login screens only.</summary>
    CenterOnScreen,
}

/// <summary>A surface's size in base units, before the widget scale multiplies it.</summary>
public readonly record struct ShellBox(double Width, double Height, ShellPlacement Placement);

/// <summary>
/// The window's size and placement as a function of its surface.
///
/// The constants came from MainWindow, where they were handed out by hand across nineteen
/// StartAnimation calls. The scale (<c>_uiScale</c>) is deliberately not applied here: it
/// is a property of the screen and of the setting, not of the surface, and it stays with
/// the window.
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

        _ => throw new ArgumentOutOfRangeException(nameof(shell), shell, "Surface has no geometry"),
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
/// The invariants of the state — what is true of any instance of it, whichever event
/// produced it. Checked here so the transition table does not have to carry them in
/// every row.
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
    /// The call screen with no call behind it. Reachable by a race: the operator expands
    /// the widget at the moment the other side hangs up. The invariant settles that for
    /// every row of the table at once.
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
    /// A call ending while the operator is on some other tab must leave them there. This is
    /// the guarantee the transition table has no row for — Task 5 leaves it to Normalize on
    /// purpose — and it is the only case that tells "fall back when Route is Call" apart
    /// from "fall back whenever the call is over". Review caught it missing by deleting the
    /// Route check and watching all 658 tests stay green.
    /// </summary>
    [Fact]
    public void ANonCallRouteSurvivesTheCallEndingUnderIt()
    {
        var s = UiState.Initial(true) with { Route = NavRoute.Tasks, LastNonCall = NavRoute.Recents };

        Assert.Equal(NavRoute.Tasks, s.Normalize(CallState.Idle).Route);
    }

    /// <summary>
    /// A LastNonCall that had become Call would turn the fall-back after a call into a
    /// return to the call — an endless call screen with no way out of it.
    /// </summary>
    [Fact]
    public void LastNonCallNeverPointsAtTheCall()
    {
        var s = UiState.Initial(true) with { LastNonCall = NavRoute.Call };

        Assert.Equal(NavRoute.Dialer, s.Normalize(CallState.Active).LastNonCall);
    }

    /// <summary>
    /// The order of normalization is load-bearing: LastNonCall is repaired first, and then
    /// Route falls back onto it. The other way round hands Route its Call straight back.
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
Expected: FAIL — `error CS0103: The name 'UiState' does not exist in the current context`, once per `UiState.Initial(...)`. CS0103 rather than the CS0246 of the previous task because this test never names the type in a type position: it only calls a static member on it, and Roslyn reports an unresolved simple name in an expression as CS0103.

- [ ] **Step 3: Написать минимальную реализацию**

`OrbitalSIP/Models/UiState.cs`:

```csharp
using OrbitalSIP.Services;

namespace OrbitalSIP.Models;

/// <summary>
/// Everything that decides what the window looks like, in one record.
///
/// Replaces MainWindow's five independent variables (_preferredMode, _isExpanded,
/// _currentTab, _settingsFromLogin and a mirror of CallState), whose product came to some
/// two hundred combinations, a handful of which meant anything.
///
/// CallState is deliberately not among them: its one source is SipService, and a mirror of
/// the call state kept inside the UI has already cost a DTMF panel once. Whatever needs it
/// takes it as a parameter.
///
/// LastNonCall is where Route falls back to when the call it was showing ends.
/// </summary>
public sealed record UiState(
    Shell    Shell,
    NavRoute Route,
    NavRoute LastNonCall,
    Shell    Home,
    bool     StatusPopup)
{
    /// <summary>
    /// The state the process starts in. Home is the widget: the app has always opened
    /// collapsed, and signing in does not change that.
    /// </summary>
    public static UiState Initial(bool hasCredentials) => new(
        Shell:       hasCredentials ? Shell.Collapsed : Shell.Login,
        Route:       NavRoute.Dialer,
        LastNonCall: NavRoute.Dialer,
        Home:        Shell.Collapsed,
        StatusPopup: false);

    /// <summary>
    /// Brings the state back into line with its invariants. The reducer calls this on its
    /// result, not on every row of the table.
    ///
    /// The order is not optional: LastNonCall is repaired first, because Route falls back
    /// onto it.
    ///
    /// Dialer, not some other route: it is what Initial starts on, so a state whose
    /// fall-back has been lost lands where a fresh one would.
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
Expected: PASS, 7 тестов.

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
- Test: `OrbitalSIP.Tests/ShellRouterPipelineTests.cs`

Первая треть таблицы: старт, вход, настройки до входа, истечение сессии.

- [ ] **Step 1: Написать падающий тест**

Создать `OrbitalSIP.Tests/ShellRouterSessionTests.cs`:

```csharp
using OrbitalSIP.Models;
using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

/// <summary>
/// The transitions the session decides. A class of their own, apart from the rest of the
/// table, because this is the one part of it that can take everything else away from the
/// operator.
/// </summary>
public class ShellRouterSessionTests
{
    private static UiState Reduce(UiState state, UiEvent e, CallState call = CallState.Idle) =>
        ShellRouter.Reduce(state, e, call);

    private static UiState Panel(NavRoute route) =>
        UiState.Initial(true) with { Shell = Shell.Panel, Route = route, LastNonCall = route, Home = Shell.Panel };

    /// <summary>
    /// The panel sitting on the call screen. Kept apart from Panel() because LastNonCall
    /// has to point at a non-call route — otherwise Normalize repairs it and the "nothing
    /// changed" comparison fails for a reason that has nothing to do with the test.
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
    /// There is exactly one way out of settings-before-login, and every button on the bar
    /// is it. This used to be a flag, cleared only on the exits someone thought of, and an
    /// answered call's panel could inherit login mode from it.
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
    /// Login placed over a call in progress would take hangup, mute and hold away from an
    /// operator who is still talking. The dispatcher waits for the call to end and raises
    /// the same event again — so doing nothing here is the whole of it.
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

Создать `OrbitalSIP.Tests/ShellRouterPipelineTests.cs`:

```csharp
using OrbitalSIP.Models;
using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

/// <summary>
/// What Reduce does around the transition table, rather than in it: normalizing the result
/// and closing the status popup when the screen moves. Both are easy to lose — dropping the
/// Normalize call from Reduce left all 675 tests of the day green, and the popup rule was
/// being asked before normalization instead of after, so a route that moved only by
/// normalization did not count as a screen change.
/// </summary>
public class ShellRouterPipelineTests
{
    /// <summary>
    /// The call screen with the status popup open, and the far side hangs up. There is no
    /// transition row for this on purpose — Normalize is the whole mechanism — so this is
    /// also the test that proves Reduce still calls it.
    /// </summary>
    [Fact]
    public void ACallEndingUnderTheStatusPopupTakesBothTheRouteAndThePopupWithIt()
    {
        var onCall = UiState.Initial(true) with
        {
            Shell = Shell.Panel, Route = NavRoute.Call, LastNonCall = NavRoute.Tasks,
            Home = Shell.Panel, StatusPopup = true,
        };

        var after = ShellRouter.Reduce(onCall, new UiEvent.CallStateChanged(CallState.Idle), CallState.Idle);

        Assert.Equal(NavRoute.Tasks, after.Route);
        Assert.False(after.StatusPopup);
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
/// Everything that changes what the window looks like, and nothing else: a button press,
/// a service's answer, a decision the operator made.
///
/// Hotkeys are deliberately not among them. They are addressed to the call through
/// SipService, and the window follows CallStateChanged the way it follows any other change
/// of call state — the one exception is spelled out in MainWindow, where answering an
/// incoming call raises CallStarted once AnswerAsync has succeeded.
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

    /// <summary>An incoming call answered or an outgoing one started — to the window these are the same thing.</summary>
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
/// The one place the UI state changes.
///
/// A pure function: the same three inputs always give the same state back. The side
/// effects — CallAsync, Hangup, SetStateAsync, opening windows — stay in MainWindow, and
/// nothing here knows about them. That is what makes the transition table something a test
/// can reach without building a window, like NavBadgeState next door and TaskListOutcome
/// over in Services.
/// </summary>
public static class ShellRouter
{
    public static UiState Reduce(UiState state, UiEvent e, CallState call)
    {
        // Normalize before the comparison, not after: it is Normalize that walks the route
        // off Call when a call ends, and a route that moves only by normalization is still
        // a screen change. Asking the question first left the status popup open across it.
        var next = Route(state, e, call).Normalize(call);

        if (next.Shell != state.Shell || next.Route != state.Route)
            next = next with { StatusPopup = false };

        return next;
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

        // A live call defers the login: the dispatcher waits for Idle and raises this
        // event again.
        UiEvent.SessionExpired when call == CallState.Idle =>
            s with { Shell = Shell.Login },

        _ => s,
    };
}
```

- [ ] **Step 4: Запустить и убедиться, что проходит**

Run: `dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q --filter "FullyQualifiedName~ShellRouterSessionTests"`
Expected: PASS, 16 тестов — 4 `[Fact]` плюс три `[Theory]` по четыре случая каждая.

Отдельно проверить конвейер `Reduce`, который этот фильтр не захватывает:

```bash
dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q --filter "FullyQualifiedName~ShellRouterPipelineTests"
```

Expected: PASS, 1 тест.

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

/// <summary>Navigation inside a session: the four tabs, expanding the window and collapsing it.</summary>
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
    /// A tap on the tab that is already lit is inert. Otherwise it rebuilds the screen and
    /// takes everything on it that was never committed: the host, the credentials, the
    /// language and the scale in Settings, a half-typed number in the dialer.
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
    /// The route survives the trip into the widget: expanding again puts the operator back
    /// where they were rather than on the dialer. ReturnToPreferredMode builds the dialer
    /// every time, whatever they were looking at.
    /// </summary>
    [Fact]
    public void TheRouteSurvivesARoundTripThroughTheWidget()
    {
        var s = Reduce(Panel(NavRoute.Recents), new UiEvent.CollapseRequested());
        Assert.Equal(Shell.Collapsed, s.Shell);

        s = Reduce(s, new UiEvent.ExpandRequested());
        Assert.Equal(Shell.Panel, s.Shell);

        Assert.Equal(NavRoute.Recents, s.Route);
    }

    /// <summary>
    /// The status popup does not survive a change of screen. Today that is a side effect of
    /// SetMainContent; here it is a rule that holds on every transition.
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
    /// Home is always one of the two surfaces there is anything to return to. A call panel
    /// or a strip landing here through a typo in one row of the table would send the
    /// operator back into a call that is not there.
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

    /// <summary>
    /// The bar has four slots and the call screen is not one of them. Null is what task 7
    /// widens BottomNavControl.ActiveTab to accept, so that this screen can light nothing
    /// instead of lying about which tab the operator is on.
    /// </summary>
    [Theory]
    [InlineData(NavRoute.Dialer,   NavTab.Dialer)]
    [InlineData(NavRoute.Recents,  NavTab.Recents)]
    [InlineData(NavRoute.Tasks,    NavTab.Tasks)]
    [InlineData(NavRoute.Settings, NavTab.Settings)]
    public void EveryTabRouteLightsItsOwnSlot(NavRoute route, NavTab tab)
    {
        Assert.Equal(tab, ShellRouter.TabFor(route));
    }

    [Fact]
    public void TheCallRouteLightsNothing()
    {
        Assert.Null(ShellRouter.TabFor(NavRoute.Call));
    }
}
```

- [ ] **Step 2: Запустить и убедиться, что падает**

Run: `dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q --filter "FullyQualifiedName~ShellRouterNavigationTests"`
Expected: FAIL — `ATabPressOpensThePanelOnThatTab` падает на `Assert.Equal(Shell.Panel, s.Shell)`, потому что `Route` пока возвращает состояние без изменений.

- [ ] **Step 3: Написать минимальную реализацию**

В `OrbitalSIP/Models/ShellRouter.cs` добавить отображение таба в route и четыре строки в `Route`, **выше** ветки `_ => s`:

```csharp
    /// <summary>The bar slot that reads as current, or null — the call screen has none.</summary>
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
        _               => throw new ArgumentOutOfRangeException(nameof(tab), tab, "Tab with no route"),
    };
```

и в `Route`, где первая из трёх новых строк несёт комментарий о порядке. Требование
«эта ветка обязана стоять ниже той» до сих пор жило только в прозе плана — компилятор о
нём не предупредит, потому что гварды не исчерпывающи, а тест поймает не всякую
перестановку. Комментарий должен быть в самом файле, рядом с ветками, которых касается:

```csharp
        // Below the LoginSettings arm above, and that order is load-bearing: in login mode
        // a tab press goes back to login, and a general TabPressed arm placed higher would
        // swallow it and open a panel to an operator with no session.
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

Про первую из новых строк — ту, что гасит нажатие на уже горящий таб, — известно, что она
не меняет поведения ни на одном достижимом состоянии, и это выяснено дважды независимо.
`Route` и `LastNonCall` расходятся только когда `Route == Call`, а тогда условие этой
строки ложно; во всех остальных состояниях общая строка ниже запишет ровно те же три
поля, которые guard сохранил бы бездействием. Значит `PressingTheLitTabChangesNothing`
пинает гарантию, а не эту строку: удалить её — все тесты останутся зелёными.

Строка тем не менее остаётся. Она называет правило спека вслух, стоит одну строчку, и
станет живой в тот день, когда общая строка начнёт писать что-то ещё. А настоящая защита
от пересборки экрана на повторном нажатии живёт не здесь, а в `Dispatch` из Task 8:
`if (next == _state) return;` — равная запись означает, что рисовать нечего.

Заодно, пока `Route` ещё читается целиком, зафиксировать два правила комментариями — оба
станут неочевидны, когда веток станет восемнадцать.

Над самим `Route`:

```csharp
    // The shape of an arm says whether its payload is used further: a bare type pattern
    // when the event carries nothing (ten of the thirteen do), a property pattern when the
    // payload is only tested, a capture when it goes into the result. Three styles on
    // purpose — do not flatten them into one.
```

И над `CallStateChanged` в `OrbitalSIP/Models/UiEvent.cs`:

```csharp
    /// <summary>
    /// The call state the window is being told about. Must be the same value the dispatcher
    /// passes to Reduce as its third argument — both come from App.SipService.State, read
    /// once, and nothing in the type system holds them together.
    /// </summary>
```

- [ ] **Step 4: Запустить и убедиться, что проходит**

Run: `dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --nologo -v q --filter "FullyQualifiedName~ShellRouter"`
Expected: PASS — все три класса тестов роутера: Session, Pipeline и новый Navigation.

- [ ] **Step 5: Закоммитить**

```bash
git add OrbitalSIP/Models/ShellRouter.cs OrbitalSIP/Models/UiEvent.cs OrbitalSIP.Tests/ShellRouterNavigationTests.cs
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
/// The call as a route of its own, not as something the «Набор» tab is quietly swapped for.
///
/// ShowDialer() hands back the call screen while a call is up, so «Набор» silently means
/// "back to the call" — and there is nowhere left to get a dialpad for a second line or for
/// a transfer target.
/// </summary>
public class ShellRouterCallTests
{
    private static UiState Reduce(UiState state, UiEvent e, CallState call) =>
        ShellRouter.Reduce(state, e, call);

    private static UiState Panel(NavRoute route, Shell home = Shell.Panel) =>
        UiState.Initial(true) with { Shell = Shell.Panel, Route = route, LastNonCall = route, Home = home };

    /// <summary>
    /// The panel sitting on the call screen. Kept apart from Panel() because LastNonCall
    /// has to point at a non-call route — otherwise Normalize repairs it and the "nothing
    /// changed" comparison fails for a reason that has nothing to do with the test.
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
    /// A second call arriving mid-conversation does not take the operator off the one they
    /// are on. What SipService does with it is not this table's business.
    /// </summary>
    [Fact]
    public void ASecondIncomingCallDoesNotDisturbTheFirst()
    {
        var before = PanelOnCall();

        Assert.Equal(before, Reduce(before, new UiEvent.IncomingCall(), CallState.Active));
    }

    /// <summary>
    /// Declining puts the operator back where the ringing interrupted them. Both Home values
    /// on purpose: with a single one, an arm that had stopped reading Home and hardcoded that
    /// same surface would pass — which is exactly what a mutation of this arm did, against all
    /// 712 tests, before this test was widened.
    /// </summary>
    [Theory]
    [InlineData(Shell.Collapsed)]
    [InlineData(Shell.Panel)]
    public void DecliningGoesBackHome(Shell home)
    {
        var s = Reduce(UiState.Initial(true) with { Shell = Shell.Incoming, Home = home },
                       new UiEvent.IncomingDeclined(), CallState.Idle);

        Assert.Equal(home, s.Shell);
    }

    /// <summary>
    /// A call that rings out and is never answered goes back the same way a declined one does.
    /// Both Home values, for the reason spelled out on DecliningGoesBackHome above.
    /// </summary>
    [Theory]
    [InlineData(Shell.Collapsed)]
    [InlineData(Shell.Panel)]
    public void AMissedCallGoesBackHomeToo(Shell home)
    {
        var s = Reduce(UiState.Initial(true) with { Shell = Shell.Incoming, Home = home },
                       new UiEvent.CallStateChanged(CallState.Idle), CallState.Idle);

        Assert.Equal(home, s.Shell);
    }

    /// <summary>
    /// The strip is the way back, and it works for every shape of live call — an outgoing
    /// ringback and a call on hold are both something to return to, not just a connected one.
    /// Driving only Active here would let CallIsLive narrow to Active without a single test
    /// noticing, which is exactly what a mutation of it did once the two arms and the strip
    /// predicate started sharing that one definition.
    /// </summary>
    [Theory]
    [InlineData(CallState.Ringing)]
    [InlineData(CallState.Active)]
    [InlineData(CallState.OnHold)]
    public void TheReturnStripBringsTheCallBack(CallState call)
    {
        var s = Reduce(Panel(NavRoute.Tasks), new UiEvent.ReturnStripPressed(), call);

        Assert.Equal(NavRoute.Call, s.Route);
    }

    [Fact]
    public void TheReturnStripDoesNothingWithoutACall()
    {
        var before = Panel(NavRoute.Tasks);

        Assert.Equal(before, Reduce(before, new UiEvent.ReturnStripPressed(), CallState.Idle));
    }

    /// <summary>
    /// While a call is up, «Набор» stays a dialer — the whole reason this work exists.
    /// </summary>
    [Fact]
    public void TheDialerTabIsStillADialerDuringACall()
    {
        var s = Reduce(PanelOnCall(cameFrom: NavRoute.Tasks),
                       new UiEvent.TabPressed(NavTab.Dialer), CallState.Active);

        Assert.Equal(NavRoute.Dialer, s.Route);
    }

    /// <summary>
    /// A call ending on the call screen returns the operator to where they left for it, not
    /// to home. Today that is a list of exceptions: Login and Settings stay put, the rest do
    /// not, and every new screen has to be assigned to one half or the other.
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
    /// And if the operator walked off to another tab mid-conversation, the end of the call
    /// must not pull them off it. This is the row of the table that retires the list of
    /// exceptions.
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
    /// Collapsing during a call is the same gesture as collapsing without one, and it has to
    /// move Home the same way. Otherwise the window settles into the widget when the call ends
    /// while Home still says Panel, and the next call unfolds a panel at the operator who just
    /// collapsed it.
    ///
    /// Every live call state, for the reason given on TheReturnStripBringsTheCallBack: this
    /// arm shares its guard with the strip predicate now.
    /// </summary>
    [Theory]
    [InlineData(CallState.Ringing)]
    [InlineData(CallState.Active)]
    [InlineData(CallState.OnHold)]
    public void CollapsingDuringACallMovesHomeToo(CallState call)
    {
        var s = Reduce(Panel(NavRoute.Dialer), new UiEvent.CallStarted(), call);
        s = Reduce(s, new UiEvent.CollapseRequested(), call);

        Assert.Equal(Shell.CallBar, s.Shell);
        Assert.Equal(Shell.Collapsed, s.Home);

        s = Reduce(s, new UiEvent.CallStateChanged(CallState.Idle), CallState.Idle);
        Assert.Equal(Shell.Collapsed, s.Shell);
    }

    /// <summary>
    /// The other direction of the status-popup rule. ShellRouterPipelineTests pins a call
    /// ending that moves the route out from under an open popup and takes the popup with
    /// it; this pins that a call ending which moves nothing leaves the popup alone. Without
    /// it, clearing the popup unconditionally would pass every other test in the suite.
    /// </summary>
    [Fact]
    public void ACallEndingSomewhereElseLeavesTheStatusPopupOpen()
    {
        var before = Panel(NavRoute.Tasks) with { StatusPopup = true };

        var after = Reduce(before, new UiEvent.CallStateChanged(CallState.Idle), CallState.Idle);

        Assert.True(after.StatusPopup);
    }

    /// <summary>
    /// The states in the middle of a call do not move the screen: their business is the
    /// labels and the buttons on a screen that is already open.
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

        // Above the general CollapseRequested arm from Task 4. The compiler enforces that
        // much on its own — an unguarded arm ahead of a guarded one of the same type is
        // CS8510, not a warning — so this note is here for the reason, which CS8510 does
        // not give: below it, a live call would collapse to the widget and take hangup,
        // mute and hold away from an operator who is still talking.
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
Expected: PASS — все четыре класса тестов роутера: Session, Pipeline, Navigation и новый Call.

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
/// The "a call is running, go back to it" strip is the only thing tying the operator to the
/// conversation while they are looking at another tab. Dark when it should not be, it
/// leaves them no way back; lit when it should not be, it takes them to a call screen with
/// no call behind it.
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

    /// <summary>On the call screen itself there is nowhere left to return to.</summary>
    [Fact]
    public void TheCallRouteNeedsNoStrip()
    {
        var s = Panel(NavRoute.Dialer) with { Route = NavRoute.Call };

        Assert.False(ShellRouter.ShowReturnStrip(s, CallState.Active));
    }

    /// <summary>
    /// The strip has nothing to say about the call bar or the widget — those are not
    /// PanelShellView's to draw. An incoming call lives on a surface of its own, where there
    /// is no panel at all.
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

В `ShellRouter` — сначала имя для условия, которое здесь появилось бы третьим по счёту:

```csharp
    /// <summary>
    /// Whether there is a call to go back to.
    ///
    /// Not Idle, and nothing finer: an outgoing ringback is already something the operator
    /// can return to, and so is a call on hold. Named because three separate places ask it —
    /// the ReturnStripPressed arm, the call-gated CollapseRequested arm, and the predicate
    /// below — and three copies of the same comparison drift apart the day "live" needs a
    /// narrower definition.
    /// </summary>
    private static bool CallIsLive(CallState call) => call != CallState.Idle;

    /// <summary>
    /// Whether the way back to the call is on screen.
    ///
    /// IncomingRinging never reaches this predicate — an incoming call lives on
    /// Shell.Incoming, where there is no panel to carry the strip.
    /// </summary>
    public static bool ShowReturnStrip(UiState state, CallState call) =>
        state.Shell == Shell.Panel &&
        state.Route != NavRoute.Call &&
        CallIsLive(call);
```

Затем заменить обе уже написанные проверки на вызов: в ветке `UiEvent.ReturnStripPressed`
и в call-gated `UiEvent.CollapseRequested` вместо `call != CallState.Idle` вызвать
`CallIsLive(call)`. Тесты Task 5 не должны при этом дрогнуть — поведение то же, меняется
только то, что условие теперь названо.

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
        /// Which tab reads as current, or null when the operator is on none of the four:
        /// the call screen is reached only through the return strip and has no slot on
        /// this bar.
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
        /// Everything the window is. Changed only through Dispatch, and only to what
        /// ShellRouter returned — assign to it directly and back comes exactly the scatter
        /// of hand-kept flags this work exists to remove.
        /// </summary>
        private UiState _state = UiState.Initial(hasCredentials: false);
```

и методы (рядом с `NavigateTo`, около строки 1105):

```csharp
        /// <summary>
        /// The one way in to a change of state. Works out the next state, and draws the
        /// difference when there is one.
        /// </summary>
        private void Dispatch(UiEvent e)
        {
            // The event's own payload wins over the live property when it has one. Both come
            // from App.SipService.State, but at different moments: SIP events arrive on
            // background threads and reach here through InvokeAsync, so the property can have
            // moved on by the time this runs. A CallStateChanged(Idle) still queued when the
            // next call starts ringing would otherwise be reduced against IncomingRinging —
            // the arm matching the payload would fire while Normalize, reading the parameter,
            // decided the call was still alive and left the route on the call screen. One
            // reduction, two answers to "is there a call".
            var call = e is UiEvent.CallStateChanged changed ? changed.State : App.SipService.State;

            var next = ShellRouter.Reduce(_state, e, call);

            // Record equality, and it carries more weight than it looks: this is what makes
            // a press on the already-lit tab free. ShellRouter has an arm for that case, but
            // the arm is belt-and-braces — the general arm below it returns an equal record
            // anyway, and this line is what turns "equal" into "do not rebuild the screen".
            if (next == _state) return;

            var prev = _state;
            _state = next;
            Apply(prev, next);
        }

        /// <summary>
        /// Draws the difference between two states. Decides nothing — every decision was
        /// already made in ShellRouter.
        ///
        /// prev == null is the first draw, and then everything is drawn.
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
                // The login screens are neither animated nor anchored to the corner: they
                // centre, the way they do on a cold start with no credentials. An animation
                // in flight is killed right here — its next tick would overwrite the
                // geometry set below and leave login the size of the widget.
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

        /// <summary>The screen this state calls for. Rebuilt whenever the (Shell, Route) pair changes.</summary>
        private object BuildContent(UiState s) => s.Shell switch
        {
            Shell.Collapsed => new Views.WidgetView(),
            Shell.Panel     => BuildPanelContent(s.Route),
            _               => throw new NotSupportedException($"{s.Shell} has not moved over to Apply yet"),
        };

        private object BuildPanelContent(NavRoute route) => route switch
        {
            NavRoute.Dialer   => CreateDialerView(),
            _                 => throw new NotSupportedException($"{route} has not moved over to Apply yet"),
        };

        /// <summary>Centres the window on the work area of the screen it is currently on.</summary>
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

        /// <summary>Kills an animation in flight without letting it finish.</summary>
        private void CancelAnimation()
        {
            _animTimer?.Stop();
            _animTimer     = null;
            _animStopwatch = null;
            _pendingContent = null;
        }

        /// <summary>
        /// Hands the bottom bar its state. The replacement for AttachNav, which derived the
        /// tab and the login mode from the screen's type — both are already in UiState here.
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
            _ => throw new ArgumentOutOfRangeException(nameof(s), s.Shell, "Surface has no screen"),
        };

        private object BuildPanelContent(NavRoute route) => route switch
        {
            NavRoute.Dialer   => CreateDialerView(),
            NavRoute.Recents  => CreateRecentsView(),
            NavRoute.Tasks    => CreateTasksView(),
            NavRoute.Settings  => CreateSettingsView(fromLogin: false),
            NavRoute.Call     => CreateActiveCallView(),
            _ => throw new ArgumentOutOfRangeException(nameof(route), route, "Route has no screen"),
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
            // The deferred login: the session died mid-conversation and has been waiting
            // for the end of it. SessionExpired again rather than CallStateChanged — that
            // keeps the decision one row of the table instead of a second path to login.
            if (state == CallState.Idle && _sessionExpiredPending)
            {
                _sessionExpiredPending = false;
                Dispatch(new UiEvent.SessionExpired());
                CloseDialogWindows();
                return;
            }

            // The end of a call is the one moment the missed counter can have just moved,
            // and the same moment the operator is looking at the bar again.
            if (state == CallState.Idle) _ = App.NavBadges.RefreshNowAsync();

            Dispatch(new UiEvent.CallStateChanged(state));

            // The labels and the buttons on an already-open call screen are not a change
            // of screen, so they go around Dispatch.
            var host = this.FindControl<ContentControl>("Host");
            bool isOnHold = state == CallState.OnHold;
            if (host?.Content is Views.ActiveCallView av) { av.MarkConnected(); av.SetStatus(isOnHold); }
            else if (host?.Content is Views.ActiveCallWidgetView awv) awv.SetStatus(isOnHold);

            RefreshChrome(_state);
        }
```

`StartOutgoingCall` — оставить проверку `State != CallState.Idle` и `CallAsync`, а ветвление по `_preferredMode` заменить на `Dispatch(new UiEvent.CallStarted())` **до** `await CallAsync`, чтобы экран поднялся сразу.

`OnSessionExpired` — ветку `if (App.SipService.State != CallState.Idle) { _sessionExpiredPending = true; return; }` оставить; вместо `ShowLoginAfterSessionExpiry()` вызвать `Dispatch(new UiEvent.SessionExpired())` и `CloseDialogWindows()` (метод появится в Task 12; пока завести пустым).

`_sessionExpiredPending` — единственное поле, которое эта работа оставляет на `MainWindow` вручную, хотя вся её суть в том, чтобы такие поля убрать. Так и задумано, и вот почему: оно описывает не то, чем окно является, а то, что одно событие пришло слишком рано и его придётся послать ещё раз. `UiState` отвечает на вопрос «что нарисовать», и добавление в него флага «а ещё где-то ждёт своей очереди истёкшая сессия» вернуло бы в запись ровно тот вид скрытой связности, ради удаления которого она заведена. Держать его снаружи безопасно потому, что читается он в одном месте и сбрасывается там же, на первом же `Idle`. Добавить этот абзац комментарием к полю.

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
    /// The panel's chrome: the top bar, the return-to-call strip, the content and the
    /// bottom bar.
    ///
    /// Until now the top bar and the bottom bar were each duplicated in the markup of
    /// five screens, and the strip would have been a third thing copied five times —
    /// five places that would each have to be taught when to show it and when to hide it,
    /// and five places to get that wrong in.
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
            _ => throw new ArgumentOutOfRangeException(nameof(route), route, "Route has no screen"),
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
    /// "A call is running — go back to it." The only thing tying the operator to the
    /// conversation while they are looking at another tab.
    ///
    /// The timer counts from SipService.ActiveCallStartedAt rather than from a mark of its
    /// own: a private counter would drift from the one on the call screen by exactly the
    /// time the operator spent getting to this tab.
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

            // A timer that outlived its screen would hold a reference to it for the rest
            // of the process.
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
        /// <summary>Closes the open window, if there is one. Called when the session expires.</summary>
        public static void CloseIfOpen() => _current?.Close();
```

`SmsComposeDialog` открывается напрямую из `ActiveCallView` и `RecentsView`, минуя лаунчер, — его экземпляры хранятся в `_activeSmsDialog` и `_historySmsDialog` этих экранов. Их закрывает `Window.OwnedWindows`, см. следующий шаг.

- [ ] **Step 2: Закрывать всё при истечении сессии**

В `MainWindow` заполнить заглушку из Task 9:

```csharp
        /// <summary>
        /// Closes every dialog window. Only on a session expiry: what is in them belongs to
        /// a session that no longer exists, and there is nothing left to send from them.
        ///
        /// A change of screen and the end of a call deliberately do not come here — the
        /// after-call work outlives the conversation, and a half-written SMS draft is worth
        /// more than consistency.
        /// </summary>
        private void CloseDialogWindows()
        {
            Views.TaskWindowLauncher.CloseIfOpen();
            Views.SurveyWindowLauncher.CloseIfOpen();
            Views.ScriptsWindowLauncher.CloseIfOpen();

            // The SMS windows are opened by the screens directly, with no launcher, but
            // this window is their owner all the same.
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
