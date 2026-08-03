using System;
using OrbitalSIP.Models;
using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

public class ActiveCallSmsLifecycleTests
{
    [Theory]
    [InlineData(CallState.Idle)]
    [InlineData(CallState.Ringing)]
    [InlineData(CallState.IncomingRinging)]
    public void CapturedTerminalOrReplacementEvent_InvalidatesPriorGenerationEvenWhenLaterStateIsActive(CallState capturedState)
    {
        using var guard = new ActiveCallSmsLaunchGuard();
        Assert.True(guard.TryBegin("same-number", "+992 ** *** 12 34", out var priorSnapshot, out _));
        Action? queuedUiAction = null;

        if (ActiveCallSmsLifecycle.ShouldInvalidate(capturedState))
            queuedUiAction = guard.Invalidate;

        var laterState = CallState.Active;
        Assert.Equal(CallState.Active, laterState);
        queuedUiAction?.Invoke();

        Assert.False(guard.IsCurrent(priorSnapshot));
    }

    [Theory]
    [InlineData(CallState.Active)]
    [InlineData(CallState.OnHold)]
    public void CapturedCurrentCallEvent_DoesNotInvalidateCurrentGeneration(CallState capturedState)
    {
        using var guard = new ActiveCallSmsLaunchGuard();
        Assert.True(guard.TryBegin("call-a", "+992 ** *** 12 34", out var snapshot, out _));

        if (ActiveCallSmsLifecycle.ShouldInvalidate(capturedState))
            guard.Invalidate();

        Assert.True(guard.IsCurrent(snapshot));
    }
}
