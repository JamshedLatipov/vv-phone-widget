using System;

namespace OrbitalSIP.Models;

/// <summary>How the window puts itself on screen when it moves to a surface.</summary>
public enum ShellPlacement
{
    /// <summary>Holds the bottom-right corner — where the operator parked it.</summary>
    AnchorBottomRight,

    /// <summary>Centres on the work area. Login screens only.</summary>
    CenterOnScreen,
}

/// <summary>A surface's size in base units, before the widget scale multiplies it.</summary>
public readonly record struct ShellBox(double Width, double Height, ShellPlacement Placement);

/// <summary>
/// The window's size and placement as a function of its surface.
///
/// The constants came from MainWindow, where they were handed out by hand across nineteen
/// StartAnimation calls. The scale (<c>_uiScale</c>) is deliberately not applied here: it
/// is a property of the screen and of the setting, not of the surface, and it stays with
/// the window.
/// </summary>
public static class ShellGeometry
{
    public const double WidgetSize  = 96;
    public const double PanelWidth  = 320;
    public const double PanelHeight = 600;
    public const double StripWidth  = 436;
    public const double StripHeight = 132;

    public static ShellBox For(Shell shell) => shell switch
    {
        Shell.Login or Shell.LoginSettings =>
            new ShellBox(PanelWidth, PanelHeight, ShellPlacement.CenterOnScreen),

        Shell.Panel =>
            new ShellBox(PanelWidth, PanelHeight, ShellPlacement.AnchorBottomRight),

        Shell.Collapsed =>
            new ShellBox(WidgetSize, WidgetSize, ShellPlacement.AnchorBottomRight),

        Shell.Incoming or Shell.CallBar =>
            new ShellBox(StripWidth, StripHeight, ShellPlacement.AnchorBottomRight),

        _ => throw new ArgumentOutOfRangeException(nameof(shell), shell, "Surface has no geometry"),
    };
}
