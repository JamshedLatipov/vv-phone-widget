using OrbitalSIP.Services;

namespace OrbitalSIP.Models;

/// <summary>
/// Everything that decides what the window looks like, in one record.
///
/// Replaces MainWindow's five independent variables (_preferredMode, _isExpanded,
/// _currentTab, _settingsFromLogin and a mirror of CallState), whose product came to some
/// two hundred combinations, a handful of which meant anything.
///
/// CallState is deliberately not among them: its one source is SipService, and a mirror of
/// the call state kept inside the UI has already cost a DTMF panel once. Whatever needs it
/// takes it as a parameter.
/// </summary>
public sealed record UiState(
    Shell    Shell,
    NavRoute Route,
    NavRoute LastNonCall,
    Shell    Home,
    bool     StatusPopup)
{
    /// <summary>
    /// The state the process starts in. Home is the widget: the app has always opened
    /// collapsed, and signing in does not change that.
    /// </summary>
    public static UiState Initial(bool hasCredentials) => new(
        Shell:       hasCredentials ? Shell.Collapsed : Shell.Login,
        Route:       NavRoute.Dialer,
        LastNonCall: NavRoute.Dialer,
        Home:        Shell.Collapsed,
        StatusPopup: false);

    /// <summary>
    /// Brings the state back into line with its invariants. The reducer calls this on its
    /// result, not on every row of the table.
    ///
    /// The order is not optional: LastNonCall is repaired first, because Route falls back
    /// onto it.
    /// </summary>
    public UiState Normalize(CallState call)
    {
        var state = this;

        if (state.LastNonCall == NavRoute.Call)
            state = state with { LastNonCall = NavRoute.Dialer };

        if (call == CallState.Idle && state.Route == NavRoute.Call)
            state = state with { Route = state.LastNonCall };

        return state;
    }
}
