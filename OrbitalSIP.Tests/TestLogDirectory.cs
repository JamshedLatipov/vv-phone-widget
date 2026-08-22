using System;
using System.IO;
using System.Runtime.CompilerServices;
using OrbitalSIP.Services.Logging;

namespace OrbitalSIP.Tests
{
    /// <summary>
    /// Points the app's logs at a scratch directory for the whole test run.
    ///
    /// <c>AppLogger</c> is a static that resolves its path at type initialisation, and plenty
    /// of code under test logs through it — so simply exercising the audio endpoint used to
    /// append test lines to the real installation's log under %APPDATA%. Once the writer also
    /// began adopting undated files and sweeping expired ones, that stopped being cosmetic: a
    /// test run applied the retention window to logs somebody may still have needed, and the
    /// first run of this suite deleted two of them.
    ///
    /// A module initialiser runs before any test and before anything can touch
    /// <c>AppLogger</c>, which is the only point early enough to redirect it.
    /// </summary>
    internal static class TestLogDirectory
    {
        [ModuleInitializer]
        internal static void Redirect()
        {
            // Left in place after the run: the log of a failed test is worth reading, and the
            // OS clears the temp directory eventually.
            var scratch = Path.Combine(Path.GetTempPath(), "OrbitalSIP-testlogs");
            Directory.CreateDirectory(scratch);
            Environment.SetEnvironmentVariable(LogPaths.DirectoryVariable, scratch);
        }
    }
}
