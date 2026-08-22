namespace OrbitalSIP.Services;

/// <summary>
/// Decides whether the in-call DTMF pad may send a tone right now, and which hint to
/// show when it may not.
///
/// SipService.SendDtmfAsync sends only when its own state snapshot equals
/// CallState.Active; every other value — Idle, Ringing, IncomingRinging, OnHold — makes
/// it return without sending, silently. CanSend asks the same question of the same
/// enum for exactly that reason: this first shipped gating on "is the call on hold"
/// alone, a narrower question that agrees with SendDtmfAsync about OnHold and is wrong
/// about the rest. StartOutgoingCall is the case that bit: it puts a full
/// ActiveCallView on screen, keys live, before CallAsync has moved the call past Idle,
/// and nothing repaints the pad again until the call reaches Active — so every tone
/// "sent" while the phone was still Ringing was echoed to the operator and dropped on
/// the floor.
/// </summary>
public static class DtmfPadPresenter
{
    public static bool CanSend(CallState state) => state == CallState.Active;

    /// <summary>
    /// The i18n key for the pad's hint label, or null once <see cref="CanSend"/> is true
    /// and there is nothing to explain. OnHold gets its own wording — take the call off
    /// hold — because that advice is actively wrong for Idle/Ringing/IncomingRinging:
    /// the call has not been answered yet, so there is no hold to take it off.
    /// </summary>
    public static string? HintKey(CallState state) => state switch
    {
        CallState.Active => null,
        CallState.OnHold => "DtmfHoldHint",
        _                 => "DtmfNotAnsweredHint", // Idle, Ringing, IncomingRinging
    };
}
