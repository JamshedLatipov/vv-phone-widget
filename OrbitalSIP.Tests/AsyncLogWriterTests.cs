using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OrbitalSIP.Services.Logging;
using Xunit;

namespace OrbitalSIP.Tests
{
    public class AsyncLogWriterTests : IDisposable
    {
        private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(5);

        private readonly string _dir;

        public AsyncLogWriterTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "OrbitalSIP-logtests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        private string LogPath => Path.Combine(_dir, "app.log");

        [Fact]
        public void Write_AppendsTheLineToTheFile()
        {
            using var writer = new AsyncLogWriter(LogPath);

            writer.Write("hello");
            Assert.True(writer.Flush(FlushTimeout));

            Assert.Equal(new[] { "hello" }, File.ReadAllLines(writer.Path));
        }

        [Fact]
        public void Write_PreservesTheOrderOfLines()
        {
            using var writer = new AsyncLogWriter(LogPath);

            for (int i = 0; i < 100; i++) writer.Write($"line {i}");
            Assert.True(writer.Flush(FlushTimeout));

            var lines = File.ReadAllLines(writer.Path);
            Assert.Equal(100, lines.Length);
            Assert.Equal("line 0", lines[0]);
            Assert.Equal("line 99", lines[99]);
        }

        [Fact]
        public void Write_CreatesTheLogDirectoryWhenItIsMissing()
        {
            var nested = Path.Combine(_dir, "does", "not", "exist", "app.log");
            using var writer = new AsyncLogWriter(nested);

            writer.Write("hello");
            Assert.True(writer.Flush(FlushTimeout));

            Assert.True(File.Exists(writer.Path));
        }

        [Fact]
        public void Dispose_WritesEverythingStillQueued()
        {
            var writer = new AsyncLogWriter(LogPath);
            for (int i = 0; i < 50; i++) writer.Write($"line {i}");

            var live = writer.Path;
            writer.Dispose();

            Assert.Equal(50, File.ReadAllLines(live).Length);
        }

        [Fact]
        public void Write_AfterDisposeIsIgnoredRatherThanThrowing()
        {
            var writer = new AsyncLogWriter(LogPath);
            writer.Dispose();

            var ex = Record.Exception(() => writer.Write("too late"));

            Assert.Null(ex);
        }

        [Fact]
        public void Write_RotatesOnceTheFileOutgrowsTheLimit()
        {
            using var writer = new AsyncLogWriter(LogPath, maxBytes: 200, keep: 2);

            for (int i = 0; i < 50; i++) writer.Write(new string('x', 40));
            Assert.True(writer.Flush(FlushTimeout));

            Assert.True(File.Exists(LogRotation.ArchivePath(writer.Path, 1)), "expected a rotated generation");
            Assert.True(new FileInfo(writer.Path).Length <= 200, "live file should have been rolled over");
        }

        [Fact]
        public void Rotation_KeepsOnlyTheConfiguredNumberOfGenerations()
        {
            using var writer = new AsyncLogWriter(LogPath, maxBytes: 100, keep: 1);

            for (int i = 0; i < 100; i++) writer.Write(new string('x', 40));
            Assert.True(writer.Flush(FlushTimeout));

            Assert.True(File.Exists(LogRotation.ArchivePath(writer.Path, 1)));
            Assert.False(File.Exists(LogRotation.ArchivePath(writer.Path, 2)));
        }

        [Fact]
        public void Rotation_KeepsTheMostRecentLinesInTheLiveFile()
        {
            using var writer = new AsyncLogWriter(LogPath, maxBytes: 200, keep: 2);

            for (int i = 0; i < 50; i++) writer.Write($"line {i}");
            Assert.True(writer.Flush(FlushTimeout));

            Assert.Contains("line 49", File.ReadAllLines(writer.Path));
        }

        [Fact]
        public async Task ConcurrentWriters_LoseNoLines()
        {
            using var writer = new AsyncLogWriter(LogPath, maxBytes: 0, capacity: 100_000);

            var tasks = Enumerable.Range(0, 4).Select(t => Task.Run(() =>
            {
                for (int i = 0; i < 250; i++) writer.Write($"t{t}-{i}");
            })).ToArray();
            await Task.WhenAll(tasks);
            Assert.True(writer.Flush(FlushTimeout));

            var lines = new HashSet<string>(File.ReadAllLines(writer.Path));
            Assert.Equal(0, writer.DroppedCount);
            Assert.Equal(1000, lines.Count);
        }

        [Fact]
        public void Write_ReportsNoDropsUnderNormalUse()
        {
            using var writer = new AsyncLogWriter(LogPath);

            for (int i = 0; i < 500; i++) writer.Write($"line {i}");
            Assert.True(writer.Flush(FlushTimeout));

            Assert.Equal(0, writer.DroppedCount);
        }

