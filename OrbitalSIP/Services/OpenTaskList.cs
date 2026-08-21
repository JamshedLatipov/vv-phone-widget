using System;
using System.Collections.Generic;
using System.Linq;
using OrbitalSIP.Models;

namespace OrbitalSIP.Services
{
    /// <summary>
    /// The operator's open tasks, assembled out of the two responses that make them up.
    ///
    /// "Open" is pending plus in_progress, and the backend treats those as disjoint sets —
    /// its pending filter is literally NOT IN ('in_progress', 'done', 'completed') — so the
    /// list costs two requests and has to be merged. That makes this the one place where
    /// the screen can quietly come to a different total than the badge, which adds the same
    /// two counts in <see cref="Models.NavBadgeState.SetTasks"/>; it lived inside the view
    /// until now, where nothing could assert it.
    /// </summary>
    public static class OpenTaskList
    {
        /// <summary>
        /// Merges the two responses: one row per task id, newest first.
        ///
        /// A null argument is an absent response and contributes nothing — the caller gets
        /// null from TaskService for a request that failed, and for the second request it
        /// deliberately never made. A null *element* contributes nothing either:
        /// System.Text.Json turns a "data": [null] into exactly that, and grouping by
        /// task.Id would dereference it.
        ///
        /// A task can be in both responses at once, which means its status changed between
        /// the two requests. The first occurrence wins, and either would do: being in
        /// either response is what "open" means, so the two copies draw the same row.
        /// "First" is decided by GroupBy, which preserves source order inside a group, and
        /// by Concat putting pending ahead of in_progress — not by the sort, which never
        /// sees the second copy because Select has already collapsed the group before
        /// OrderByDescending runs. (The sort's own stability is real but does something
        /// else: it keeps equal createdAt values in that same order.)
        ///
        /// A task with no createdAt sorts last, which is a choice and not a law:
        /// descending by creation puts the newest at the top, where the eye lands, and a
        /// row that cannot say when it was made has not earned that position. Substituting
        /// <see cref="DateTimeOffset.MinValue"/> is how that is spelled — "as old as
        /// anything can be" — rather than an accident of null sorting.
        /// </summary>
        public static List<TaskItem> From(IEnumerable<TaskItem>? pending, IEnumerable<TaskItem>? inProgress) =>
            (pending ?? Enumerable.Empty<TaskItem>())
                .Concat(inProgress ?? Enumerable.Empty<TaskItem>())
                .Where(task => task is not null)
                .GroupBy(task => task.Id)
                .Select(sameId => sameId.First())
                .OrderByDescending(task => task.CreatedAt ?? DateTimeOffset.MinValue)
                .ToList();
    }
}
