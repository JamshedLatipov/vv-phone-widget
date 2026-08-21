using System;
using System.Linq;
using OrbitalSIP.Models;
using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

/// <summary>
/// The merge behind the Open chip. Two requests, because the backend's pending filter
/// excludes in_progress outright, and one list out of them — which is the one place the
/// screen can end up with a different set of open tasks than the badge counted.
/// </summary>
public class OpenTaskListTests
{
    private static readonly DateTimeOffset Created =
        new(2026, 8, 21, 9, 0, 0, TimeSpan.FromHours(5));

    private static TaskItem Task(int id, string? status = "pending", DateTimeOffset? created = null) =>
        new() { Id = id, Title = $"Task {id}", Status = status, CreatedAt = created ?? Created };

    /// <summary>A row the backend sent without a createdAt, which older ones can be.</summary>
    private static TaskItem Undated(int id) =>
        new() { Id = id, Title = $"Task {id}", Status = "pending", CreatedAt = null };

    [Fact]
    public void BothResponsesContributeTheirTasks()
    {
        var merged = OpenTaskList.From(
            new[] { Task(1), Task(2) },
            new[] { Task(3, "in_progress"), Task(4, "in_progress") });

        Assert.Equal(new[] { 1, 2, 3, 4 }, merged.Select(t => t.Id).OrderBy(id => id));
    }

    [Fact]
    public void AnEmptyResponseContributesNothing()
    {
        Assert.Equal(new[] { 1 }, OpenTaskList.From(new[] { Task(1) }, Array.Empty<TaskItem>()).Select(t => t.Id));
        Assert.Equal(new[] { 2 }, OpenTaskList.From(Array.Empty<TaskItem>(), new[] { Task(2) }).Select(t => t.Id));
    }

    [Fact]
    public void TwoEmptyResponsesMakeAnEmptyList()
    {
        Assert.Empty(OpenTaskList.From(Array.Empty<TaskItem>(), Array.Empty<TaskItem>()));
    }

    /// <summary>
    /// Null is an absent response, not an empty one — a request that failed, or the second
    /// request the caller deliberately never made after a 403. It must contribute nothing
    /// rather than throw on the way past.
    /// </summary>
    [Fact]
    public void AbsentResponsesContributeNothing()
    {
        Assert.Empty(OpenTaskList.From(null, null));
        Assert.Equal(new[] { 1 }, OpenTaskList.From(new[] { Task(1) }, null).Select(t => t.Id));
        Assert.Equal(new[] { 2 }, OpenTaskList.From(null, new[] { Task(2) }).Select(t => t.Id));
    }

    /// <summary>
    /// The same task in both responses means its status moved between the two requests.
    /// It is one task and must be drawn once: duplicated, the list would also stop
    /// agreeing with the badge, which counts each task once.
    /// </summary>
    [Fact]
    public void ATaskInBothResponsesAppearsExactlyOnce()
    {
        var merged = OpenTaskList.From(
            new[] { Task(7), Task(8) },
            new[] { Task(7, "in_progress"), Task(9, "in_progress") });

        Assert.Equal(3, merged.Count);
        Assert.Equal(1, merged.Count(task => task.Id == 7));
    }

    [Fact]
    public void NewestCreatedComesFirst()
    {
        var merged = OpenTaskList.From(
            new[] { Task(1, created: Created), Task(2, created: Created.AddHours(3)) },
            new[] { Task(3, "in_progress", Created.AddHours(1)) });

        Assert.Equal(new[] { 2, 3, 1 }, merged.Select(t => t.Id));
    }

    /// <summary>
    /// Pinned because it is a choice rather than a law: a task with no createdAt sorts
    /// last. Descending by creation puts the newest where the eye lands, and a row that
    /// cannot say when it was made has not earned that spot — so the null is read as "as
    /// old as anything can be" rather than left to sort wherever it happens to land.
    /// </summary>
    [Fact]
    public void TaskWithoutACreationDateSortsLast()
    {
        var merged = OpenTaskList.From(
            new[] { Task(1, created: Created), Undated(2), Task(3, created: Created.AddHours(2)) },
            null);

        Assert.Equal(new[] { 3, 1, 2 }, merged.Select(t => t.Id));
    }
}
