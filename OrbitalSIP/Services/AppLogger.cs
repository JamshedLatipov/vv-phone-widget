using System;
using System.IO;
using System.Text;

namespace OrbitalSIP.Services
{
    public static class AppLogger
    {
        private static readonly object _lock = new();
        private static readonly string _logFilePath;

        static AppLogger()
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OrbitalSIP", "logs");
            Directory.CreateDirectory(logDir);
            _logFilePath = Path.Combine(logDir, "app.log");
        }

        public static void Log(string tag, string message)
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{tag}] {message}";
            System.Diagnostics.Debug.WriteLine(line);
            lock (_lock)
            {
                File.AppendAllText(_logFilePath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
    }
}
