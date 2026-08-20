using OrbitalSIP.Models;

namespace OrbitalSIP.Services;

/// <summary>
/// Decides whether the Dialer tab animates while a call is up.
///
/// Sibling of <see cref="WidgetPulse"/> and written for the same reason: an animation
/// that never stops keeps a transparent, topmost window repainting for no one. The tab
/// only breathes when the operator has navigated away from the call, which is the one
/// moment "tap here to get back" is worth saying out loud.
/// </summary>
public static class NavPulse
{
    public static bool ShouldPulse(bool inCall, NavTab currentTab) =>
        inCall && currentTab != NavTab.Dialer;
}
