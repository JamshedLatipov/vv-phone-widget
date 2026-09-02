using System;
using System.IO;
using System.Text;

namespace OrbitalSIP.Services.Logging
{
    /// <summary>
    /// The last-resort log for exceptions that escaped everything else.
    ///
    /// Written synchronously and without a background writer on purpose: the callers are the
    /// unhandled-exception and unobserved-task handlers, and a queue drained by another thread
    /// is exactly what does not survive the process that is on its way down.
    ///
    /// It still has to obey the same retention window as the other two logs, which a bare
    /// append onto an undated crash.log never could: <see cref="LogRetention"/> cannot date
    /// such a file, so nothing swept it and nothing bounded it. One support copy held four
    /// months in a single 2.9 MB file — bigger than the three days of app and SIP logs it came
    /// with, and none of it recent enough to read.
    /// </summary>
    public static class CrashLog
    {
        /// <summary>Appends one entry to today's crash log and sweeps the days that have fallen out of the window.</summary>
        public static void Write(string source, Exception? ex) =>
            Write(LogPaths.File("crash.log"), DateTime.Now, source, ex?.ToString(),
                  LogRetention.DefaultKeepDays);

        /// <param name="basePath">Undated name the daily files are derived from, e.g. <c>…\crash.log</c>.</param>
        /// <param name="now">Clock, injected so the window is testable without waiting days.</param>
        /// <param name="keepDays">Days of history kept, counting today.</param>
        public static void Write(string basePath, DateTime now, string source, string? detail, int keepDays)
        {
            try
            {
                var directory = Path.GetDirectoryName(basePath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                Adopt(basePath);
                Sweep(basePath, now, keepDays);

                var line = $"{now:yyyy-MM-dd HH:mm:ss.fff} [{source}] {detail}{Environment.NewLine}";
                File.AppendAllText(LogRetention.DailyPath(basePath, now), line, Encoding.UTF8);
            }
            catch { /* nowhere left to report */ }
        }

        /// <summary>
        /// Folds the undated file every existing installation already has into the dated
        /// scheme, under the day it was last written. Renamed rather than deleted: the whole
        /// point of this file is that it holds the only record of something, and a guess about
        /// what it is worth is not a reason to throw it away. The next sweep takes it on
        /// schedule.
        /// </summary>
        private static void Adopt(string basePath)
        {
            try
            {
                if (!File.Exists(basePath)) return;

                var adopted = LogRetention.AdoptedPath(basePath, basePath, File.GetLastWriteTime(basePath));
                if (adopted == null || File.Exists(adopted)) return;

                File.Move(basePath, adopted);
            }
            catch { /* housekeeping; never let it stop the crash line from landing */ }
        }

        private static void Sweep(string basePath, DateTime now, int keepDays)
        {
            try
            {
                var directory = Path.GetDirectoryName(basePath);
                if (string.IsNullOrEmpty(directory)) return;

                var dated = Directory.GetFiles(directory, LogRetention.SearchPattern(basePath));
                foreach (var expired in LogRetention.Expired(basePath, dated, now, keepDays))
                    try { File.Delete(expired); } catch { }
            }
            catch { /* same */ }
        }
    }
}
