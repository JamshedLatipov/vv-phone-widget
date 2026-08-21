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
    private string? _operatorId;

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

    /// <summary>
    /// Forgets everything when the operator changes.
    ///
    /// The counters and the watermark describe one person's shift. A shared terminal
    /// handed over after a session expiry keeps the same process, so without this the
    /// incoming operator inherits the outgoing one's watermark and their own missed calls
    /// are undercounted until they happen to open Recents. Same person signing back in
    /// keeps their watermark, which is why this turns on identity and not on Stop().
    ///
    /// Identity is the operator's login name, not the access token: a refresh mints a new
    /// token roughly once an hour and must not read as a new person. That is the one way
    /// this differs from <see cref="Services.TaskService.TasksForbidden"/>, which is
    /// keyed on the token precisely because it wants a refresh to re-probe.
    ///
    /// An id that could not be read is "unknown", not "somebody else": it goes missing
    /// whenever the session cannot be inspected — a token just cleared by an expiry, a
    /// settings read that found nothing — and blanking the badges every time the backend
    /// coughed would be a worse lie than a stale number. Unknown is not remembered
    /// either, or the real id arriving on the next poll would itself read as a handover.
    /// </summary>
    public void SetOperator(string? operatorId)
    {
        if (string.IsNullOrEmpty(operatorId)) return;
        if (operatorId == _operatorId) return;

        _operatorId = operatorId;

        OpenTasks = 0;
        HasOverdueTasks = false;
        _missedCalls = 0;
        _seenMissed = 0;
    }

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
