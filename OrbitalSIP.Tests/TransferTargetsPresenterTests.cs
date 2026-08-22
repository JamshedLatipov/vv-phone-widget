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

    [Fact]
    public void Loading_BeatsEverythingElse()
    {
        // Every other input is live (a non-empty result, an error, AND
        // forbidden) so this only passes if loading is checked first, not
        // merely first among inputs that happen to be switched off.
        var targets = Targets(operators: [new TransferOperatorTarget("1042", "Иванов")]);

        Assert.Equal(
            TransferPanelState.Loading,
            TransferTargetsPresenter.SelectState(targets, loading: true, error: "boom", forbidden: true));
    }

    [Fact]
    public void Forbidden_IsNotAnError_SoNoRetryIsOffered()
    {
        // targets and error are both live here too, so Forbidden has to win
        // on its own precedence, not because nothing else was competing.
        var targets = Targets(operators: [new TransferOperatorTarget("1042", "Иванов")]);

        Assert.Equal(
            TransferPanelState.Forbidden,
            TransferTargetsPresenter.SelectState(targets, loading: false, error: "boom", forbidden: true));
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
