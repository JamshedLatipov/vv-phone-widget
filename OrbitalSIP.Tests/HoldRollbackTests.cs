using OrbitalSIP.Models;
using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

/// <summary>
/// Whether a failed hold may put the previous call state back.
///
/// ApplyHold claims the new state under the lock, then fires the re-INVITE unlocked —
/// it is network I/O, and the SIP callbacks it wakes up contend on that same lock. The
/// call can therefore end inside PutOnHold/TakeOffHold: OnCallEnded claims Idle, drops
/// the agent and tears the media down, and the re-INVITE then throws against a dialog
/// that no longer exists. Restoring the pre-hold state from that catch put Active back
/// over the Idle that had just been announced — with no agent behind it, so no BYE and
/// no RTP close could ever move it again, and every later INVITE was refused for the
/// rest of the session.
/// </summary>
public class HoldRollbackTests
{
    [Fact]
    public void RestoresThePreviousStateWhenTheCallIsStillUp()
    {
        Assert.True(HoldRollback.ShouldRestore(
            callStillUp: true, current: CallState.OnHold, claimed: CallState.OnHold));
    }

    /// <summary>
    /// The regression this exists for: the agent is gone, so the call is over and there
    /// is nothing left to hold.
    /// </summary>
    [Fact]
    public void LeavesTheCallEndedWhenTheAgentIsAlreadyGone()
    {
        Assert.False(HoldRollback.ShouldRestore(
            callStillUp: false, current: CallState.OnHold, claimed: CallState.OnHold));
    }

    /// <summary>
    /// Another thread has moved the state on since the claim — Idle from OnCallEnded is
    /// the case that matters. Whatever it decided outranks a rollback for a re-INVITE
    /// that failed because of that very decision.
    /// </summary>
    [Fact]
    public void LeavesTheStateAloneWhenSomethingElseHasMovedIt()
    {
        Assert.False(HoldRollback.ShouldRestore(
            callStillUp: true, current: CallState.Idle, claimed: CallState.OnHold));
    }

    /// <summary>Resuming from hold fails the same way and gets the same answer.</summary>
    [Fact]
    public void RestoresTheHeldStateWhenResumingFailsOnALiveCall()
    {
        Assert.True(HoldRollback.ShouldRestore(
            callStillUp: true, current: CallState.Active, claimed: CallState.Active));
    }
}
