namespace OrbitalSIP.Models;

/// <summary>
/// What is on screen inside <see cref="Shell.Panel"/>.
///
/// A type of its own rather than a widened <see cref="NavTab"/>: the bar has four slots
/// and there will not be a fifth. <see cref="Call"/> is reached only through the return
/// strip, by expanding <see cref="Shell.CallBar"/>, or by a call starting — the bar has
/// no button for it, and while it is up there is nothing to light.
/// </summary>
public enum NavRoute
{
    Dialer,
    Recents,
    Tasks,
    Settings,

    /// <summary>The call screen. Legal only while a call is live — see <c>ShellRouter</c>.</summary>
    Call,
}
