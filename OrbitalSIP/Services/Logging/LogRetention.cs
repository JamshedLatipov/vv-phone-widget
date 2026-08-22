using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace OrbitalSIP.Services.Logging
{
    /// <summary>
    /// Pure retention policy for the app's log directory: which day a file belongs to, and
    /// which days have fallen out of the window. Kept free of IO so the decisions are testable.
    ///
    /// Size-based rotation on its own cannot express "the last three days": it keeps whatever
    /// the last few megabytes happen to be, so a quiet week leaves months on disk while a busy
    /// hour throws away the morning. Dating every file is what makes the window mean anything —
    /// <see cref="LogRotation"/> still handles overflow *within* a day, and its generations
    /// expire together with the day they belong to.
    /// </summary>
    public static class LogRetention
    {
        /// <summary>Days of history kept, counting today.</summary>
        public const int DefaultKeepDays = 3;

        private const string DateFormat = "yyyy-MM-dd";

        /// <summary>Live file for one day: <c>app.log</c> + 2026-08-22 becomes <c>app-2026-08-22.log</c>.</summary>
        public static string DailyPath(string basePath, DateTime date)
        {
            var directory = Path.GetDirectoryName(basePath);
            var name = Path.GetFileNameWithoutExtension(basePath);
            var extension = Path.GetExtension(basePath);
            var dated = $"{name}-{date.ToString(DateFormat, CultureInfo.InvariantCulture)}{extension}";

            return string.IsNullOrEmpty(directory) ? dated : Path.Combine(directory, dated);
        }

        /// <summary>Directory glob matching every dated file of this log and nothing else's.</summary>
        public static string SearchPattern(string basePath) =>
            $"{Path.GetFileNameWithoutExtension(basePath)}-*{Path.GetExtension(basePath)}";

        /// <summary>
        /// The day <paramref name="candidatePath"/> belongs to, or null when it is not one of
        /// ours. Within-day generations report the same day as the live file, so they expire
        /// with it. Only the file name is read — the directory is not compared, because the
        /// caller found the candidate by listing the log directory in the first place.
        /// </summary>
        public static DateTime? DateOf(string basePath, string candidatePath)
        {
            var name = Path.GetFileNameWithoutExtension(basePath);
            var extension = Path.GetExtension(basePath);

            var candidate = Path.GetFileName(candidatePath);
            if (string.IsNullOrEmpty(candidate)) return null;

            var prefix = name + "-";
            if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
            if (!candidate.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) return null;

            // Everything between "app-" and ".log": either the date on its own, or the date
            // followed by a within-day generation ("2026-08-22" / "2026-08-22.1").
            var middle = candidate.Substring(prefix.Length, candidate.Length - prefix.Length - extension.Length);

            int dot = middle.IndexOf('.');
            if (dot >= 0)
            {
                var generation = middle.Substring(dot + 1);
                if (generation.Length == 0) return null;
                foreach (var c in generation)
                    if (!char.IsDigit(c)) return null;

                middle = middle.Substring(0, dot);
            }

            return DateTime.TryParseExact(middle, DateFormat, CultureInfo.InvariantCulture,
                                          DateTimeStyles.None, out var date)
                ? date
                : null;
        }

        /// <summary>Oldest day still inside a <paramref name="keepDays"/>-day window ending today.</summary>
        public static DateTime Oldest(DateTime today, int keepDays) =>
            today.Date.AddDays(-(Math.Max(1, keepDays) - 1));

        /// <summary>
        /// Which of <paramref name="candidates"/> have fallen out of the window. Anything this
        /// class cannot date is left out: crash.log, the other log's files and anything saved
        /// by hand are somebody else's, and deleting on a guess is not worth the disk it frees.
        /// A window of zero or less disables retention rather than expiring everything.
        /// </summary>
        public static IReadOnlyList<string> Expired(
            string basePath, IEnumerable<string> candidates, DateTime today, int keepDays)
        {
            if (keepDays <= 0 || candidates == null) return Array.Empty<string>();

            var oldest = Oldest(today, keepDays);
            var expired = new List<string>();

            foreach (var candidate in candidates)
            {
                var date = DateOf(basePath, candidate);
                if (date != null && date.Value.Date < oldest) expired.Add(candidate);
            }

            return expired;
        }

        /// <summary>
        /// Name under which an undated file from before daily naming joins the scheme, or null
        /// when it is not a legacy file of this log.
        ///
        /// <c>app.log</c> and <c>app.1.log</c> carry no date, so <see cref="Expired"/> cannot
        /// see them and they would sit on disk for good — which is exactly the several hundred
        /// megabytes this policy exists to stop. Renaming one to the day it was last written
        /// folds it into the window, and the next sweep removes it on schedule. Nothing is
        /// deleted outright on the strength of a guess about what a file is.
        /// </summary>
        public static string? AdoptedPath(string basePath, string legacyPath, DateTime lastWrite)
        {
            var name = Path.GetFileNameWithoutExtension(basePath);
            var extension = Path.GetExtension(basePath);

            var candidate = Path.GetFileName(legacyPath);
            if (string.IsNullOrEmpty(candidate)) return null;
            if (!candidate.StartsWith(name, StringComparison.OrdinalIgnoreCase)) return null;
            if (!candidate.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) return null;

            // Legacy shapes are "app.log" and "app.<generation>.log" — nothing else. A dated
            // file has "-2026-08-22" here and is already in the scheme; "appx" is another log.
            var middle = candidate.Substring(name.Length, candidate.Length - name.Length - extension.Length);
            if (middle.Length > 0)
            {
                if (middle[0] != '.') return null;
                if (middle.Length == 1) return null;
                for (int i = 1; i < middle.Length; i++)
                    if (!char.IsDigit(middle[i])) return null;
            }

            var directory = Path.GetDirectoryName(basePath);
            var adopted = DailyPath(
                string.IsNullOrEmpty(directory) ? name + extension : Path.Combine(directory, name + extension),
                lastWrite);

            return adopted;
        }
    }
}
