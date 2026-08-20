namespace OrbitalSIP.Models;

/// <summary>
/// The four slots of the bottom navigation bar.
///
/// The bar used to identify its tabs by string ("Dialer", "Recents", …), compared in
/// four places at once inside the control. A typo in any of them silently highlighted
/// nothing, which is exactly what an enum exists to prevent.
/// </summary>
public enum NavTab
{
    Dialer,
    Recents,
    Tasks,
    Settings,
}
