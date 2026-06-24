import re

with open('OrbitalSIP/Program.cs', 'r') as f:
    content = f.read()

search = """                // SocketException with OperationAborted (995) is expected during shutdown —
                // SIPSorcery's internal receive loops get cancelled when the transport is disposed.
                // Skip logging to avoid noisy crash reports.
                var inner = e.Exception.InnerException ?? e.Exception;
                if (inner is System.Net.Sockets.SocketException se &&
                    se.SocketErrorCode == System.Net.Sockets.SocketError.OperationAborted)
                    return;

                LogFatalException("UnobservedTaskException", e.Exception);"""

replace = """                // SocketException with OperationAborted (995) is expected during shutdown —
                // SIPSorcery's internal receive loops get cancelled when the transport is disposed.
                // Skip logging to avoid noisy crash reports.
                if (IsOperationAborted(e.Exception))
                    return;

                LogFatalException("UnobservedTaskException", e.Exception);"""

new_content = content.replace(search, replace)

search_bottom = """        private static void LogFatalException(string source, Exception? ex)
        {"""

replace_bottom = """        private static bool IsOperationAborted(Exception? ex)
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
        {"""

new_content = new_content.replace(search_bottom, replace_bottom)

with open('OrbitalSIP/Program.cs', 'w') as f:
    f.write(new_content)
