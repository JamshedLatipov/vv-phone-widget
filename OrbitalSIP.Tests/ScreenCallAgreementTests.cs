using OrbitalSIP.Models;
using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

/// <summary>
/// Whether the screen on show and the call the service is on describe the same thing.
///
/// This exists because a report of a "frozen" widget could not be checked. sip.log proved the
/// service had been right at every step — the call rang for 22 seconds, 180 and PRACK went out
/// on time — while the operator was looking at a full active-call screen for it: a timer
/// counting up from zero, "in call" on the status line, Answer nowhere to be reached, and a
/// hangup button that rejected the caller with 486. Nothing recorded the disagreement, so the
/// only evidence it had ever happened was a photograph of the screen.
/// </summary>
public class ScreenCallAgreementTests
{
    private static UiState Panel(NavRoute route) =>
        UiState.Initial(true) with { Shell = Shell.Panel, Route = route, LastNonCall = NavRoute.Dialer, Home = Shell.Panel };

    [Theory]
    [InlineData(CallState.Ringing)]
    [InlineData(CallState.Active)]
    [InlineData(CallState.OnHold)]
    public void TheCallScreenAgreesWithACallTheOperatorIsOn(CallState call)
    {
        Assert.Null(ScreenCallAgreement.Disagreement(Panel(NavRoute.Call), call));
    }

    /// <summary>The incident: the call screen over a call nobody has answered.</summary>
    [Fact]
    public void TheCallScreenOverARingingIncomingCallIsADisagreement()
    {
        Assert.NotNull(ScreenCallAgreement.Disagreement(Panel(NavRoute.Call), CallState.IncomingRinging));
    }

    [Fact]
    public void TheCallScreenOverNoCallAtAllIsADisagreement()
    {
        Assert.NotNull(ScreenCallAgreement.Disagreement(Panel(NavRoute.Call), CallState.Idle));
    }

    /// <summary>The mini bar carries the same in-call controls, so it makes the same claim.</summary>
    [Fact]
    public void TheCallBarOverNoCallIsADisagreement()
    {
        var s = Panel(NavRoute.Dialer) with { Shell = Shell.CallBar };

        Assert.NotNull(ScreenCallAgreement.Disagreement(s, CallState.Idle));
        Assert.Null(ScreenCallAgreement.Disagreement(s, CallState.OnHold));
    }

    [Fact]
    public void TheIncomingScreenAgreesOnlyWithARingingIncomingCall()
    {
        var s = Panel(NavRoute.Dialer) with { Shell = Shell.Incoming };

        Assert.Null(ScreenCallAgreement.Disagreement(s, CallState.IncomingRinging));
        Assert.NotNull(ScreenCallAgreement.Disagreement(s, CallState.Active));
        Assert.NotNull(ScreenCallAgreement.Disagreement(s, CallState.Idle));
    }

    /// <summary>
    /// A tab open during a live call is not a disagreement — that is the return strip's whole
    /// reason to exist. Only screens that claim to BE the call are checked.
    /// </summary>
    [Theory]
    [InlineData(CallState.Idle)]
    [InlineData(CallState.Active)]
    [InlineData(CallState.IncomingRinging)]
    public void AnOrdinaryTabClaimsNothingAboutTheCall(CallState call)
    {
        Assert.Null(ScreenCallAgreement.Disagreement(Panel(NavRoute.Tasks), call));
    }
}
