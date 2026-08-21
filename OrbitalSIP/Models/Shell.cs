namespace OrbitalSIP.Models;

/// <summary>
/// The window's surfaces — what it is taken as a whole: its size, how it is placed, and
/// whether it carries chrome.
///
/// Replaces the pair of flags the state used to be inferred from: _isExpanded, set true
/// in nine places including the 436×132 strip that is not a panel at all, and
/// _settingsFromLogin, which had already leaked into an answered call's panel once. A
/// surface cannot leak — the only ways out of one are the transitions written down.
/// </summary>
public enum Shell
{
    /// <summary>The login screen. No session, no bottom bar.</summary>
    Login,

    /// <summary>Settings opened before signing in. The one way out is back to <see cref="Login"/>.</summary>
    LoginSettings,

    /// <summary>The floating 96×96 widget.</summary>
    Collapsed,

    /// <summary>The 320×600 panel, with a top bar and a bottom bar. What is inside it is <see cref="NavRoute"/>'s decision.</summary>
    Panel,

    /// <summary>The incoming-call strip.</summary>
    Incoming,

    /// <summary>The strip for a call in progress — the "collapsed" equivalent of the panel while a call runs.</summary>
    CallBar,
}
