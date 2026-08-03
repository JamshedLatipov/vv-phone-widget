using System;
using System.Threading;

namespace OrbitalSIP.Models;

public readonly record struct ActiveCallSmsLaunchSnapshot(
    long Generation,
    string CallIdentity,
    string DisplayNumber);

/// <summary>Owns one active-call SMS launch and invalidates it when its call view expires.</summary>
public sealed class ActiveCallSmsLaunchGuard : IDisposable
{
    private long _generation;
    private bool _launchInProgress;
    private CancellationTokenSource? _cancellation;

    public bool TryBegin(
        string callIdentity,
        string displayNumber,
        out ActiveCallSmsLaunchSnapshot snapshot,
        out CancellationToken cancellationToken)
    {
        if (_launchInProgress)
        {
            snapshot = default;
            cancellationToken = default;
            return false;
        }

        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        _launchInProgress = true;
        snapshot = new ActiveCallSmsLaunchSnapshot(_generation, callIdentity, displayNumber);
        cancellationToken = _cancellation.Token;
        return true;
    }

    public bool IsCurrent(ActiveCallSmsLaunchSnapshot snapshot) =>
        _launchInProgress && snapshot.Generation == _generation;

    public void Complete(ActiveCallSmsLaunchSnapshot snapshot)
    {
        if (!IsCurrent(snapshot))
            return;

        _launchInProgress = false;
        _cancellation?.Dispose();
        _cancellation = null;
    }

    public void Invalidate()
    {
        _generation++;
        _launchInProgress = false;
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
    }

    public void Dispose() => Invalidate();
}