        [Fact]
        public void Write_DropsRatherThanBlockingWhenTheQueueIsSaturated()
        {
            // Capacity of one against a burst the writer thread cannot possibly keep up
            // with: the contract is that the caller returns immediately and the overflow
            // is counted, never that the caller waits for the disk.
            using var writer = new AsyncLogWriter(LogPath, maxBytes: 0, capacity: 1);

            for (int i = 0; i < 20_000; i++) writer.Write($"line {i}");
            writer.Flush(FlushTimeout);

            Assert.True(writer.DroppedCount > 0, "expected the saturated queue to drop lines");
        }

        // ── Daily files and the retention window ──────────────────────

        private string Dated(DateTime day) => LogRetention.DailyPath(LogPath, day);

        [Fact]
        public void Write_GoesIntoTodaysDatedFile()
        {
            using var writer = new AsyncLogWriter(LogPath);

            writer.Write("hello");
            Assert.True(writer.Flush(FlushTimeout));

            Assert.Equal(Dated(DateTime.Now), writer.Path);
            Assert.False(File.Exists(LogPath), "the undated name must never be written to");
        }

        [Fact]
        public void Retention_DeletesTheDaysThatFellOutOfTheWindow()
        {
            var stale = Dated(DateTime.Now.AddDays(-9));
            var kept = Dated(DateTime.Now.AddDays(-1));
            File.WriteAllText(stale, "old");
            File.WriteAllText(kept, "recent");

            using var writer = new AsyncLogWriter(LogPath, keepDays: 3);

            Assert.False(File.Exists(stale), "a nine-day-old file is outside a three-day window");
            Assert.True(File.Exists(kept), "yesterday is inside a three-day window");
        }

        [Fact]
        public void Retention_TakesTheWithinDayGenerationsWithTheirDay()
        {
            var stale = LogRotation.ArchivePath(Dated(DateTime.Now.AddDays(-9)), 1);
            File.WriteAllText(stale, "old overflow");

            using var writer = new AsyncLogWriter(LogPath, keepDays: 3);

            Assert.False(File.Exists(stale));
        }

        [Fact]
        public void Retention_LeavesFilesItDidNotWriteAlone()
        {
            // crash.log is appended to directly by the crash handler, and sip-*.log belongs to
            // the other writer. Neither is ours to date, so neither is ours to delete.
            var crash = Path.Combine(_dir, "crash.log");
            var otherLog = LogRetention.DailyPath(Path.Combine(_dir, "sip.log"), DateTime.Now.AddDays(-9));
            File.WriteAllText(crash, "stack trace");
            File.WriteAllText(otherLog, "the other log");

            using var writer = new AsyncLogWriter(LogPath, keepDays: 3);

            Assert.True(File.Exists(crash));
            Assert.True(File.Exists(otherLog));
        }

        [Fact]
        public void Retention_IsDisabledByANonPositiveWindow()
        {
            var ancient = Dated(new DateTime(2020, 1, 1));
            File.WriteAllText(ancient, "ancient");

            using var writer = new AsyncLogWriter(LogPath, keepDays: 0);

            Assert.True(File.Exists(ancient));
        }

        [Fact]
        public void Retention_AdoptsTheUndatedFilesLeftBySizeOnlyRotation()
        {
            // The several hundred megabytes already on disk carry no date, so no window can see
            // them. They get renamed into the scheme and expire from there — the nine-day-old
            // one immediately, the fresh one only once it ages out.
            var legacyStale = Path.Combine(_dir, "app.1.log");
            var legacyFresh = Path.Combine(_dir, "app.log");
            File.WriteAllText(legacyStale, "old");
            File.WriteAllText(legacyFresh, "recent");
            File.SetLastWriteTime(legacyStale, DateTime.Now.AddDays(-9));
            File.SetLastWriteTime(legacyFresh, DateTime.Now.AddDays(-1));

            using var writer = new AsyncLogWriter(LogPath, keepDays: 3);

            Assert.False(File.Exists(legacyStale), "the legacy generation should have been adopted, then swept");
            Assert.False(File.Exists(legacyFresh), "the legacy live file should have been adopted");
            Assert.Equal("recent", File.ReadAllText(Dated(DateTime.Now.AddDays(-1))).Trim());
        }

        [Fact]
        public void Retention_AdoptionDoesNotOverwriteADayThatAlreadyExists()
        {
            var yesterday = DateTime.Now.AddDays(-1);
            File.WriteAllText(Dated(yesterday), "already dated");
            var legacy = Path.Combine(_dir, "app.log");
            File.WriteAllText(legacy, "legacy");
            File.SetLastWriteTime(legacy, yesterday);

            using var writer = new AsyncLogWriter(LogPath, keepDays: 3);

            Assert.Equal("already dated", File.ReadAllText(Dated(yesterday)).Trim());
            Assert.Equal("legacy", File.ReadAllText(LogRotation.ArchivePath(Dated(yesterday), 1)).Trim());
        }
    }
}
