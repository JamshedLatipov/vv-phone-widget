using System;
using System.IO;

namespace OrbitalSIP.Services.Logging
{
    /// <summary>
    /// Where the app's logs live. One place, because the directory used to be spelled out at
    /// three separate call sites — and only two of them were the app.
    ///
    /// <see cref="AppLogger"/> is a static that resolves its path at type initialisation, so
    /// merely touching it from a test host binds the log to the real installation under
    /// %APPDATA%. That was harmless while the writer only appended; once it also adopts and
    /// sweeps files, a test run silently applies the retention window to logs somebody may
    /// still need. <see cref="DirectoryVariable"/> is how the test suite — or a support build
    /// collecting logs somewhere else — points that away from the real directory.
    /// </summary>
    public static class LogPaths
    {
        /// <summary>Environment variable that redirects the whole log directory when set.</summary>
        public const string DirectoryVariable = "ORBITALSIP_LOG_DIR";

        /// <summary>Directory every log file lives in. Read fresh, so setting the variable before first use is enough.</summary>
        public static string Directory
        {
            get
            {
                var overridden = Environment.GetEnvironmentVariable(DirectoryVariable);
                if (!string.IsNullOrWhiteSpace(overridden)) return overridden;

                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "OrbitalSIP", "logs");
            }
        }

        /// <summary>Full path of one log file in <see cref="Directory"/>.</summary>
        public static string File(string fileName) => Path.Combine(Directory, fileName);
    }
}
