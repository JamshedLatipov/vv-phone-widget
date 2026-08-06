using OrbitalSIP.Models;

namespace OrbitalSIP.Services;

/// <summary>
/// Decides whether the widget's status ring animates.
///
/// The ring used to breathe permanently, so the app never stopped repainting a
/// transparent top-most window — all shift, for a state that needs no attention.
/// Restricting the pulse to states that are actually off gives the animation
/// something to mean and leaves the common case still.
/// </summary>
public static class WidgetPulse
{
    /// <returns>
    /// true when the widget should draw the operator's eye: registration is not healthy, or it is
    /// but the operator is paused out of the queue — by themselves or by a supervisor, which
    /// <see cref="StatusState.Paused"/> already covers.
    /// </returns>
    public static bool ShouldPulse(RegistrationState registration, StatusState? queueState) =>
        registration != RegistrationState.Registered || (queueState?.Paused ?? false);
}
