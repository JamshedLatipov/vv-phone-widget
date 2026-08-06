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

            Assert.Equal(new[] { "hello" }, File.ReadAllLines(LogPath));
        }

        [Fact]
        public void Write_PreservesTheOrderOfLines()
        {
            using var writer = new AsyncLogWriter(LogPath);

            for (int i = 0; i < 100; i++) writer.Write($"line {i}");
            Assert.True(writer.Flush(FlushTimeout));

            var lines = File.ReadAllLines(LogPath);
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

            Assert.True(File.Exists(nested));
        }

        [Fact]
        public void Dispose_WritesEverythingStillQueued()
        {
            var writer = new AsyncLogWriter(LogPath);
            for (int i = 0; i < 50; i++) writer.Write($"line {i}");

            writer.Dispose();

            Assert.Equal(50, File.ReadAllLines(LogPath).Length);
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

            Assert.True(File.Exists(LogRotation.ArchivePath(LogPath, 1)), "expected a rotated generation");
            Assert.True(new FileInfo(LogPath).Length <= 200, "live file should have been rolled over");
        }

        [Fact]
        public void Rotation_KeepsOnlyTheConfiguredNumberOfGenerations()
        {
            using var writer = new AsyncLogWriter(LogPath, maxBytes: 100, keep: 1);

            for (int i = 0; i < 100; i++) writer.Write(new string('x', 40));
            Assert.True(writer.Flush(FlushTimeout));

            Assert.True(File.Exists(LogRotation.ArchivePath(LogPath, 1)));
            Assert.False(File.Exists(LogRotation.ArchivePath(LogPath, 2)));
        }

        [Fact]
        public void Rotation_KeepsTheMostRecentLinesInTheLiveFile()
        {
            using var writer = new AsyncLogWriter(LogPath, maxBytes: 200, keep: 2);

            for (int i = 0; i < 50; i++) writer.Write($"line {i}");
            Assert.True(writer.Flush(FlushTimeout));

            Assert.Contains("line 49", File.ReadAllLines(LogPath));
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

            var lines = new HashSet<string>(File.ReadAllLines(LogPath));
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
    }
}
