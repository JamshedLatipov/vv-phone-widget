using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace OrbitalSIP.Views
{
    /// <summary>
    /// Lets an undecorated (<c>SystemDecorations="None"</c>) window be dragged by its
    /// header. Presses that land on an interactive control (e.g. the close button)
    /// are ignored so those still receive their clicks.
    /// </summary>
    internal static class WindowDrag
    {
        public static void EnableDrag(this Window window, Control? header)
        {
            if (header == null) return;

            header.PointerPressed += (_, e) =>
            {
                // Walk from the click source up to the header; bail out if we pass
                // through a control that needs the press for itself.
                var v = e.Source as Visual;
                while (v != null && v != header)
                {
                    if (v is Button || v is TextBox) return;
                    v = v.GetVisualParent();
                }

                if (e.GetCurrentPoint(window).Properties.IsLeftButtonPressed)
                    window.BeginMoveDrag(e);
            };
        }
    }
}
