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
