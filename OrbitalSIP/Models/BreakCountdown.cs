using System;

namespace OrbitalSIP.Models
{
    /// <summary>
    /// Renders the time left on the operator's break as MM:SS.
    ///
    /// Built from <see cref="TimeSpan.Minutes"/> before, which is the minute component of
    /// an hours/minutes/seconds split rather than the number of minutes: the longest break
    /// the popup offers is 60 minutes, and that rendered as 00:00 for the first second and
    /// then jumped to 59:59. The remaining time comes from a server-supplied end time, so
    /// nothing here may assume it stays under an hour.
    /// </summary>
    public static class BreakCountdown
    {
        public static string Format(TimeSpan remaining)
        {
            // A break whose end time has passed reads as finished, not as negative digits.
            if (remaining <= TimeSpan.Zero) return "00:00";

            var minutes = (int)remaining.TotalMinutes;
            return $"{minutes:D2}:{remaining.Seconds:D2}";
        }
    }
}
