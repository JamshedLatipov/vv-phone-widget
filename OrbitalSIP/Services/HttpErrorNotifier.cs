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
        public static event Action<string>? ErrorOccurred;

        public static void NotifyHttpError(string source, string? url, HttpStatusCode statusCode, string? details = null)
        {
            var message = $"{source}: HTTP {(int)statusCode} {statusCode}";

            if (!string.IsNullOrWhiteSpace(url))
                message += $" ({url})";

            if (!string.IsNullOrWhiteSpace(details))
                message += $" - {details}";

            ErrorOccurred?.Invoke(message);
        }

        public static void NotifyException(string source, Exception ex)
        {
            // Log full exception details to file/console
            LogExceptionDetails(source, ex);
            
            // Notify UI with brief message
            ErrorOccurred?.Invoke($"{source}: {ex.GetType().Name} - {ex.Message}");
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