using OrbitalSIP.Services.Logging;
using Xunit;

namespace OrbitalSIP.Tests
{
    public class BoundedLogQueueTests
    {
        [Fact]
        public void ANewQueueIsEmptyAndHasDroppedNothing()
        {
            var queue = new BoundedLogQueue(4);

            Assert.Equal(0, queue.Count);
            Assert.Equal(0, queue.DroppedCount);
        }

        [Fact]
        public void TryEnqueue_AcceptsLinesUpToCapacity()
        {
            var queue = new BoundedLogQueue(3);

            Assert.True(queue.TryEnqueue("a"));
            Assert.True(queue.TryEnqueue("b"));
            Assert.True(queue.TryEnqueue("c"));
            Assert.Equal(3, queue.Count);
        }

        [Fact]
        public void TryEnqueue_RejectsTheLineWhenTheQueueIsFull()
        {
            var queue = new BoundedLogQueue(2);
            queue.TryEnqueue("a");
            queue.TryEnqueue("b");

            Assert.False(queue.TryEnqueue("c"));
            Assert.Equal(2, queue.Count);
        }

        [Fact]
        public void TryEnqueue_CountsEveryDroppedLine()
        {
            var queue = new BoundedLogQueue(1);
            queue.TryEnqueue("kept");

            queue.TryEnqueue("dropped 1");
            queue.TryEnqueue("dropped 2");

            Assert.Equal(2, queue.DroppedCount);
        }

        [Fact]
        public void TryDequeue_ReturnsLinesInTheOrderTheyWereWritten()
        {
            var queue = new BoundedLogQueue(4);
            queue.TryEnqueue("first");
            queue.TryEnqueue("second");

            Assert.True(queue.TryDequeue(out var a));
            Assert.True(queue.TryDequeue(out var b));
            Assert.Equal("first", a);
            Assert.Equal("second", b);
        }

        [Fact]
        public void TryDequeue_OnAnEmptyQueueReturnsFalse()
        {
            var queue = new BoundedLogQueue(4);

            Assert.False(queue.TryDequeue(out _));
        }

        [Fact]
        public void TryDequeue_FreesRoomForNewLines()
        {
            var queue = new BoundedLogQueue(1);
            queue.TryEnqueue("a");
            Assert.False(queue.TryEnqueue("b"));

            queue.TryDequeue(out _);

            Assert.True(queue.TryEnqueue("b"));
        }
    }
}
