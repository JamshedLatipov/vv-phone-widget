using System;
using OrbitalSIP.Models;
using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

/// <summary>
/// One rule failing quietly here strands an operator mid-call: either the
/// caller never learns a transfer worked, or — the worse direction — a
/// queue transfer the backend refused or could not resolve goes out as a
/// SIP REFER anyway, aimed at a name Asterisk cannot dial.
/// </summary>
public class TransferOutcomePresenterTests
{
    /// <summary>
    /// Every (kind, outcome) pair from the transfer feature's routing table,
    /// named after what actually happens rather than the enum values alone —
    /// a mis-ordered or copy-pasted `when` guard changes behaviour without
    /// changing any of these names, so it is the assertion that has to catch
    /// it, not the title.
    /// </summary>
    [Theory]
    [InlineData(TransferTargetKind.Extension, TransferOutcome.Ok, TransferOutcomeAction.NotifySuccess)]
    [InlineData(TransferTargetKind.Extension, TransferOutcome.ChannelUnresolved, TransferOutcomeAction.ReferFallback)]
    [InlineData(TransferTargetKind.Extension, TransferOutcome.Failed, TransferOutcomeAction.NotifyFailure)]
    [InlineData(TransferTargetKind.Queue, TransferOutcome.Ok, TransferOutcomeAction.NotifySuccess)]
    [InlineData(TransferTargetKind.Queue, TransferOutcome.ChannelUnresolved, TransferOutcomeAction.NotifyFailure)]
    [InlineData(TransferTargetKind.Queue, TransferOutcome.Failed, TransferOutcomeAction.NotifyFailure)]
    public void SelectAction_MatchesTheRoutingTable(
        TransferTargetKind kind, TransferOutcome outcome, TransferOutcomeAction expected)
    {
        Assert.Equal(expected, TransferOutcomePresenter.SelectAction(kind, outcome));
    }

    /// <summary>
    /// The row the feature's design calls out by name: a queue with an
    /// unresolved channel is "could not ask" exactly like an extension's is,
    /// but there is no REFER-safe number behind a queue name, so this is the
    /// one case where "could not ask" must NOT be read as permission to
    /// REFER. Asserted on its own, on top of the table sweep above, because
    /// this is precisely the row a reordered or copy-pasted `when` guard
    /// would silently flip — and it is the row this task's brief warned is
    /// the one that must never regress.
    /// </summary>
    [Fact]
    public void Queue_WithChannelUnresolved_FailsInsteadOfFallingBackToRefer()
    {
        var action = TransferOutcomePresenter.SelectAction(TransferTargetKind.Queue, TransferOutcome.ChannelUnresolved);

        Assert.Equal(TransferOutcomeAction.NotifyFailure, action);
        Assert.NotEqual(TransferOutcomeAction.ReferFallback, action);
    }

    /// <summary>
    /// Sweeps every current outcome for a queue target: whatever
    /// TransferOutcome grows into later, ReferFallback must never come out
    /// the other end for a queue. A REFER aimed at a queue name goes nowhere
    /// on the wire and strands the caller on a call the operator believes
    /// they handed off — see TransferOutcomePresenter's class doc comment.
    /// </summary>
    [Fact]
    public void Queue_NeverFallsBackToRefer_ForAnyOutcome()
    {
        foreach (var outcome in Enum.GetValues<TransferOutcome>())
        {
            var action = TransferOutcomePresenter.SelectAction(TransferTargetKind.Queue, outcome);

            Assert.True(
                action != TransferOutcomeAction.ReferFallback,
                $"Queue + {outcome} produced ReferFallback — a queue name is not dialable over SIP REFER.");
        }
    }

    /// <summary>
    /// ReferFallback is reachable at all only for an Extension target with
    /// ChannelUnresolved. This sweeps the full (kind, outcome) grid — not
    /// just the queue rows above — to pin that down as the single exception
    /// rather than one of several.
    /// </summary>
    [Fact]
    public void ReferFallback_IsReachableOnlyForExtensionWithChannelUnresolved()
    {
        foreach (var kind in Enum.GetValues<TransferTargetKind>())
        foreach (var outcome in Enum.GetValues<TransferOutcome>())
        {
            var action = TransferOutcomePresenter.SelectAction(kind, outcome);
            var isTheOneException = kind == TransferTargetKind.Extension && outcome == TransferOutcome.ChannelUnresolved;

            Assert.True(
                (action == TransferOutcomeAction.ReferFallback) == isTheOneException,
                $"kind={kind}, outcome={outcome}: expected ReferFallback only for Extension+ChannelUnresolved, got {action}.");
        }
    }

    /// <summary>A successful transfer reports success regardless of what was picked.</summary>
    [Fact]
    public void Ok_AlwaysNotifiesSuccess_RegardlessOfKind()
    {
        foreach (var kind in Enum.GetValues<TransferTargetKind>())
        {
            Assert.Equal(
                TransferOutcomeAction.NotifySuccess,
                TransferOutcomePresenter.SelectAction(kind, TransferOutcome.Ok));
        }
    }

    /// <summary>
    /// A refusal (or an ambiguous POST-phase timeout) never REFERs for
    /// either kind — the backend already answered, so a REFER behind it
    /// could go behind a deliberate refusal or race a transfer that already
    /// happened. See TransferService.TransferAsync's doc comment.
    /// </summary>
    [Fact]
    public void Failed_AlwaysNotifiesFailure_RegardlessOfKind()
    {
        foreach (var kind in Enum.GetValues<TransferTargetKind>())
        {
            Assert.Equal(
                TransferOutcomeAction.NotifyFailure,
                TransferOutcomePresenter.SelectAction(kind, TransferOutcome.Failed));
        }
    }
}
