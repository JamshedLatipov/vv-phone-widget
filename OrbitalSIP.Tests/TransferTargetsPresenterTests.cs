using System.Collections.Generic;
using OrbitalSIP.Models;
using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

/// <summary>
/// Пустой список целей и недоступный бэк выглядят в UI одинаково, если их не
/// различать состоянием: в первом случае переводить действительно некому, во
/// втором — виджету просто не ответили, и оператору нужна кнопка повтора.
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
        Assert.Equal(
            TransferPanelState.Loading,
            TransferTargetsPresenter.SelectState(null, loading: true, error: null, forbidden: false));
    }

    [Fact]
    public void Forbidden_IsNotAnError_SoNoRetryIsOffered()
    {
        Assert.Equal(
            TransferPanelState.Forbidden,
            TransferTargetsPresenter.SelectState(null, loading: false, error: null, forbidden: true));
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
