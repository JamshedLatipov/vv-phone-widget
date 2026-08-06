using System;
using System.Collections.Generic;
using System.IO;

namespace OrbitalSIP.Services.Logging
{
    /// <summary>
    /// Pure rotation policy for the app's log files: when to roll over, and what the
    /// archive generations are called. Kept free of IO so the decisions are testable.
    /// </summary>
    public static class LogRotation
    {
        /// <summary>True when appending <paramref name="incomingBytes"/> would push the file past <paramref name="maxBytes"/>. An empty file never rotates, so an oversized single line cannot loop.</summary>
        public static bool ShouldRotate(long currentSize, int incomingBytes, long maxBytes) =>
            maxBytes > 0 && currentSize > 0 && currentSize + incomingBytes > maxBytes;

        /// <summary>Name of an archive generation: generation 0 is the live file, 1 is <c>app.1.log</c>, and so on.</summary>
        public static string ArchivePath(string logPath, int generation)
        {
            if (generation <= 0) return logPath;

            var directory = Path.GetDirectoryName(logPath);
            var name = Path.GetFileNameWithoutExtension(logPath);
            var extension = Path.GetExtension(logPath);
            var archived = $"{name}.{generation}{extension}";

            return string.IsNullOrEmpty(directory) ? archived : Path.Combine(directory, archived);
        }

        /// <summary>Renames to perform for one rollover, oldest generation first so each move lands on a free (or discardable) name.</summary>
        public static IReadOnlyList<(string From, string To)> RollPlan(string logPath, int keep)
        {
            if (keep <= 0) return Array.Empty<(string, string)>();

            var plan = new List<(string From, string To)>(keep);
            for (int generation = keep - 1; generation >= 0; generation--)
                plan.Add((ArchivePath(logPath, generation), ArchivePath(logPath, generation + 1)));

            return plan;
        }
    }
}
