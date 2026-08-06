using System.IO;
using System.Linq;
using OrbitalSIP.Services.Logging;
using Xunit;

namespace OrbitalSIP.Tests
{
    public class LogRotationTests
    {
        [Fact]
        public void ShouldRotate_WhenTheAppendWouldExceedTheLimit()
        {
            Assert.True(LogRotation.ShouldRotate(currentSize: 40, incomingBytes: 20, maxBytes: 50));
        }

        [Fact]
        public void ShouldRotate_IsFalseWhenTheAppendStillFits()
        {
            Assert.False(LogRotation.ShouldRotate(currentSize: 20, incomingBytes: 20, maxBytes: 50));
        }

        [Fact]
        public void ShouldRotate_IsFalseAtExactlyTheLimit()
        {
            Assert.False(LogRotation.ShouldRotate(currentSize: 40, incomingBytes: 10, maxBytes: 50));
        }

        [Fact]
        public void ShouldRotate_NeverRotatesAnEmptyFile()
        {
            // Otherwise a single line longer than the limit would rotate forever and
            // never actually get written anywhere.
            Assert.False(LogRotation.ShouldRotate(currentSize: 0, incomingBytes: 5000, maxBytes: 50));
        }

        [Fact]
        public void ShouldRotate_IsDisabledWhenTheLimitIsNotPositive()
        {
            Assert.False(LogRotation.ShouldRotate(currentSize: 9999, incomingBytes: 20, maxBytes: 0));
            Assert.False(LogRotation.ShouldRotate(currentSize: 9999, incomingBytes: 20, maxBytes: -1));
        }

        [Fact]
        public void ArchivePath_NumbersTheGeneration()
        {
            Assert.Equal("app.1.log", LogRotation.ArchivePath("app.log", 1));
            Assert.Equal("app.2.log", LogRotation.ArchivePath("app.log", 2));
        }

        [Fact]
        public void ArchivePath_GenerationZeroIsTheLiveFile()
        {
            Assert.Equal("app.log", LogRotation.ArchivePath("app.log", 0));
        }

        [Fact]
        public void ArchivePath_KeepsTheFileInItsDirectory()
        {
            var live = Path.Combine("logs", "sub", "app.log");

            var archived = LogRotation.ArchivePath(live, 1);

            Assert.Equal(Path.GetDirectoryName(live), Path.GetDirectoryName(archived));
            Assert.Equal("app.1.log", Path.GetFileName(archived));
        }

        [Fact]
        public void ArchivePath_HandlesAFileWithoutAnExtension()
        {
            Assert.Equal("applog.1", LogRotation.ArchivePath("applog", 1));
        }

        [Fact]
        public void RollPlan_MovesTheOldestGenerationFirst()
        {
            // Oldest first, so each rename lands on a name that is free or holds the
            // generation we are happy to discard.
            var plan = LogRotation.RollPlan("app.log", keep: 3).ToList();

            Assert.Equal(3, plan.Count);
            Assert.Equal(("app.2.log", "app.3.log"), plan[0]);
            Assert.Equal(("app.1.log", "app.2.log"), plan[1]);
            Assert.Equal(("app.log", "app.1.log"), plan[2]);
        }

        [Fact]
        public void RollPlan_WithASingleGenerationJustArchivesTheLiveFile()
        {
            var plan = LogRotation.RollPlan("app.log", keep: 1).ToList();

            Assert.Single(plan);
            Assert.Equal(("app.log", "app.1.log"), plan[0]);
        }

        [Fact]
        public void RollPlan_WithNoGenerationsIsEmpty()
        {
            Assert.Empty(LogRotation.RollPlan("app.log", keep: 0));
        }
    }
}
