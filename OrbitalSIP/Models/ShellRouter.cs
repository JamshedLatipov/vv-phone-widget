using System;
using OrbitalSIP.Services;

namespace OrbitalSIP.Models;

/// <summary>
/// The one place the UI state changes.
///
/// A pure function: the same three inputs always give the same state back. The side
/// effects — CallAsync, Hangup, SetStateAsync, opening windows — stay in MainWindow, and
/// nothing here knows about them. That is what makes the transition table something a test
/// can reach without building a window, like NavBadgeState next door and TaskListOutcome
/// over in Services.
/// </summary>
public static class ShellRouter
{
    public static UiState Reduce(UiState state, UiEvent e, CallState call)
    {
        // Normalize before the comparison, not after: it is Normalize that walks the route
        // off Call when a call ends, and a route that moves only by normalization is still
        // a screen change. Asking the question first left the status popup open across it.
        var next = Route(state, e, call).Normalize(call);

        if (next.Shell != state.Shell || next.Route != state.Route)
            next = next with { StatusPopup = false };

        return next;
    }

    // The shape of an arm says whether its payload is used further: a bare type pattern
    // when the event carries nothing (ten of the thirteen do), a property pattern when the
    // payload is only tested, a capture when it goes into the result. Three styles on
    // purpose — do not flatten them into one.
    private static UiState Route(UiState s, UiEvent e, CallState call) => e switch
    {
        UiEvent.LoginSucceeded => s with
        {
            Shell       = Shell.Collapsed,
            Home        = Shell.Collapsed,
            Route       = NavRoute.Dialer,
            LastNonCall = NavRoute.Dialer,
        },

        UiEvent.LoginSettingsRequested when s.Shell == Shell.Login =>
            s with { Shell = Shell.LoginSettings },

        UiEvent.SettingsSaved when s.Shell == Shell.LoginSettings =>
            s with { Shell = Shell.Login },

        UiEvent.TabPressed when s.Shell == Shell.LoginSettings =>
            s with { Shell = Shell.Login },

        // A live call defers the login: the dispatcher waits for Idle and raises this
        // event again.
        UiEvent.SessionExpired when call == CallState.Idle =>
            s with { Shell = Shell.Login },

        // Below the LoginSettings arm above, and that order is load-bearing: in login mode
        // a tab press goes back to login, and a general TabPressed arm placed higher would
        // swallow it and open a panel to an operator with no session.
        UiEvent.TabPressed t when s.Shell == Shell.Panel && RouteFor(t.Tab) == s.Route => s,

        UiEvent.TabPressed t => s with
        {
            Shell       = Shell.Panel,
            Route       = RouteFor(t.Tab),
            LastNonCall = RouteFor(t.Tab),
        },

        UiEvent.ExpandRequested when s.Shell == Shell.Collapsed =>
            s with { Shell = Shell.Panel, Home = Shell.Panel },

        UiEvent.IncomingCall when call is CallState.Idle or CallState.IncomingRinging =>
            s with { Shell = Shell.Incoming },

        UiEvent.IncomingDeclined =>
            s with { Shell = s.Home },

        UiEvent.CallStarted =>
            s.Home == Shell.Panel
                ? s with { Shell = Shell.Panel, Route = NavRoute.Call }
                : s with { Shell = Shell.CallBar },

        UiEvent.ReturnStripPressed when call != CallState.Idle =>
            s with { Shell = Shell.Panel, Route = NavRoute.Call },

        UiEvent.ExpandRequested when s.Shell == Shell.CallBar =>
            s with { Shell = Shell.Panel, Route = NavRoute.Call, Home = Shell.Panel },

        // Above the general CollapseRequested arm from Task 4. The compiler enforces that
        // much on its own — an unguarded arm ahead of a guarded one of the same type is
        // CS8510, not a warning — so this note is here for the reason, which CS8510 does
        // not give: below it, a live call would collapse to the widget and take hangup,
        // mute and hold away from an operator who is still talking.
        UiEvent.CollapseRequested when call != CallState.Idle =>
            s with { Shell = Shell.CallBar, Home = Shell.Collapsed },

        UiEvent.CollapseRequested =>
            s with { Shell = Shell.Collapsed, Home = Shell.Collapsed },

        UiEvent.CallStateChanged { State: CallState.Idle } when s.Shell == Shell.CallBar =>
            s with { Shell = Shell.Collapsed },

        UiEvent.CallStateChanged { State: CallState.Idle } when s.Shell == Shell.Incoming =>
            s with { Shell = s.Home },

        UiEvent.StatusPopupToggled p => s with { StatusPopup = p.Open },

        _ => s,
    };

    /// <summary>The bar slot that reads as current, or null — the call screen has none.</summary>
    public static NavTab? TabFor(NavRoute route) => route switch
    {
        NavRoute.Dialer   => NavTab.Dialer,
        NavRoute.Recents  => NavTab.Recents,
        NavRoute.Tasks    => NavTab.Tasks,
        NavRoute.Settings => NavTab.Settings,
        _                 => null,
    };

    private static NavRoute RouteFor(NavTab tab) => tab switch
    {
        NavTab.Dialer   => NavRoute.Dialer,
        NavTab.Recents  => NavRoute.Recents,
        NavTab.Tasks    => NavRoute.Tasks,
        NavTab.Settings => NavRoute.Settings,
        _               => throw new ArgumentOutOfRangeException(nameof(tab), tab, "Tab with no route"),
    };
}
