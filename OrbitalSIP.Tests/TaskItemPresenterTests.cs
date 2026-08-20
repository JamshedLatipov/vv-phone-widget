using System;
using OrbitalSIP.Models;
using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

public class TaskItemPresenterTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 21, 14, 0, 0, TimeSpan.FromHours(5));

    /// <summary>A second "now", at UTC instead of this dev machine's own UTC+5 — used to
    /// prove Bucket and TimeText read the offset the caller passed, not the machine's
    /// zone. UTC is picked because it is the default on most CI runners.</summary>
    private static readonly DateTimeOffset NowAtUtc =
        new(2026, 8, 21, 14, 0, 0, TimeSpan.Zero);

    /// <summary>Near a UTC midnight, so a machine-zone shift — which
    /// DateTimeOffset.ToLocalTime() would apply — can move the due date onto a different
    /// calendar day than this "now"'s own offset would.</summary>
    private static readonly DateTimeOffset NowNearUtcMidnight =
        new(2026, 8, 21, 23, 0, 0, TimeSpan.Zero);

    private static TaskItem MakeTask(string? status = "pending", DateTimeOffset? due = null, string? priority = null) =>
        new() { Id = 1, Title = "Перезвонить", Status = status, DueDate = due, Priority = priority };

    [Fact]
    public void TaskPastItsDueDateIsOverdue()
    {
        Assert.True(TaskItemPresenter.IsOverdue(MakeTask(due: Now.AddHours(-1)), Now));
    }

    [Fact]
    public void TaskDueLaterIsNotOverdue()
    {
        Assert.False(TaskItemPresenter.IsOverdue(MakeTask(due: Now.AddHours(1)), Now));
    }

    /// <summary>
    /// The backend's own predicate is a strict "dueDate < NOW()", so a task due at this
    /// exact instant is still on time. Matching it keeps the row list and the badge from
    /// disagreeing by one.
    /// </summary>
    [Fact]
    public void TaskDueExactlyNowIsNotYetOverdue()
    {
        Assert.False(TaskItemPresenter.IsOverdue(MakeTask(due: Now), Now));
    }

    [Theory]
    [InlineData("done")]
    [InlineData("completed")]
    public void FinishedTaskIsNeverOverdue(string status)
    {
        Assert.False(TaskItemPresenter.IsOverdue(MakeTask(status, Now.AddDays(-9)), Now));
    }

    [Fact]
    public void TaskWithNoDeadlineIsNeverOverdue()
    {
        Assert.False(TaskItemPresenter.IsOverdue(MakeTask(due: null), Now));
    }

    /// <summary>
    /// Older rows can have a null status. NULL is not "done" or "completed", so the
    /// task is still open — this must not throw, and must not be misread as finished.
    /// </summary>
    [Fact]
    public void TaskWithNullStatusPastItsDueDateIsStillOverdue()
    {
        Assert.True(TaskItemPresenter.IsOverdue(MakeTask(status: null, due: Now.AddDays(-1)), Now));
    }

    /// <summary>
    /// Ordinal match, deliberately: mirrors the backend's own case-sensitive SQL
    /// predicate. Pinned so the choice is visible rather than assumed — an unrecognised
    /// casing counts as still open, never as silently finished.
    /// </summary>
    [Fact]
    public void UnrecognisedStatusCasingIsTreatedAsStillOpen()
    {
        Assert.True(TaskItemPresenter.IsOverdue(MakeTask("Done", Now.AddDays(-9)), Now));
    }

    [Fact]
    public void MissingDeadlineHasNoBucket()
    {
        Assert.Equal(DueBucket.None, TaskItemPresenter.Bucket(MakeTask(due: null), Now));
    }

    [Fact]
    public void PastDeadlineBucketsAsOverdue()
    {
        Assert.Equal(DueBucket.Overdue, TaskItemPresenter.Bucket(MakeTask(due: Now.AddMinutes(-5)), Now));
    }

    [Fact]
    public void DeadlineLaterTodayBucketsAsToday()
    {
        Assert.Equal(DueBucket.Today, TaskItemPresenter.Bucket(MakeTask(due: Now.AddHours(2)), Now));
    }

    [Fact]
    public void DeadlineTomorrowBucketsAsTomorrow()
    {
        Assert.Equal(DueBucket.Tomorrow, TaskItemPresenter.Bucket(MakeTask(due: Now.AddDays(1)), Now));
    }

    /// <summary>
    /// Bucket must resolve "tomorrow" at now's own offset, not the machine's. Near a UTC
    /// midnight, shifting both instants into this dev machine's UTC+5 first — what
    /// DateTimeOffset.ToLocalTime() does — lands them on the same calendar day and
    /// misreports Today instead of Tomorrow. A reviewer proved this by actually running
    /// the old code here; this fixture reproduces exactly that failure.
    /// </summary>
    [Fact]
    public void DeadlineTomorrowBucketsAsTomorrowRegardlessOfMachineTimeZone()
    {
        var due = NowNearUtcMidnight.AddHours(1);

        Assert.Equal(DueBucket.Tomorrow, TaskItemPresenter.Bucket(MakeTask(due: due), NowNearUtcMidnight));
    }

    [Fact]
    public void DeadlineFurtherOutBucketsAsLater()
    {
        Assert.Equal(DueBucket.Later, TaskItemPresenter.Bucket(MakeTask(due: Now.AddDays(4)), Now));
    }

    /// <summary>
    /// A finished task past its deadline buckets as Later, not just "not Overdue" —
    /// Later's job is to render a plain date with no relative word, which is right for a
    /// closed task's history too. Pin the actual value: DueBucket.None would also satisfy
    /// "not Overdue" but would hide the date entirely.
    /// </summary>
    [Fact]
    public void FinishedTaskWithAnOldDeadlineBucketsAsLater()
    {
        Assert.Equal(DueBucket.Later, TaskItemPresenter.Bucket(MakeTask("done", Now.AddDays(-3)), Now));
    }

    /// <summary>
    /// Bucket and TimeText are two separate functions with nothing forcing them to agree,
    /// so drive both from one task and instant instead of testing them in isolation — a
    /// dueDate arriving from the backend at UTC, read against the operator's own +5 "now",
    /// is the shape the rest of the suite has no room for.
    /// </summary>
    [Fact]
    public void BucketAndTimeTextAgreeOnTheSameDeadline()
    {
        var due = new DateTimeOffset(2026, 8, 21, 11, 30, 0, TimeSpan.Zero); // 16:30 at Now's +5
        var task = MakeTask(due: due);

        Assert.Equal(DueBucket.Today, TaskItemPresenter.Bucket(task, Now));
        Assert.Equal("16:30", TaskItemPresenter.TimeText(task.DueDate, Now));
    }

    [Theory]
    [InlineData("urgent", "#EF4444")]
    [InlineData("high", "#F59E0B")]
    [InlineData("medium", "#60A5FA")]
    [InlineData("low", "#64748B")]
    public void PriorityPicksItsStripeColour(string priority, string expected)
    {
        Assert.Equal(expected, TaskItemPresenter.PriorityColor(priority));
    }

    /// <summary>The CRM has no enum behind this column, so casing is not guaranteed.</summary>
    [Fact]
    public void PriorityIsMatchedRegardlessOfCasing()
    {
        Assert.Equal("#EF4444", TaskItemPresenter.PriorityColor("URGENT"));
    }

    /// <summary>
    /// A stripe is always drawn, so rows keep the same width whatever the CRM puts in
    /// this column next.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("whatever-the-crm-adds-next")]
    public void UnknownPriorityFallsBackToTheQuietColour(string? priority)
    {
        Assert.Equal("#64748B", TaskItemPresenter.PriorityColor(priority));
    }

    [Fact]
    public void SameDayDeadlineShowsOnlyTheTime()
    {
        Assert.Equal("16:30", TaskItemPresenter.TimeText(Now.AddHours(2).AddMinutes(30), Now));
    }

    /// <summary>
    /// TimeText must format at the offset now carries, not the machine's zone. A reviewer
    /// proved DateTimeOffset.ToLocalTime() breaks this by running the old code on this dev
    /// machine (UTC+5): it silently added five hours here, a divergence the original
    /// UTC+5-only fixture could never have caught since it matched the machine by luck.
    /// </summary>
    [Fact]
    public void SameDayDeadlineShowsOnlyTheTimeRegardlessOfMachineTimeZone()
    {
        Assert.Equal("16:30", TaskItemPresenter.TimeText(NowAtUtc.AddHours(2).AddMinutes(30), NowAtUtc));
    }

    [Fact]
    public void DistantDeadlineShowsDayAndTime()
    {
        var due = new DateTimeOffset(2026, 9, 12, 9, 5, 0, TimeSpan.FromHours(5));
        Assert.Equal("12.09 09:05", TaskItemPresenter.TimeText(due, Now));
    }

    [Fact]
    public void MissingDeadlineHasNoTimeText()
    {
        Assert.Equal(string.Empty, TaskItemPresenter.TimeText(null, Now));
    }
}
