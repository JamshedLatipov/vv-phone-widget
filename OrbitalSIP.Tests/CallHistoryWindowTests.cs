using System;
using System.Globalization;
using OrbitalSIP.Models;
using Xunit;

namespace OrbitalSIP.Tests;

/// <summary>
/// "Today" in the call history means the operator's local day, not the UTC one.
///
/// The deployment runs at UTC+5, so a UTC-based day boundary lands at 05:00 local:
/// everything the operator did between local midnight and 05:00 fell outside the window
/// and simply was not in the list. Night shifts lost the first five hours of their own
/// work, with no way to call anyone back.
/// </summary>
public class CallHistoryWindowTests
{
    private static readonly TimeSpan Tajikistan = TimeSpan.FromHours(5);

    private static DateTimeOffset Local(int year, int month, int day, int hour, int minute, TimeSpan offset) =>
        new DateTimeOffset(year, month, day, hour, minute, 0, offset);

    private static DateTimeOffset Parse(string iso) =>
        DateTimeOffset.ParseExact(iso, "yyyy-MM-ddTHH:mm:ss.fffZ",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private static bool Covers((string From, string To) window, DateTimeOffset instant) =>
        Parse(window.From) <= instant && instant <= Parse(window.To);

    /// <summary>
    /// The regression. At 10:00 local the UTC date is already today, so the old window
    /// started at 00:00Z — which is 05:00 local. A call taken at 02:00 local, eight hours
    /// into the same shift, was not in it.
    /// </summary>
    [Fact]
    public void CoversACallMadeJustAfterLocalMidnight()
    {
        var window = CallHistoryWindow.ForLocalDay(Local(2026, 8, 20, 10, 0, Tajikistan));
        var callAtTwoInTheMorning = Local(2026, 8, 20, 2, 0, Tajikistan);

        Assert.True(Covers(window, callAtTwoInTheMorning),
            $"02:00 local is inside the operator's day, but the window is {window.From}..{window.To}");
    }

    [Fact]
    public void CoversACallMadeLateInTheLocalEvening()
    {
        var window = CallHistoryWindow.ForLocalDay(Local(2026, 8, 20, 10, 0, Tajikistan));
        var callAtElevenAtNight = Local(2026, 8, 20, 23, 30, Tajikistan);

        Assert.True(Covers(window, callAtElevenAtNight),
            $"23:30 local is inside the operator's day, but the window is {window.From}..{window.To}");
    }

    [Fact]
    public void CoversTheMomentItIsAskedAbout()
    {
        var now = Local(2026, 8, 20, 2, 0, Tajikistan);
        Assert.True(Covers(CallHistoryWindow.ForLocalDay(now), now));
    }

    [Fact]
    public void ExcludesYesterdayEvening()
    {
        var window = CallHistoryWindow.ForLocalDay(Local(2026, 8, 20, 10, 0, Tajikistan));
        var yesterdayEvening = Local(2026, 8, 19, 23, 0, Tajikistan);

        Assert.False(Covers(window, yesterdayEvening));
    }

    [Fact]
    public void ExcludesTomorrowMorning()
    {
        var window = CallHistoryWindow.ForLocalDay(Local(2026, 8, 20, 10, 0, Tajikistan));
        var tomorrowMorning = Local(2026, 8, 21, 0, 30, Tajikistan);

        Assert.False(Covers(window, tomorrowMorning));
    }

    /// <summary>The window is a whole local day, whatever the offset.</summary>
    [Theory]
    [InlineData(5)]
    [InlineData(0)]
    [InlineData(-8)]
    [InlineData(13)]
    public void SpansExactlyOneDay(int offsetHours)
    {
        var window = CallHistoryWindow.ForLocalDay(Local(2026, 8, 20, 10, 0, TimeSpan.FromHours(offsetHours)));

        var span = Parse(window.To) - Parse(window.From);
        Assert.Equal(TimeSpan.FromDays(1) - TimeSpan.FromMilliseconds(1), span);
    }

    /// <summary>Serialised the way the CDR endpoint expects: UTC instant, explicit Z.</summary>
    [Fact]
    public void SerialisesAsUtcIso8601()
    {
        var window = CallHistoryWindow.ForLocalDay(Local(2026, 8, 20, 10, 0, Tajikistan));

        Assert.Equal("2026-08-19T19:00:00.000Z", window.From);
        Assert.Equal("2026-08-20T18:59:59.999Z", window.To);
    }

    /// <summary>At UTC+0 the local day and the UTC day are the same day.</summary>
    [Fact]
    public void MatchesTheUtcDayWhenTheOffsetIsZero()
    {
        var window = CallHistoryWindow.ForLocalDay(Local(2026, 8, 20, 10, 0, TimeSpan.Zero));

        Assert.Equal("2026-08-20T00:00:00.000Z", window.From);
        Assert.Equal("2026-08-20T23:59:59.999Z", window.To);
    }
}
