using OrbitalSIP.Models;
using Xunit;

namespace OrbitalSIP.Tests;

public class ActiveCallSmsLaunchGuardTests
{
    [Fact]
    public void TryBegin_SnapshotsCallIdentityAndDisplayNumberForCurrentGeneration()
    {
        using var guard = new ActiveCallSmsLaunchGuard();

        var started = guard.TryBegin("call-a", "+992 ** *** 12 34", out var snapshot, out _);

        Assert.True(started);
        Assert.Equal("call-a", snapshot.CallIdentity);
        Assert.Equal("+992 ** *** 12 34", snapshot.DisplayNumber);
        Assert.True(guard.IsCurrent(snapshot));
    }

    [Fact]
    public void Invalidate_CancelsLookupAndMakesItsSnapshotIneligibleToOpen()
    {
        using var guard = new ActiveCallSmsLaunchGuard();
        Assert.True(guard.TryBegin("call-a", "+992 ** *** 12 34", out var snapshot, out var cancellationToken));

        guard.Invalidate();

        Assert.True(cancellationToken.IsCancellationRequested);
        Assert.False(guard.IsCurrent(snapshot));
    }

    [Fact]
    public void CompletingStaleLaunch_CannotClearNewCallLaunchGuard()
    {
        using var guard = new ActiveCallSmsLaunchGuard();
        Assert.True(guard.TryBegin("call-a", "+992 ** *** 12 34", out var oldSnapshot, out _));
        guard.Invalidate();
        Assert.True(guard.TryBegin("call-b", "+992 ** *** 56 78", out var newSnapshot, out _));

        guard.Complete(oldSnapshot);

        Assert.True(guard.IsCurrent(newSnapshot));
        Assert.False(guard.TryBegin("call-b", "+992 ** *** 56 78", out _, out _));
    }

    [Fact]
    public void Complete_CurrentLaunchAllowsNextLaunch()
    {
        using var guard = new ActiveCallSmsLaunchGuard();
        Assert.True(guard.TryBegin("call-a", "+992 ** *** 12 34", out var snapshot, out _));

        guard.Complete(snapshot);

        Assert.True(guard.TryBegin("call-a", "+992 ** *** 12 34", out _, out _));
    }
}
