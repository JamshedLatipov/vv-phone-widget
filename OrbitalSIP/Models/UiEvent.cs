using OrbitalSIP.Services;

namespace OrbitalSIP.Models;

/// <summary>
/// Everything that changes what the window looks like, and nothing else: a button press,
/// a service's answer, a decision the operator made.
///
/// Hotkeys are deliberately not among them. They are addressed to the call through
/// SipService, and the window follows CallStateChanged the way it follows any other change
/// of call state — the one exception is spelled out in MainWindow, where answering an
/// incoming call raises CallStarted once AnswerAsync has succeeded.
/// </summary>
public abstract record UiEvent
{
    public sealed record LoginSucceeded            : UiEvent;
    public sealed record SessionExpired            : UiEvent;
    public sealed record LoginSettingsRequested    : UiEvent;
    public sealed record SettingsSaved             : UiEvent;
    public sealed record TabPressed(NavTab Tab)    : UiEvent;
    public sealed record ReturnStripPressed        : UiEvent;
    public sealed record ExpandRequested           : UiEvent;
    public sealed record CollapseRequested         : UiEvent;
    public sealed record IncomingCall              : UiEvent;
    public sealed record IncomingDeclined          : UiEvent;

    /// <summary>An incoming call answered or an outgoing one started — to the window these are the same thing.</summary>
    public sealed record CallStarted               : UiEvent;

    public sealed record CallStateChanged(CallState State) : UiEvent;

    public sealed record StatusPopupToggled(bool Open)     : UiEvent;
}
