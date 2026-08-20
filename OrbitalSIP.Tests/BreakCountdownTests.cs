using System;
using OrbitalSIP.Models;
using Xunit;

namespace OrbitalSIP.Tests;

/// <summary>
/// The countdown on the operator's own break.
///
/// It was built from TimeSpan.Minutes, which is the minute component of an hours/minutes
/// split — so the longest break the popup offers, 60 minutes, rendered as 00:00.
/// </summary>
public class BreakCountdownTests
{
    [Fact]
    public void ShowsAFullHourAsSixtyMinutes()
    {
        Assert.Equal("60:00", BreakCountdown.Format(TimeSpan.FromMinutes(60)));
    }

    [Fact]
    public void ShowsJustUnderAnHourNormally()
    {
        Assert.Equal("59:59", BreakCountdown.Format(new TimeSpan(0, 59, 59)));
    }

    [Fact]
    public void PadsBothFieldsToTwoDigits()
    {
        Assert.Equal("05:07", BreakCountdown.Format(new TimeSpan(0, 5, 7)));
    }

    [Fact]
    public void ShowsSecondsOnlyBreaksWithAZeroMinuteField()
    {
        Assert.Equal("00:09", BreakCountdown.Format(TimeSpan.FromSeconds(9)));
    }

    /// <summary>
    /// Nothing offers this today, but the countdown is driven off a server-supplied end
    /// time — so it must not silently wrap if one ever exceeds an hour.
    /// </summary>
    [Fact]
    public void KeepsCountingPastAnHour()
    {
        Assert.Equal("90:00", BreakCountdown.Format(TimeSpan.FromMinutes(90)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-3600)]
    public void ClampsAnElapsedBreakToZero(int seconds)
    {
        Assert.Equal("00:00", BreakCountdown.Format(TimeSpan.FromSeconds(seconds)));
    }
}
