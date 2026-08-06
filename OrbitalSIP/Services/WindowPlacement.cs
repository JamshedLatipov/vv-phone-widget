using System;
using Avalonia;
using Avalonia.Controls;

namespace OrbitalSIP.Services
{
    public static class WindowPlacement
    {
        /// <summary>
        /// Pulls a chrome-less dialog back inside the screen it opened on. Call it from
        /// OnOpened — CenterOwner has placed the window by then, and that placement is
        /// what needs correcting.
        /// </summary>
        public static void KeepOnScreen(this Window window)
        {
            var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
            if (screen == null) return;

            var size = PixelSize.FromSize(window.FrameSize ?? window.ClientSize, screen.Scaling);
            var placed = ClampToWorkingArea(
                new PixelRect(window.Position, size), screen.WorkingArea);

            if (placed != window.Position) window.Position = placed;
        }

        /// <summary>
        /// Keeps a chrome-less window fully inside the screen.
        ///
        /// The dialogs use SystemDecorations="None", so their own header bar is the
        /// only drag handle they have and the OS offers no way to move them. With
        /// WindowStartupLocation="CenterOwner" over the softphone widget — which
        /// operators park against a screen edge — that header could land off-screen,
        /// leaving the window unreachable.
        ///
        /// A window larger than the working area pins to the origin, so the header
        /// stays reachable and the overflow spills off the far edge.
        /// </summary>
        public static PixelPoint ClampToWorkingArea(PixelRect window, PixelRect workingArea) =>
            new(
                Clamp(window.X, window.Width, workingArea.X, workingArea.Width),
                Clamp(window.Y, window.Height, workingArea.Y, workingArea.Height));

        private static int Clamp(int position, int size, int areaStart, int areaSize)
        {
            if (size >= areaSize) return areaStart;
            return Math.Clamp(position, areaStart, areaStart + areaSize - size);
        }
    }
}
