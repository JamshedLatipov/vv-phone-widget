using System;
using System.Net;

namespace OrbitalSIP.Services
{
    /// <summary>
    /// Global error notification hub for displaying HTTP, network, and SIP errors in the UI.
    /// Wire this into MainWindow to show error banners to the user.
    /// </summary>
    public static class HttpErrorNotifier
    {
        /// <summary>
        /// The banner lives in a 320px-wide window with no scrollbar. Anything longer than
        /// this is not read, it just pushes the rest of the panel off screen.
        /// </summary>
        private const int MaxBannerLength = 160;

        public static event Action<string>? ErrorOccurred;

        /// <summary>Raise a plain pre-built message in the UI banner (e.g. audio device warnings).</summary>
        public static void Notify(string message) => ErrorOccurred?.Invoke(Cap(message));

        /// <summary>
        /// Reports a failed backend call.
        ///
        /// The URL and the response body go to the log only. They used to be concatenated
        /// straight into the banner text, which put backend stack traces, internal host
        /// names and query strings (including caller phone numbers) on the operator's
        /// screen, in a window with no room for them. The operator needs to know the call
        /// failed and roughly why; the detail is a support question, and support reads the
        /// log.
        /// </summary>
        public static void NotifyHttpError(string source, string? url, HttpStatusCode statusCode, string? details = null)
        {
            // The URL is redacted before it reaches the log: several routes carry the
            // caller's number in the path or the query string, so logging it verbatim here
            // put straight back on disk what redacting the individual call sites removed.
            AppLogger.Log(source,
                $"HTTP {(int)statusCode} {statusCode} for {Models.LogRedaction.Url(url)}. Body: {details ?? "<empty>"}");

            ErrorOccurred?.Invoke($"{source}: HTTP {(int)statusCode} {statusCode}");
        }

        public static void NotifyException(string source, Exception ex)
        {
            // Log full exception details to file/console
            LogExceptionDetails(source, ex);

            // Notify UI with brief message
            ErrorOccurred?.Invoke(Cap($"{source}: {ex.GetType().Name} - {ex.Message}"));
        }

        private static string Cap(string message)
        {
            if (string.IsNullOrEmpty(message) || message.Length <= MaxBannerLength)
                return message;

            return $"{message[..(MaxBannerLength - 1)]}…";
        }

        private static void LogExceptionDetails(string source, Exception ex)
        {
            var details = new System.Text.StringBuilder();
            details.AppendLine($"[{source}] Exception Details:");
            details.AppendLine($"  Type: {ex.GetType().FullName}");
            details.AppendLine($"  Message: {ex.Message}");

            if (ex.InnerException != null)
            {
                details.AppendLine($"  Inner Exception Type: {ex.InnerException.GetType().FullName}");
                details.AppendLine($"  Inner Message: {ex.InnerException.Message}");
            }

            details.AppendLine($"  StackTrace: {ex.StackTrace}");

            if (ex is System.Net.Http.HttpRequestException hre)
            {
                if (hre.InnerException is System.Net.Sockets.SocketException se)
                {
                    details.AppendLine($"  Socket Error Code: {se.SocketErrorCode}");
                    details.AppendLine($"  Socket Message: {se.Message}");
                }
            }

            AppLogger.Log(source, details.ToString());
        }
    }
}
