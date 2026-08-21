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

        _ => s,
    };
}
