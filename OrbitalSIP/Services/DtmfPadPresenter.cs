namespace OrbitalSIP.Services;

/// <summary>
/// Decides whether the in-call DTMF pad may send a tone right now.
///
/// SipService.SendDtmfAsync already refuses to send outside CallState.Active — hold's
/// re-INVITE takes the media path down, so a tone sent while parked would reach no
/// one — but it does so silently, with nothing telling the pad why. This is the one
/// place that question gets answered, so the pad's key state and SendDtmfAsync's own
/// guard can never disagree about what a held call allows.
/// </summary>
public static class DtmfPadPresenter
{
    public static bool CanSend(bool onHold) => !onHold;
}
