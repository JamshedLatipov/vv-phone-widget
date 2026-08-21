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
    /// move home the same way. Otherwise the window settles into the widget when the call
    /// ends while Home stays the panel, and the next call opens a panel on the operator who
    /// collapsed it.
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
