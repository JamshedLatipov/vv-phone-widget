using Material.Icons;

namespace OrbitalSIP.Services;

/// <summary>
/// Picks the glyph for the first tab slot, which carries three affordances in one: a
/// dial pad, "back to the call you are on", and "back to login".
///
/// Sibling of <see cref="NavPulse"/>, and pulled out for the same reason. The control
/// used to leave this icon wherever the last setter to run had put it, so leaving login
/// mode worked only because MainWindow happened to call SetLoginMode after SetInCall.
/// The property that matters is that the answer depends on the state and not on the
/// call order, and that property is only worth anything if something pins it.
/// </summary>
public static class NavTabIcon
{
    /// <summary>
    /// Login mode wins over the call state: without a session there is no call to go
    /// back to, so the slot is a way out of Settings and nothing else.
    /// </summary>
    public static MaterialIconKind ForDialerTab(bool loginMode, bool inCall) =>
        loginMode ? MaterialIconKind.ArrowLeft :
        inCall    ? MaterialIconKind.PhoneInTalk :
                    MaterialIconKind.Dialpad;
}
