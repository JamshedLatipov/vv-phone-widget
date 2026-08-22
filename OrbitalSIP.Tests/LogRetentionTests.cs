using System;
using System.IO;
using System.Linq;
using OrbitalSIP.Services.Logging;
using Xunit;

namespace OrbitalSIP.Tests
{
    /// <summary>
    /// Retention policy for the log directory: which day a file belongs to, and which days
    /// have fallen out of the window. Size-based rotation alone kept whatever the last few
    /// megabytes happened to be, so a quiet week left January on disk and a busy hour threw
    /// away the morning. Dating the files is what makes "the last three days" mean that.
    /// </summary>
    public class LogRetentionTests
    {
        private static readonly DateTime Today = new DateTime(2026, 8, 22);

        [Fact]
        public void DailyPath_DatesTheFile()
        {
            Assert.Equal("app-2026-08-22.log", LogRetention.DailyPath("app.log", Today));
        }

        [Fact]
        public void DailyPath_KeepsTheFileInItsDirectory()
        {
            var dated = LogRetention.DailyPath(Path.Combine("logs", "app.log"), Today);

            Assert.Equal("logs", Path.GetDirectoryName(dated));
            Assert.Equal("app-2026-08-22.log", Path.GetFileName(dated));
        }

        [Fact]
        public void DailyPath_IgnoresTheTimeOfDay()
        {
            Assert.Equal(
                LogRetention.DailyPath("app.log", Today),
                LogRetention.DailyPath("app.log", Today.AddHours(23).AddMinutes(59)));
        }

        [Fact]
        public void SearchPattern_MatchesOnlyTheDatedFilesOfThisLog()
        {
            Assert.Equal("app-*.log", LogRetention.SearchPattern(Path.Combine("logs", "app.log")));
        }

        [Fact]
        public void DateOf_ReadsTheDayBackOut()
        {
            Assert.Equal(Today, LogRetention.DateOf("app.log", "app-2026-08-22.log"));
        }

        [Fact]
        public void DateOf_ReadsTheDayOffAWithinDayGeneration()
        {
            // A busy day still rolls over on size, and app-2026-08-22.1.log belongs to the
            // same day as the live file — it has to expire with it, not outlive it.
            Assert.Equal(Today, LogRetention.DateOf("app.log", "app-2026-08-22.1.log"));
        }

        [Fact]
        public void DateOf_ComparesTheFullPath()
        {
            var dated = Path.Combine("logs", "app-2026-08-22.log");

            Assert.Equal(Today, LogRetention.DateOf(Path.Combine("logs", "app.log"), dated));
        }

        [Fact]
        public void DateOf_RejectsAnotherLogsFile()
        {
            // app and sip share a directory. Neither may ever decide the other's files are old.
            Assert.Null(LogRetention.DateOf("app.log", "sip-2026-08-22.log"));
        }

        [Fact]
        public void DateOf_RejectsAPrefixThatOnlyLooksLikeOurs()
        {
            Assert.Null(LogRetention.DateOf("app.log", "appx-2026-08-22.log"));
        }

        [Fact]
        public void DateOf_RejectsTheUndatedLegacyFile()
        {
            Assert.Null(LogRetention.DateOf("app.log", "app.log"));
            Assert.Null(LogRetention.DateOf("app.log", "app.1.log"));
        }

        [Fact]
        public void DateOf_RejectsSomethingThatIsNotADate()
        {
            Assert.Null(LogRetention.DateOf("app.log", "app-yesterday.log"));
            Assert.Null(LogRetention.DateOf("app.log", "app-2026-13-45.log"));
        }

        [Fact]
        public void Expired_KeepsTodayAndTheTwoDaysBefore()
        {
            var files = new[]
            {
                "app-2026-08-22.log",
                "app-2026-08-21.log",
                "app-2026-08-20.log",
                "app-2026-08-19.log",
            };

            var expired = LogRetention.Expired("app.log", files, Today, keepDays: 3);

            Assert.Equal(new[] { "app-2026-08-19.log" }, expired);
        }

        [Fact]
        public void Expired_TakesTheWithinDayGenerationsWithTheirDay()
        {
            var files = new[] { "app-2026-08-19.log", "app-2026-08-19.1.log", "app-2026-08-22.log" };

            var expired = LogRetention.Expired("app.log", files, Today, keepDays: 3);

            Assert.Equal(new[] { "app-2026-08-19.1.log", "app-2026-08-19.log" }, expired.OrderBy(f => f, StringComparer.Ordinal).ToArray());
        }

        [Fact]
        public void Expired_NeverTouchesAFileItCannotDate()
        {
            // Anything we did not name is somebody else's: crash.log, a hand-saved copy, the
            // other log's files. Deleting on a guess is not worth the disk it frees.
            var files = new[] { "crash.log", "sip-2026-01-01.log", "app.log", "notes.txt" };

            Assert.Empty(LogRetention.Expired("app.log", files, Today, keepDays: 3));
        }

        [Fact]
        public void Expired_IsDisabledWhenTheWindowIsNotPositive()
        {
            var files = new[] { "app-2020-01-01.log" };

            Assert.Empty(LogRetention.Expired("app.log", files, Today, keepDays: 0));
            Assert.Empty(LogRetention.Expired("app.log", files, Today, keepDays: -1));
        }

        [Fact]
        public void Expired_IgnoresTheTimeOfDayOnBothSides()
        {
            var files = new[] { "app-2026-08-20.log" };

            Assert.Empty(LogRetention.Expired("app.log", files, Today.AddHours(23), keepDays: 3));
        }

        [Fact]
        public void AdoptedPath_DatesTheLegacyFileByWhenItWasLastWritten()
        {
            // app.log predates daily naming, so the sweep cannot see it and it would sit on
            // disk for good. Renaming it into the scheme lets it expire on schedule instead.
            Assert.Equal(
                "app-2026-08-10.log",
                LogRetention.AdoptedPath("app.log", "app.log", new DateTime(2026, 8, 10, 6, 35, 0)));
        }

        [Fact]
        public void AdoptedPath_AdoptsTheLegacyGenerationsToo()
        {
            Assert.Equal(
                "app-2026-08-06.log",
                LogRetention.AdoptedPath("app.log", "app.2.log", new DateTime(2026, 8, 6, 15, 47, 0)));
        }

        [Fact]
        public void AdoptedPath_KeepsTheFileInItsDirectory()
        {
            var adopted = LogRetention.AdoptedPath(
                Path.Combine("logs", "app.log"),
                Path.Combine("logs", "app.1.log"),
                new DateTime(2026, 8, 10));

            Assert.Equal("logs", Path.GetDirectoryName(adopted));
            Assert.Equal("app-2026-08-10.log", Path.GetFileName(adopted));
        }

        [Fact]
        public void AdoptedPath_LeavesAnAlreadyDatedFileAlone()
        {
            Assert.Null(LogRetention.AdoptedPath("app.log", "app-2026-08-22.log", Today));
            Assert.Null(LogRetention.AdoptedPath("app.log", "app-2026-08-22.1.log", Today));
        }

        [Fact]
        public void AdoptedPath_LeavesAnotherLogsFileAlone()
        {
            Assert.Null(LogRetention.AdoptedPath("app.log", "sip.log", Today));
            Assert.Null(LogRetention.AdoptedPath("app.log", "crash.log", Today));
            Assert.Null(LogRetention.AdoptedPath("app.log", "appx.log", Today));
        }
    }
}
