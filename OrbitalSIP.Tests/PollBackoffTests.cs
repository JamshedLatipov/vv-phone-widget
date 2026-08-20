using System;
using OrbitalSIP.Models;
using Xunit;

namespace OrbitalSIP.Tests;

public class PollBackoffTests
{
    private static readonly TimeSpan Healthy = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan Max = TimeSpan.FromMinutes(5);

    [Fact]
    public void NoFailuresPollsAtTheHealthyInterval()
    {
        Assert.Equal(Healthy, PollBackoff.Next(0, Healthy, Max));
    }

    /// <summary>
    /// The first failure must not already stretch the interval: a single dropped request
    /// is normal, and recovering from it should not cost the operator 40 seconds of stale
    /// presence.
    /// </summary>
    [Fact]
    public void FirstFailureKeepsTheHealthyInterval()
    {
        Assert.Equal(Healthy, PollBackoff.Next(1, Healthy, Max));
    }

    [Theory]
    [InlineData(2, 40)]
    [InlineData(3, 80)]
    [InlineData(4, 160)]
    [InlineData(5, 300)]   // 320 would exceed the cap
    public void IntervalDoublesPerFailureUntilTheCap(int failures, double expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), PollBackoff.Next(failures, Healthy, Max));
    }

    /// <summary>A backend down all shift must settle at the cap, not keep growing.</summary>
    [Theory]
    [InlineData(6)]
    [InlineData(50)]
    [InlineData(int.MaxValue)]
    public void LongOutageStaysAtTheCap(int failures)
    {
        Assert.Equal(Max, PollBackoff.Next(failures, Healthy, Max));
    }

    /// <summary>
    /// Guards the shape of the arithmetic rather than a caller: a negative count must not
    /// come back as a fraction of a second and turn a backoff into a hot loop.
    /// </summary>
    [Fact]
    public void NegativeFailureCountFallsBackToTheHealthyInterval()
    {
        Assert.Equal(Healthy, PollBackoff.Next(-3, Healthy, Max));
    }

    [Fact]
    public void CapBelowTheHealthyIntervalNeverShortensPolling()
    {
        Assert.Equal(Healthy, PollBackoff.Next(9, Healthy, TimeSpan.FromSeconds(5)));
    }
}
