using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace OrbitalSIP.Services.Logging
{
    /// <summary>
    /// Hand-off buffer between logging callers and the writer thread. Bounded on purpose:
    /// a caller that is on the UI, audio or SIP thread must never wait on the disk, so an
    /// overflowing queue drops the line and counts it rather than applying backpressure.
    /// </summary>
    public sealed class BoundedLogQueue
    {
        private readonly ConcurrentQueue<string> _lines = new();
        private readonly int _capacity;
        private int _count;
        private long _dropped;

        public BoundedLogQueue(int capacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity), "The queue needs room for at least one line.");
            _capacity = capacity;
        }

        /// <summary>Lines currently waiting to be written.</summary>
        public int Count => Volatile.Read(ref _count);

        /// <summary>Lines discarded because the queue was full.</summary>
        public long DroppedCount => Interlocked.Read(ref _dropped);

        /// <summary>Queues a line. Returns false — and counts a drop — when the queue is full.</summary>
        public bool TryEnqueue(string line)
        {
            // Claim the slot before enqueuing so concurrent callers cannot overshoot the cap.
            if (Interlocked.Increment(ref _count) > _capacity)
            {
                Interlocked.Decrement(ref _count);
                Interlocked.Increment(ref _dropped);
                return false;
            }

            _lines.Enqueue(line);
            return true;
        }

        /// <summary>Takes the oldest queued line.</summary>
        public bool TryDequeue([MaybeNullWhen(false)] out string line)
        {
            if (!_lines.TryDequeue(out line)) return false;

            Interlocked.Decrement(ref _count);
            return true;
        }
    }
}
