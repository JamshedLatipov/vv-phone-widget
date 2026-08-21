using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

/// <summary>
/// The four things the tasks screen can be looking at, and which one a given pair of
/// responses adds up to. Every defect found in this screen so far was a defect in this
/// decision rather than in the drawing of it.
/// </summary>
public class TaskListOutcomeTests
{
    private static TaskListState Of(TaskFetch first, TaskFetch second, int taskCount,
                                    bool forbidden = false, bool unassignable = false) =>
        TaskListOutcome.Of(new[] { first, second }, taskCount, forbidden, unassignable);

    [Fact]
    public void BothHalvesAnsweredWithTasksIsReady()
    {
        Assert.Equal(TaskListState.Ready, Of(TaskFetch.Answered, TaskFetch.Answered, 3));
    }

    [Fact]
    public void BothHalvesAnsweredWithNothingIsEmpty()
    {
        Assert.Equal(TaskListState.Empty, Of(TaskFetch.Answered, TaskFetch.Answered, 0));
    }

    /// <summary>The All chip makes one request, so the second slot is never attempted.</summary>
    [Theory]
    [InlineData(2, TaskListState.Ready)]
    [InlineData(0, TaskListState.Empty)]
    public void OneRequestIsEnoughToKnow(int taskCount, TaskListState expected)
    {
        Assert.Equal(expected, TaskListOutcome.Of(new[] { TaskFetch.Answered }, taskCount, false, false));
    }

    [Fact]
    public void AFailedFirstHalfIsAFailedLoad()
    {
        Assert.Equal(TaskListState.Failed, Of(TaskFetch.Failed, TaskFetch.Skipped, 0));
    }

    /// <summary>
    /// Half an answer is not the operator's open tasks. The pending half alone is missing
    /// every task already started, and nothing on screen would say so — which is the exact
    /// disagreement with the badge that asking twice exists to prevent.
    /// </summary>
    [Fact]
    public void AFailedSecondHalfIsAFailedLoadEvenWithTasksInHand()
    {
        Assert.Equal(TaskListState.Failed, Of(TaskFetch.Answered, TaskFetch.Failed, 5));
    }

    /// <summary>
    /// Nothing asked and no reason given. It should not arise; if it does, nobody learned
    /// there were no tasks, so "could not load" is the honest end of it.
    /// </summary>
    [Fact]
    public void NothingAttemptedAndNoReasonIsAFailedLoad()
    {
        Assert.Equal(TaskListState.Failed, Of(TaskFetch.Skipped, TaskFetch.Skipped, 0));
    }

    /// <summary>
    /// A 403 fails its request as well as refusing it, so the two are always seen
    /// together. Read as a failure it would put a retryable sentence next to a refresh
    /// button that can never help.
    /// </summary>
    [Fact]
    public void ARefusalOutranksTheRequestItFailed()
    {
        Assert.Equal(TaskListState.Refused, Of(TaskFetch.Failed, TaskFetch.Skipped, 0, forbidden: true));
    }

    /// <summary>Once known, the flag skips the requests entirely — the state is the same.</summary>
    [Fact]
    public void ARefusalAlreadyKnownNeedsNoRequestAtAll()
    {
        Assert.Equal(TaskListState.Refused, Of(TaskFetch.Skipped, TaskFetch.Skipped, 0, forbidden: true));
    }

    /// <summary>
    /// Rows in hand do not soften a refusal: the ability is gone, and showing what the
    /// last load happened to fetch under a chip that still works would be a list the
    /// backend has just said this operator may not have.
    /// </summary>
    [Fact]
    public void ARefusalOutranksTasksAlreadyFetched()
    {
        Assert.Equal(TaskListState.Refused, Of(TaskFetch.Answered, TaskFetch.Failed, 4, forbidden: true));
    }

    /// <summary>
    /// No user id in the session means no request was made and none can be. That is not a
    /// failure the operator can retry, and telling them it is puts a transient sentence
    /// beside a refresh button that will never help — the account genuinely cannot be
    /// assigned tasks, which is what "no access" says.
    /// </summary>
    [Fact]
    public void AnAccountThatCannotBeAssignedTasksIsRefusedRatherThanFailed()
    {
        Assert.Equal(TaskListState.Refused, Of(TaskFetch.Skipped, TaskFetch.Skipped, 0, unassignable: true));
    }

    [Fact]
    public void BothSignalsAtOnceStillReadsAsRefused()
    {
        Assert.Equal(TaskListState.Refused,
            Of(TaskFetch.Skipped, TaskFetch.Skipped, 0, forbidden: true, unassignable: true));
    }
}
