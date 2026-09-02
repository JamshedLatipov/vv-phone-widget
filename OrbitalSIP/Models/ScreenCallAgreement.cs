using OrbitalSIP.Services;

namespace OrbitalSIP.Models;

/// <summary>
/// Whether the screen on show and the call the service is on describe the same thing.
///
/// Purely an observation: nothing acts on the answer, it is written to the log. The value is
/// that the disagreement gets recorded at all. A report of a "frozen" widget could not be
/// checked against anything — sip.log proved the service had been correct at every step, the
/// window kept no record of what it had drawn, and the only evidence that the two had ever
/// parted company was a photograph of the screen.
///
/// Only screens that claim to BE the call are checked. A tab open during a live call claims
/// nothing — that is what the return strip is for.
/// </summary>
public static class ScreenCallAgreement
{
    /// <summary>What the screen and the call disagree about, or null when they agree.</summary>
    public static string? Disagreement(UiState state, CallState call)
    {
        // The full call panel and the mini bar carry the same controls — hold, mute, hangup —
        // and both mean "the operator is on this call".
        var showsCall = state.Shell == Shell.CallBar ||
                        (state.Shell == Shell.Panel && state.Route == NavRoute.Call);

        if (showsCall && call is not (CallState.Ringing or CallState.Active or CallState.OnHold))
            return $"the call screen is up while the call is {call}";

        if (state.Shell == Shell.Incoming && call != CallState.IncomingRinging)
            return $"the incoming screen is up while the call is {call}";

        return null;
    }
}
