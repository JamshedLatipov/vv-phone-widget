using System;
using OrbitalSIP.Models;

namespace OrbitalSIP.Services
{
    /// <summary>
    /// Everything a task row displays that can be worked out from the task itself.
    ///
    /// Kept apart from the view model — and free of I18nService — so the awkward parts
    /// (what counts as overdue, where "today" ends) are testable without Avalonia. The
    /// view model layers the translated words on top.
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

            var dueDay = due.ToLocalTime().Date;
            var today = now.ToLocalTime().Date;

            if (dueDay == today) return DueBucket.Today;
            if (dueDay == today.AddDays(1)) return DueBucket.Tomorrow;
            return DueBucket.Later;
        }

        /// <summary>Time alone for today, day and time for anything further out.</summary>
        public static string TimeText(DateTimeOffset? due, DateTimeOffset now)
        {
            if (due is not { } value) return string.Empty;

            var local = value.ToLocalTime();
            return local.Date == now.ToLocalTime().Date
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

        private static bool IsFinished(string? status) =>
            status is "done" or "completed";
    }
}
