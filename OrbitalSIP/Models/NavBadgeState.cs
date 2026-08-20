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
    public bool HasOverdueTasks { get; private set; }

    /// <summary>Missed calls the operator has not looked at since last opening Recents.</summary>
    public int NewMissed => Math.Max(0, _missedCalls - _seenMissed);

    public void SetTasks(int pending, int inProgress, int overdue)
    {
        OpenTasks = Math.Max(0, pending) + Math.Max(0, inProgress);
        HasOverdueTasks = overdue > 0;
    }

    /// <summary>
    /// Records the backend's "missed today" total.
    ///
    /// A total below the previous one means the backend's counter restarted at midnight,
    /// not that calls were un-missed. The watermark is re-seated to zero, not to the new
    /// total, because doing the latter would swallow every call missed between the
    /// rollover and the poll that catches it — the interval is two minutes, so that gap
    /// is routine, not a corner case.
    /// </summary>
    public void SetMissed(int missedCalls)
    {
        var value = Math.Max(0, missedCalls);

        // A total below the previous one can only mean the backend's "missed today"
        // counter restarted at midnight — calls are never un-missed. Everything it
        // reports after a restart is therefore unseen, which is why the watermark goes
        // to zero and not to the new total: reseating it to the total would swallow any
        // call missed between the rollover and this poll.
        if (value < _missedCalls) _seenMissed = 0;

        _missedCalls = value;
    }

    public void MarkRecentsSeen() => _seenMissed = _missedCalls;

    /// <summary>Badge text. Empty means "draw nothing"; the pill is 18px and holds two glyphs.</summary>
    public static string FormatCount(int count) =>
        count <= 0 ? string.Empty :
        count > 9  ? "9+" :
        count.ToString();
}
