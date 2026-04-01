using System;
using System.IO;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Sentry;

namespace OrbitalSIP
{
    internal static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            using var mutex = new Mutex(true, "OrbitalSIP_SingleInstance", out bool createdNew);
            if (!createdNew)
                return;

            using var _ = SentrySdk.Init(o =>
            {
                o.Dsn = "https://27b96d6e386862f38f9b01254edcfba8@o132137.ingest.us.sentry.io/4511144864514048";
                o.AutoSessionTracking = true;
                o.IsGlobalModeEnabled = true;
                // Capture unhandled exceptions automatically — no need to hook AppDomain manually.
                o.CaptureFailedRequests = false;
#if DEBUG
                o.Debug = true;
                o.TracesSampleRate = 0;
#else
                o.Debug = false;
                o.TracesSampleRate = 0;
#endif
            });

            // Catch any unhandled exception on background threads / async voids —
            // report to Sentry AND write to the local crash log.
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                LogFatalException("UnhandledException", e.ExceptionObject as Exception);

            // Catch unobserved task exceptions (fire-and-forget Tasks that threw).
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                LogFatalException("UnobservedTaskException", e.Exception);
                e.SetObserved(); // prevent process termination
            };

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }

        private static void LogFatalException(string source, Exception? ex)
        {
            if (ex != null)
                SentrySdk.CaptureException(ex);

            try
            {
                var logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "OrbitalSIP", "logs");
                Directory.CreateDirectory(logDir);
                var logPath = Path.Combine(logDir, "crash.log");
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{source}] {ex}{Environment.NewLine}";
                File.AppendAllText(logPath, line, Encoding.UTF8);
            }
            catch { /* nowhere left to report */ }
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
    }
}
