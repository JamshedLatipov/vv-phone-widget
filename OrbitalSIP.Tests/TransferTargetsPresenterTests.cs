using System.Collections.Generic;
using OrbitalSIP.Models;
using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

/// <summary>
/// An empty list of targets and an unreachable backend look identical in the
/// UI unless a state tells them apart: in the first case there really is no
/// one to transfer to, in the second the widget simply got no answer, and
/// the operator needs a retry button.
/// </summary>
public class TransferTargetsPresenterTests
{
    private static TransferTargets Targets(
        IReadOnlyList<TransferOperatorTarget>? operators = null,
        IReadOnlyList<TransferQueueTarget>? queues = null) =>
        new(operators ?? [], queues ?? []);

    /// <summary>
    /// The three shapes SelectState's `targets` parameter can take. The
    /// sweeps below run every other input against all three so a reordered
    /// guard has nowhere to hide behind a shape that happens not to trigger
    /// it.
    /// </summary>
    private static IEnumerable<(string Name, TransferTargets? Targets)> TargetShapes()
    {
        yield return ("null", null);
        yield return ("empty", Targets());
        yield return ("non-empty", Targets(operators: [new TransferOperatorTarget("1042", "Иванов")]));
    }

    /// <summary>
    /// We have moved this hole twice: first by leaving every competing input
    /// switched off, then by fixing that but leaving `targets: null` as the
    /// one input that never varied — a guard reordered to check
    /// `targets == null` before `loading` would have passed unnoticed either
    /// time. This sweeps all three target shapes against both error values
    /// against both forbidden values — twelve combinations — so no guard can
    /// be promoted above `loading` without a failure naming exactly which
    /// combination broke.
    /// </summary>
    [Fact]
    public void Loading_BeatsEverythingElse()
    {
        foreach (var (name, targets) in TargetShapes())
        foreach (var error in new string?[] { null, "boom" })
        foreach (var forbidden in new[] { false, true })
        {
            var actual = TransferTargetsPresenter.SelectState(targets, loading: true, error, forbidden);

            Assert.True(
                actual == TransferPanelState.Loading,
                $"targets={name}, error={error ?? "null"}, forbidden={forbidden}: expected Loading, got {actual}");
        }
    }

    /// <summary>
    /// Same sweep, one input narrower: `loading` is fixed at false (Forbidden
    /// only matters once loading has finished) and `forbidden` at true,
    /// while all three target shapes run against both error values — six
    /// combinations — so a guard reordered ahead of `forbidden` fails loudly
    /// instead of hiding behind whichever shape it happens not to catch.
    /// </summary>
    [Fact]
    public void Forbidden_IsNotAnError_SoNoRetryIsOffered()
    {
        foreach (var (name, targets) in TargetShapes())
        foreach (var error in new string?[] { null, "boom" })
        {
            var actual = TransferTargetsPresenter.SelectState(targets, loading: false, error, forbidden: true);

            Assert.True(
                actual == TransferPanelState.Forbidden,
                $"targets={name}, error={error ?? "null"}: expected Forbidden, got {actual}");
        }
    }

    [Fact]
    public void EmptyTargets_IsDistinctFromAFailedLoad()
    {
        Assert.Equal(
            TransferPanelState.Empty,
            TransferTargetsPresenter.SelectState(Targets(), loading: false, error: null, forbidden: false));
        Assert.Equal(
            TransferPanelState.Error,
            TransferTargetsPresenter.SelectState(null, loading: false, error: "boom", forbidden: false));
    }

    /// <summary>
    /// A non-null but empty result and a failed load must not collapse into
    /// the same state. If the empty/ready fallthrough were ever checked
    /// ahead of the error check, this would return Empty instead of Error:
    /// the operator would see "nobody to transfer to" with no retry button,
    /// and would never learn the list failed to load.
    /// </summary>
    [Fact]
    public void AFailedLoadOutranksAnEmptyResult()
    {
        Assert.Equal(
            TransferPanelState.Error,
            TransferTargetsPresenter.SelectState(Targets(), loading: false, error: "boom", forbidden: false));
    }

    /// <summary>
    /// System.Text.Json binds a missing key, or an explicit `null`, straight
    /// through to TransferTargets.Operators/.Queues regardless of the C#
    /// nullable annotation (see the comment on TransferTargets). The panel
    /// must read that as "nobody to transfer to", not throw.
    /// </summary>
    [Fact]
    public void NullLists_AreTreatedAsEmpty_NotAsAFailure()
    {
        var targets = new TransferTargets(null, null);

        Assert.Equal(
            TransferPanelState.Empty,
            TransferTargetsPresenter.SelectState(targets, loading: false, error: null, forbidden: false));
    }

    [Fact]
    public void NullLists_FilterToEmptyWithoutThrowing()
    {
        var targets = new TransferTargets(null, null);

        Assert.Empty(TransferTargetsPresenter.FilterOperators(targets, query: "", ownExtension: null));
        Assert.Empty(TransferTargetsPresenter.FilterQueues(targets, query: ""));
    }

    [Fact]
    public void Filter_ExcludesTheOperatorThemselves()
    {
        var targets = Targets(operators: [
            new TransferOperatorTarget("1042", "Свободный"),
            new TransferOperatorTarget("1099", "Я сам"),
        ]);

        var rows = TransferTargetsPresenter.FilterOperators(targets, query: "", ownExtension: "1099");

        Assert.Single(rows);
        Assert.Equal("1042", rows[0].Extension);
    }

    [Fact]
    public void Filter_MatchesNameAndExtensionCaseInsensitively()
    {
        var targets = Targets(operators: [
            new TransferOperatorTarget("1042", "Иванов"),
            new TransferOperatorTarget("2050", "Петров"),
        ]);

        Assert.Single(TransferTargetsPresenter.FilterOperators(targets, "иван", ownExtension: null));
        Assert.Single(TransferTargetsPresenter.FilterOperators(targets, "2050", ownExtension: null));
    }

    [Fact]
    public void Filter_SinksUndialableQueuesToTheBottomInsteadOfDroppingThem()
    {
        var targets = Targets(queues: [
            new TransferQueueTarget("Отдел продаж", null, "unsafe-name"),
            new TransferQueueTarget("sales", "Продажи", null),
        ]);

        var rows = TransferTargetsPresenter.FilterQueues(targets, query: "");

        Assert.Equal(2, rows.Count);
        Assert.Equal("sales", rows[0].Name);
        Assert.Equal("Отдел продаж", rows[1].Name);
    }

    [Fact]
    public void QueueDisabledKey_MapsEveryKnownReasonAndFallsBack()
    {
        Assert.Null(TransferTargetsPresenter.QueueDisabledKey(null));
        Assert.Equal("TransferQueueUnsafeName", TransferTargetsPresenter.QueueDisabledKey("unsafe-name"));
        Assert.Equal("TransferQueueUnavailable", TransferTargetsPresenter.QueueDisabledKey("something-new"));
    }
}
