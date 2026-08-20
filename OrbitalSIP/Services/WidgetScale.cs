using System;

namespace OrbitalSIP.Services
{
    /// <summary>
    /// Picks the factor the whole widget layout is scaled by.
    ///
    /// Every screen in this app is built from fixed logical sizes — a 96 px widget, a
    /// 320x600 panel, a 436x132 incoming strip — and those numbers were chosen against a
    /// 1080p screen. Windows only stretches them when the display has a DPI scaling factor
    /// set, and most of the machines this runs on do not: a 1366x768 laptop shows the panel
    /// at 78% of the screen height, while a 4K monitor at 100% scaling shows the same panel
    /// as a stamp in the corner. Neither is a DPI problem, so neither is something Avalonia
    /// corrects on its own.
    ///
    /// The factor here is applied as a layout transform over the window content, so the
    /// views keep their own numbers and everything inside them — text, icons, hit targets —
    /// grows and shrinks together.
    /// </summary>
    public static class WidgetScale
    {
        /// <summary>Stored setting meaning "work it out from the screen".</summary>
        public const int Auto = 0;

        public const int MinPercent = 50;
        public const int MaxPercent = 200;

        /// <summary>Percentages offered in Settings, in the order the combo lists them.</summary>
        public static readonly int[] Choices = { Auto, 75, 100, 125, 150 };

        /// <summary>Widest layout the window ever takes (the incoming-call strip).</summary>
        private const double WidestLayout = 436;
        /// <summary>Tallest layout the window ever takes (the expanded panel).</summary>
        private const double TallestLayout = 600;

        /// <summary>
        /// Share of the work area the largest layout may occupy. Below 1.0 because a panel
        /// filling the screen edge to edge stops reading as a widget, and because the
        /// operator still has to reach whatever is behind it.
        /// </summary>
        private const double MaxWorkAreaFraction = 0.85;

        /// <summary>
        /// Factor to use when the operator has not picked one. Stepped rather than
        /// continuous: a smooth ratio would land on fractional pixel sizes that make the
        /// ring and the icons blurry, and the four steps already cover the range of screens
        /// in the field.
        /// </summary>
        /// <param name="logicalHeight">Work-area height in logical units — that is, physical pixels divided by the screen's DPI scaling. A screen that is already scaled by Windows must not be scaled twice.</param>
        public static double AutoFactor(double logicalHeight) =>
            logicalHeight switch
            {
                < 800  => 0.75,
                < 1250 => 1.00,
                < 1700 => 1.25,
                _      => 1.50
            };

        /// <summary>
        /// Resolves the stored setting against the screen it will be shown on.
        ///
        /// A hand-picked percentage is honoured but still capped to what fits: the combo
        /// offers 150% on every machine, and on a 768-tall laptop that would push the panel
        /// past the bottom of the screen, where its title bar — the only drag handle a
        /// chrome-less window has — cannot be reached.
        /// </summary>
        public static double Resolve(int percent, double logicalWidth, double logicalHeight)
        {
            var requested = percent == Auto
                ? AutoFactor(logicalHeight)
                : Math.Clamp(percent, MinPercent, MaxPercent) / 100.0;

            return FitToScreen(requested, logicalWidth, logicalHeight);
        }

        private static double FitToScreen(double factor, double logicalWidth, double logicalHeight)
        {
            // Nothing measured yet (no screen resolved during startup) — take the setting
            // at face value rather than clamping against a zero-sized screen.
            if (logicalWidth <= 0 || logicalHeight <= 0) return factor;

            var ceiling = Math.Min(
                logicalWidth  * MaxWorkAreaFraction / WidestLayout,
                logicalHeight * MaxWorkAreaFraction / TallestLayout);

            // The floor wins over the ceiling on a screen too small for even the smallest
            // layout: an unreadable widget beats one scaled down to nothing.
            return Math.Max(MinPercent / 100.0, Math.Min(factor, ceiling));
        }

        /// <summary>Row to select in the Settings combo for a stored percentage.</summary>
        public static int ListPosition(int percent)
        {
            var index = Array.IndexOf(Choices, percent);
            return index < 0 ? 0 : index;   // an unknown value reads as Auto
        }

        /// <summary>Percentage to store for the selected combo row.</summary>
        public static int FromListPosition(int listPosition) =>
            listPosition >= 0 && listPosition < Choices.Length
                ? Choices[listPosition]
                : Auto;
    }
}
