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
