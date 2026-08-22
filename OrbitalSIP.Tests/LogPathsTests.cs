using System;
using System.IO;
using OrbitalSIP.Services.Logging;
using Xunit;

namespace OrbitalSIP.Tests
{
    /// <summary>
    /// Guards the redirect itself. Nothing here mutates the variable: test classes run in
    /// parallel, and flipping the log directory out from under another class is exactly the
    /// kind of global-state edit this redirect exists to prevent.
    /// </summary>
    public class LogPathsTests
    {
        [Fact]
        public void Directory_HonoursTheOverride()
        {
            var overridden = Environment.GetEnvironmentVariable(LogPaths.DirectoryVariable);

            Assert.False(string.IsNullOrWhiteSpace(overridden),
                "the module initialiser should have set the override for the whole run.");
            Assert.Equal(overridden, LogPaths.Directory);
        }

        [Fact]
        public void TheTestRunNeverPointsAtTheRealInstallation()
        {
            // The one that matters. Without the redirect, running this suite adopts and sweeps
            // the logs of the widget installed on this machine — which is how two of them were
            // deleted the first time the retention window shipped.
            var real = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OrbitalSIP", "logs");

            Assert.NotEqual(real, LogPaths.Directory, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void File_LandsInTheLogDirectory()
        {
            Assert.Equal(Path.Combine(LogPaths.Directory, "sip.log"), LogPaths.File("sip.log"));
        }
    }
}
