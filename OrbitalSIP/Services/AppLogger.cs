using System;
using System.IO;
using OrbitalSIP.Services.Logging;

namespace OrbitalSIP.Services
{
    /// <summary>
    /// App-wide log. Callers only format a line and hand it to the background writer —
    /// they never touch the disk, because most of the 130-odd call sites sit on the UI,
    /// audio or SIP threads where a synchronous append stalls something the user notices.
    /// </summary>
    public static class AppLogger
    {
        private static readonly AsyncLogWriter _writer;

        static AppLogger()
        {
            _writer = new AsyncLogWriter(LogPaths.File("app.log"));
        }

        public static void Log(string tag, string message)
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{tag}] {message}";
            System.Diagnostics.Debug.WriteLine(line);
            _writer.Write(line);
        }

        /// <summary>Lines dropped because the app out-logged the disk. Non-zero means the log is incomplete.</summary>
        public static long DroppedCount => _writer.DroppedCount;

        /// <summary>Drains the queue to disk. Call on shutdown so the tail of the log survives.</summary>
        public static void Shutdown() => _writer.Dispose();
    }
}
