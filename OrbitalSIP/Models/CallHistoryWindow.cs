using System;
using System.Globalization;

namespace OrbitalSIP.Models
{
    /// <summary>
    /// The "today" the call history asks the CDR endpoint for.
    ///
    /// It has to be the operator's local day expressed as UTC instants, not the UTC day.
    /// Taking <c>DateTime.UtcNow.Date</c> put the boundary at 05:00 local in this
    /// deployment (UTC+5), so the window both dropped the first five hours of the
    /// operator's own day and ran five hours into the next one. An operator on a night
    /// shift opened «Недавние» and could not see — or call back — anyone they had spoken
    /// to since midnight.
    ///
    /// Takes the current instant rather than reading the clock, so the boundaries are
    /// testable at offsets this machine is not set to.
    /// </summary>
    public static class CallHistoryWindow
    {
        private const string Iso8601Utc = "yyyy-MM-ddTHH:mm:ss.fffZ";

        /// <summary>
        /// Inclusive UTC bounds of the local day containing <paramref name="localNow"/>,
        /// formatted for the <c>fromDate</c>/<c>toDate</c> query parameters.
        /// </summary>
        public static (string From, string To) ForLocalDay(DateTimeOffset localNow)
        {
            // Y/M/D on a DateTimeOffset are already the components as seen at its own
            // offset, so this is local midnight for whoever is holding the phone.
            var localMidnight = new DateTimeOffset(
                localNow.Year, localNow.Month, localNow.Day, 0, 0, 0, localNow.Offset);

            // End of day as the last representable instant before the next local midnight,
            // so the two windows of consecutive days abut without overlapping.
            var localDayEnd = localMidnight.AddDays(1).AddTicks(-1);

            return (
                localMidnight.UtcDateTime.ToString(Iso8601Utc, CultureInfo.InvariantCulture),
                localDayEnd.UtcDateTime.ToString(Iso8601Utc, CultureInfo.InvariantCulture));
        }
    }
}
