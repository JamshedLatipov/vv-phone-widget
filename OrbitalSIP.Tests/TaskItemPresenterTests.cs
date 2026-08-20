using System;
using OrbitalSIP.Models;
using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

public class TaskItemPresenterTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 21, 14, 0, 0, TimeSpan.FromHours(5));

    private static TaskItem Task(string? status = "pending", DateTimeOffset? due = null, string? priority = null) =>
        new() { Id = 1, Title = "Перезвонить", Status = status, DueDate = due, Priority = priority };

    [Fact]
    public void TaskPastItsDueDateIsOverdue()
    {
        Assert.True(TaskItemPresenter.IsOverdue(Task(due: Now.AddHours(-1)), Now));
    }

    [Fact]
    public void TaskDueLaterIsNotOverdue()
    {
        Assert.False(TaskItemPresenter.IsOverdue(Task(due: Now.AddHours(1)), Now));
    }

    /// <summary>
    /// The backend's own predicate is a strict "dueDate < NOW()", so a task due at this
    /// exact instant is still on time. Matching it keeps the row list and the badge from
    /// disagreeing by one.
    /// </summary>
    [Fact]
    public void TaskDueExactlyNowIsNotYetOverdue()
    {
        Assert.False(TaskItemPresenter.IsOverdue(Task(due: Now), Now));
    }

    [Theory]
    [InlineData("done")]
    [InlineData("completed")]
    public void FinishedTaskIsNeverOverdue(string status)
    {
        Assert.False(TaskItemPresenter.IsOverdue(Task(status, Now.AddDays(-9)), Now));
    }

    [Fact]
    public void TaskWithNoDeadlineIsNeverOverdue()
    {
        Assert.False(TaskItemPresenter.IsOverdue(Task(due: null), Now));
    }

    [Fact]
    public void MissingDeadlineHasNoBucket()
    {
        Assert.Equal(DueBucket.None, TaskItemPresenter.Bucket(Task(due: null), Now));
    }

    [Fact]
    public void PastDeadlineBucketsAsOverdue()
    {
        Assert.Equal(DueBucket.Overdue, TaskItemPresenter.Bucket(Task(due: Now.AddMinutes(-5)), Now));
    }

    [Fact]
    public void DeadlineLaterTodayBucketsAsToday()
    {
        Assert.Equal(DueBucket.Today, TaskItemPresenter.Bucket(Task(due: Now.AddHours(2)), Now));
    }

    [Fact]
    public void DeadlineTomorrowBucketsAsTomorrow()
    {
        Assert.Equal(DueBucket.Tomorrow, TaskItemPresenter.Bucket(Task(due: Now.AddDays(1)), Now));
    }

    [Fact]
    public void DeadlineFurtherOutBucketsAsLater()
    {
        Assert.Equal(DueBucket.Later, TaskItemPresenter.Bucket(Task(due: Now.AddDays(4)), Now));
    }

    /// <summary>
    /// A finished task keeps its bucket off Overdue even with a deadline long gone —
    /// otherwise the "Все" filter would paint every closed task red.
    /// </summary>
    [Fact]
    public void FinishedTaskWithAnOldDeadlineDoesNotBucketAsOverdue()
    {
        Assert.NotEqual(DueBucket.Overdue, TaskItemPresenter.Bucket(Task("done", Now.AddDays(-3)), Now));
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
