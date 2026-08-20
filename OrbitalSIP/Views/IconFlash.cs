using System;
using System.Threading.Tasks;
using Material.Icons;
using Material.Icons.Avalonia;

namespace OrbitalSIP.Views;

/// <summary>
/// Briefly swaps a button's icon to a checkmark to confirm the action landed.
///
/// Four copies of this existed, and all four had the same defect: they captured the
/// current icon as the value to restore. A second press inside the flash window captured
/// the checkmark itself, so the button stayed ticked for the rest of the call and the
/// operator lost the only signal that copying had worked. Double-clicking a small icon
/// button is not an unusual thing to do.
///
/// A flash already in progress is left alone rather than restarted — the action still
/// happened, and the confirmation on screen is already saying so.
/// </summary>
internal static class IconFlash
{
    private static readonly TimeSpan DefaultHold = TimeSpan.FromMilliseconds(1200);

    public static Task ConfirmAsync(MaterialIcon? icon) => ConfirmAsync(icon, DefaultHold);

    public static async Task ConfirmAsync(MaterialIcon? icon, TimeSpan hold)
    {
        if (icon == null || icon.Kind == MaterialIconKind.Check) return;

        var original = icon.Kind;
        icon.Kind = MaterialIconKind.Check;
        try
        {
            await Task.Delay(hold);
        }
        finally
        {
            // Restored even if the delay is interrupted, so the button cannot be left
            // ticked by a cancellation either.
            icon.Kind = original;
        }
    }

    /// <summary>Flashes the first MaterialIcon inside a button's content, whatever it is wrapped in.</summary>
    public static Task ConfirmAsync(object? buttonContent)
    {
        var icon = buttonContent as MaterialIcon;
        if (icon == null && buttonContent is Avalonia.Controls.Panel panel)
        {
            foreach (var child in panel.Children)
                if (child is MaterialIcon nested) { icon = nested; break; }
        }

        return ConfirmAsync(icon);
    }
}
