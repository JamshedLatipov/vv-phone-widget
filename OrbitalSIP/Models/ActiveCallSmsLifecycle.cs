using OrbitalSIP.Services;

namespace OrbitalSIP.Models;

/// <summary>Classifies captured SIP state events that invalidate an active-call SMS launch.</summary>
public static class ActiveCallSmsLifecycle
{
    public static bool ShouldInvalidate(CallState capturedState) =>
        capturedState is CallState.Idle or CallState.Ringing or CallState.IncomingRinging;
}
