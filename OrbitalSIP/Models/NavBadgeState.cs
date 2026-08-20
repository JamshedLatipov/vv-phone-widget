using System;

namespace OrbitalSIP.Models;

/// <summary>
/// The numbers behind the bottom-nav badges, and nothing else — no HTTP, no timer, no
/// control. <see cref="Services.NavBadgeService"/> owns the polling and hands the
/// answers here; this type decides what they mean.
/// </summary>
public sealed class NavBadgeState
{
    private int _missedCalls;
    private int _seenMissed;

    /// <summary>
    /// Tasks the operator still has to deal with.
    ///
    /// pending + inProgress, because the backend's "pending" filter excludes
    /// in_progress outright (NOT IN ('in_progress', 'done', 'completed')). Overdue is
    /// deliberately absent: it overlaps both buckets, so adding it would count the same
    /// task twice.
    /// </summary>
    public int OpenTasks { get; private set; }

    /// <summary>True when at least one open task is past its due date.</summary>
    public bool TasksAlert { get; private set; }

    /// <summary>Missed calls the operator has not looked at since last opening Recents.</summary>
    public int NewMissed => Math.Max(0, _missedCalls - _seenMissed);

    public void SetTasks(int pending, int inProgress, int overdue)
    {
        OpenTasks = Math.Max(0, pending) + Math.Max(0, inProgress);
        TasksAlert = overdue > 0;
    }

    /// <summary>
    /// Records the backend's "missed today" total.
    ///
    /// A total below the watermark means the day rolled over and the counter restarted,
    /// not that calls were un-missed. Re-seating the watermark there is what keeps the
    /// first missed call after midnight visible instead of silently absorbed.
    /// </summary>
    public void SetMissed(int missedCalls)
    {
        var value = Math.Max(0, missedCalls);
        if (value < _seenMissed) _seenMissed = value;
        _missedCalls = value;
    }

    public void MarkRecentsSeen() => _seenMissed = _missedCalls;

    /// <summary>Badge text. Empty means "draw nothing"; the pill is 18px and holds two glyphs.</summary>
    public static string FormatCount(int count) =>
        count <= 0 ? string.Empty :
        count > 9  ? "9+" :
        count.ToString();
}
