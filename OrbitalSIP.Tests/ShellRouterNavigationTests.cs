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
