using System;

namespace OrbitalSIP.Models;

/// <summary>
/// How far apart to space polls while the backend is not answering.
///
/// Presence polling ran at a flat 20 s and raised an error banner on every failure, so a
/// backend that was down — or a session that had expired — produced three identical
/// banners a minute for the rest of the shift while continuing to hammer a host that was
/// already in trouble.
/// </summary>
public static class PollBackoff
{
    /// <summary>Doublings before the interval stops growing, independent of the cap.</summary>
    private const int MaxDoublings = 4;

    /// <summary>
    /// Interval to use after <paramref name="consecutiveFailures"/> failures in a row.
    /// Zero failures — or any negative count, which no caller should produce but which
    /// must not turn into a sub-second poll — gives the healthy interval back.
    /// </summary>
    public static TimeSpan Next(int consecutiveFailures, TimeSpan healthy, TimeSpan max)
    {
        if (consecutiveFailures <= 0) return healthy;
        if (max < healthy) return healthy;

        var doublings = Math.Min(consecutiveFailures - 1, MaxDoublings);
        var seconds   = healthy.TotalSeconds * Math.Pow(2, doublings);

        return TimeSpan.FromSeconds(Math.Min(seconds, max.TotalSeconds));
    }
}
