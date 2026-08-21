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
