using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Sentry;

namespace OrbitalSIP
{
    internal static class Program
    {
        private const string PipeName = "OrbitalSIP_TelPipe";

        /// <summary>Phone number passed on the command line at launch (tel: link), if any.</summary>
        public static string? InitialDialNumber { get; private set; }

        /// <summary>Raised when a tel: link is opened while the app is already running.</summary>
        public static event Action<string>? DialRequested;

        [STAThread]
        public static void Main(string[] args)
        {
            var number = ExtractTelNumber(args);

            using var mutex = new Mutex(true, "OrbitalSIP_SingleInstance", out bool createdNew);
            if (!createdNew)
            {
                // Another instance is already running — forward the number to it and exit.
                if (!string.IsNullOrEmpty(number))
                    SendNumberToRunningInstance(number!);
                return;
            }

            InitialDialNumber = number;
            StartPipeServer();

            using var _ = SentrySdk.Init(o =>
            {
                o.Dsn = "https://27b96d6e386862f38f9b01254edcfba8@o132137.ingest.us.sentry.io/4511144864514048";
                o.AutoSessionTracking = true;
                o.IsGlobalModeEnabled = true;
                // Capture unhandled exceptions automatically — no need to hook AppDomain manually.
                o.CaptureFailedRequests = false;

                o.Debug = false;
                o.TracesSampleRate = 0;
            });

            // Catch any unhandled exception on background threads / async voids —
            // report to Sentry AND write to the local crash log.
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                LogFatalException("UnhandledException", e.ExceptionObject as Exception);

            // Catch unobserved task exceptions (fire-and-forget Tasks that threw).
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                e.SetObserved(); // prevent process termination regardless

                // SocketException with OperationAborted (995) is expected during shutdown —
                // SIPSorcery's internal receive loops get cancelled when the transport is disposed.
                // Skip logging to avoid noisy crash reports.
                if (IsOperationAborted(e.Exception))
                    return;

                LogFatalException("UnobservedTaskException", e.Exception);
            };

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }

        private static bool IsOperationAborted(Exception? ex)
        {
            if (ex == null) return false;
            if (ex is System.Net.Sockets.SocketException se && se.SocketErrorCode == System.Net.Sockets.SocketError.OperationAborted)
                return true;
            if (ex is AggregateException ae)
            {
                foreach (var inner in ae.Flatten().InnerExceptions)
                {
                    if (IsOperationAborted(inner))
                        return true;
                }
            }
            if (ex.InnerException != null)
                return IsOperationAborted(ex.InnerException);
            return false;
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

        // ── tel: / callto: / sip: protocol handling ───────────────────────────

        /// <summary>Extracts a dialable phone number from a tel:/callto:/sip: URI argument.</summary>
        private static string? ExtractTelNumber(string[] args)
        {
            foreach (var a in args)
            {
                if (string.IsNullOrWhiteSpace(a)) continue;
                var s = a.Trim();
                foreach (var scheme in new[] { "tel:", "callto:", "sip:" })
                {
                    if (s.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
                        return NormalizeNumber(s.Substring(scheme.Length));
                }
            }
            return null;
        }

        /// <summary>Keeps only dial-relevant characters from a raw URI body.</summary>
        private static string NormalizeNumber(string raw)
        {
            try { raw = Uri.UnescapeDataString(raw); } catch { /* leave as-is */ }
            raw = raw.TrimStart('/');
            var at = raw.IndexOf('@');
            if (at >= 0) raw = raw.Substring(0, at); // strip sip:user@host host part

            var sb = new StringBuilder();
            foreach (var c in raw)
                if (char.IsDigit(c) || c == '+' || c == '*' || c == '#')
                    sb.Append(c);
            return sb.ToString();
        }

        /// <summary>Background pipe server: receives numbers from secondary launches.</summary>
        private static void StartPipeServer()
        {
            var t = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        using var server = new NamedPipeServerStream(
                            PipeName, PipeDirection.In, 1,
                            PipeTransmissionMode.Byte, PipeOptions.None);
                        server.WaitForConnection();
                        using var reader = new StreamReader(server, Encoding.UTF8);
                        var num = reader.ReadLine();
                        if (!string.IsNullOrWhiteSpace(num))
                            DialRequested?.Invoke(num!.Trim());
                    }
                    catch (Exception ex)
                    {
                        LogFatalException("PipeServer", ex);
                        Thread.Sleep(500);
                    }
                }
            })
            { IsBackground = true, Name = "OrbitalSIP-TelPipe" };
            t.Start();
        }

        /// <summary>Sends the dialed number to the already-running instance.</summary>
        private static void SendNumberToRunningInstance(string number)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                client.Connect(2000);
                using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
                writer.WriteLine(number);
            }
            catch { /* running instance not ready — nothing we can do */ }
        }
    }
}
