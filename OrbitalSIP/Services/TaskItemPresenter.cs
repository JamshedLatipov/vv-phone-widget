using System;
using OrbitalSIP.Models;

namespace OrbitalSIP.Services
{
    /// <summary>How soon a task is due, in the buckets the row label distinguishes.</summary>
    public enum DueBucket
    {
        /// <summary>No deadline set.</summary>
        None,

        /// <summary>Past its deadline and still open.</summary>
        Overdue,

        /// <summary>Due later today, at the offset <c>now</c> was read at.</summary>
        Today,

        /// <summary>Due on the calendar day immediately after today.</summary>
        Tomorrow,

        /// <summary>
        /// Everything else the row label renders as a plain date, no relative word
        /// attached: a deadline more than a day out, AND a finished task whose deadline
        /// has already passed. IsOverdue excludes done/completed tasks, so a closed task
        /// with an old deadline lands here rather than in Overdue — Later is defined by
        /// what the renderer does with it ("just the date"), not by when it happens, and
        /// that is exactly right for both a far-future deadline and a closed task's
        /// history.
        /// </summary>
        Later,
    }

    /// <summary>
    /// Everything a task row displays that can be worked out from the task itself.
    ///
    /// Kept apart from the view model — and free of I18nService — so the awkward parts
    /// (what counts as overdue, where "today" ends) are testable without Avalonia. The
    /// view model layers the translated words on top.
    ///
    /// Day boundaries always come from the offset the caller's <c>now</c> carries — see
    /// <see cref="DateAt"/> — never from <see cref="TimeZoneInfo.Local"/>.
    /// OrbitalSIP/Models/CallHistoryWindow.cs, in this same folder, already paid for that
    /// exact mistake once: reading the machine's zone instead of the operator's cut the
    /// first five hours off a night-shift operator's own call history.
    /// </summary>
    public static class TaskItemPresenter
    {
        private const string ColorUrgent = "#EF4444";
        private const string ColorHigh   = "#F59E0B";
        private const string ColorMedium = "#60A5FA";
        private const string ColorLow    = "#64748B";

        /// <summary>
        /// Mirrors the backend's own predicate: not done or completed, and strictly past
        /// the deadline. Strict, so a task due at this exact second is still on time — the
        /// badge counts it the same way and the two must not disagree.
        /// </summary>
        public static bool IsOverdue(TaskItem task, DateTimeOffset now) =>
            task.DueDate is { } due
            && due < now
            && !IsFinished(task.Status);

        public static DueBucket Bucket(TaskItem task, DateTimeOffset now)
        {
            if (task.DueDate is not { } due) return DueBucket.None;
            if (IsOverdue(task, now)) return DueBucket.Overdue;

            var dueDay = DateAt(due, now);
            var today = now.Date;

            if (dueDay == today) return DueBucket.Today;
            if (dueDay == today.AddDays(1)) return DueBucket.Tomorrow;
            return DueBucket.Later;
        }

        /// <summary>Time alone for today, day and time for anything further out.</summary>
        public static string TimeText(DateTimeOffset? due, DateTimeOffset now)
        {
            if (due is not { } value) return string.Empty;

            var local = value.ToOffset(now.Offset);
            return DateAt(value, now) == now.Date
                ? local.ToString("HH:mm")
                : local.ToString("dd.MM HH:mm");
        }

        /// <summary>
        /// Stripe colour down the left of a row. An unknown or absent priority gets the
        /// quiet colour rather than no stripe, so rows stay the same width.
        /// </summary>
        public static string PriorityColor(string? priority) => priority?.ToLowerInvariant() switch
        {
            "urgent" => ColorUrgent,
            "high"   => ColorHigh,
            "medium" => ColorMedium,
            _        => ColorLow,
        };

        /// <summary>
        /// The calendar date <paramref name="instant"/> falls on, read at the offset
        /// <paramref name="now"/> carries — e.g. a UTC dueDate from the backend, read
        /// against the operator's own local offset. <see cref="DateTimeOffset.ToLocalTime"/>
        /// would substitute <see cref="TimeZoneInfo.Local"/> instead — the machine's zone,
        /// not the operator's — so Bucket and TimeText both go through this rather than
        /// each converting separately, which is also what stops the two from silently
        /// disagreeing about where a day ends.
        /// </summary>
        private static DateTime DateAt(DateTimeOffset instant, DateTimeOffset now) =>
            instant.ToOffset(now.Offset).Date;

        /// <summary>
        /// Whether the backend considers this task closed.
        ///
        /// Ordinal, deliberately: mirrors the backend's own case-sensitive SQL predicate.
        /// An unrecognised casing such as "Done" is treated as still open, never as
        /// silently finished — a closed task mislabelled overdue is a cosmetic annoyance,
        /// but a genuinely open task mislabelled finished would vanish from the operator's
        /// overdue view entirely.
        ///
        /// Public because the row's tick-off button asks the same question, and the answer
        /// has to be the same one: a second copy of this status list somewhere in the view
        /// is a pair that drifts, and it would drift into a task the list calls overdue and
        /// the button calls done. The asymmetry above holds for the button too — an
        /// unrecognised casing leaves it offered, which costs an idempotent PATCH, while
        /// hiding it from a genuinely open task would strand that task with no way to
        /// close it from here.
        /// </summary>
        public static bool IsFinished(string? status) =>
            status is "done" or "completed";
    }
}
