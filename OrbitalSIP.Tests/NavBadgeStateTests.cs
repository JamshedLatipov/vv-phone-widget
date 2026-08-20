using OrbitalSIP.Models;
using Xunit;

namespace OrbitalSIP.Tests;

public class NavBadgeStateTests
{
    /// <summary>
    /// The backend reports pending and in_progress as disjoint sets — its "pending"
    /// filter is literally NOT IN ('in_progress', 'done', 'completed'). Counting only
    /// pending would put a 3 on a badge above a list of 5.
    /// </summary>
    [Fact]
    public void OpenTasksAddsPendingAndInProgress()
    {
        var state = new NavBadgeState();

        state.SetTasks(pending: 3, inProgress: 2, overdue: 0);

        Assert.Equal(5, state.OpenTasks);
    }

    /// <summary>
    /// Overdue overlaps both pending and in_progress, so adding it would double-count
    /// the same task. It only decides the colour.
    /// </summary>
    [Fact]
    public void OverdueColoursTheBadgeWithoutInflatingIt()
    {
        var state = new NavBadgeState();

        state.SetTasks(pending: 3, inProgress: 2, overdue: 2);

        Assert.Equal(5, state.OpenTasks);
        Assert.True(state.TasksAlert);
    }

    [Fact]
    public void NoOverdueTasksLeavesTheBadgeUnalarmed()
    {
        var state = new NavBadgeState();

        state.SetTasks(pending: 4, inProgress: 0, overdue: 0);

        Assert.False(state.TasksAlert);
    }

    [Fact]
    public void MissedCallsCountAsNewUntilRecentsIsOpened()
    {
        var state = new NavBadgeState();

        state.SetMissed(3);

        Assert.Equal(3, state.NewMissed);
    }

    [Fact]
    public void OpeningRecentsClearsTheMissedBadge()
    {
        var state = new NavBadgeState();
        state.SetMissed(3);

        state.MarkRecentsSeen();

        Assert.Equal(0, state.NewMissed);
    }

    [Fact]
    public void MissedCallsArrivingAfterRecentsWasOpenedCountAgain()
    {
        var state = new NavBadgeState();
        state.SetMissed(3);
        state.MarkRecentsSeen();

        state.SetMissed(5);

        Assert.Equal(2, state.NewMissed);
    }

    /// <summary>
    /// The counter is "missed today", so a night shift crossing midnight sees it reset
    /// to zero while the watermark still holds yesterday's number. Without re-seating
    /// the watermark, every missed call of the new day would be swallowed until the
    /// operator beat yesterday's total.
    /// </summary>
    [Fact]
    public void CounterResettingAtMidnightReseatsTheWatermark()
    {
        var state = new NavBadgeState();
        state.SetMissed(7);
        state.MarkRecentsSeen();

        state.SetMissed(0);
        Assert.Equal(0, state.NewMissed);

        state.SetMissed(1);
        Assert.Equal(1, state.NewMissed);
    }

    [Fact]
    public void MissedCountNeverGoesNegative()
    {
        var state = new NavBadgeState();
        state.SetMissed(4);
        state.MarkRecentsSeen();

        state.SetMissed(2);

        Assert.Equal(0, state.NewMissed);
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "1")]
    [InlineData(9, "9")]
    [InlineData(10, "9+")]
    [InlineData(250, "9+")]
    [InlineData(-1, "")]
    public void CountIsFormattedForAnEighteenPixelPill(int count, string expected)
    {
        Assert.Equal(expected, NavBadgeState.FormatCount(count));
    }

    [Fact]
    public void FreshStateShowsNothing()
    {
        var state = new NavBadgeState();

        Assert.Equal(0, state.OpenTasks);
        Assert.Equal(0, state.NewMissed);
        Assert.False(state.TasksAlert);
    }
}
