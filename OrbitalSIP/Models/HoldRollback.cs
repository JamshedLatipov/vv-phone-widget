using OrbitalSIP.Services;

namespace OrbitalSIP.Models;

/// <summary>
/// Decides whether a failed hold may put the previous call state back.
///
/// The re-INVITE that carries a hold goes out unlocked — it is network I/O, and the SIP
/// callbacks it wakes up contend on the same lock that guards the state. So the call can
/// end while PutOnHold/TakeOffHold is in flight, and the exception that comes back is
/// then a consequence of the hangup rather than a reason to undo it. Restoring the
/// pre-hold state from there wrote Active over the Idle OnCallEnded had just announced,
/// leaving a call on the operator's screen with no agent behind it: no BYE and no RTP
/// close could reach it again, and every later INVITE was refused for the rest of the
/// session because the service still read as busy.
/// </summary>
public static class HoldRollback
{
    /// <param name="callStillUp">Whether an agent is still published for the call.</param>
    /// <param name="current">The state right now, read under the same lock as the write.</param>
    /// <param name="claimed">The state this hold claimed before it fired the re-INVITE.</param>
    public static bool ShouldRestore(bool callStillUp, CallState current, CallState claimed) =>
        // Two questions, and the second alone would answer today: every path that drops the
        // agent moves the state in the same locked block, so a missing agent always shows up
        // as a state that is no longer the claimed one. Both are asked because the cost of
        // being wrong here is a session-long stuck call, and because the pair says out loud
        // what a reader would otherwise have to derive from OnCallEnded two screens away.
        callStillUp && current == claimed;
}
