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
