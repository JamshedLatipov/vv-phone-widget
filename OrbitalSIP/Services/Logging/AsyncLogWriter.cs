using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace OrbitalSIP.Services.Logging
{
    /// <summary>
    /// Appends log lines from a single background thread, so callers on the UI, audio and
    /// SIP threads hand off a string and return instead of opening, writing and closing a
    /// file under a global lock. Rotates the file once it outgrows <c>maxBytes</c>.
    /// </summary>
    public sealed class AsyncLogWriter : IDisposable
    {
        public const long DefaultMaxBytes = 4 * 1024 * 1024;
        public const int DefaultKeep = 2;
        public const int DefaultCapacity = 8192;

        /// <summary>Longest the writer thread parks before it re-checks the queue, so <see cref="Flush"/> never depends on a wake-up signal arriving.</summary>
        private const int PollIntervalMs = 50;

        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private readonly BoundedLogQueue _queue;
        private readonly long _maxBytes;
        private readonly int _keep;
        private readonly Thread _thread;
        private readonly SemaphoreSlim _pending = new(0);
        private readonly ManualResetEventSlim _idle = new(true);
        private readonly CancellationTokenSource _shutdown = new();

        private readonly int _newLineBytes = Utf8NoBom.GetByteCount(Environment.NewLine);
        private long _size;
        private volatile bool _disposed;

        public AsyncLogWriter(string path,
                              long maxBytes = DefaultMaxBytes,
                              int keep = DefaultKeep,
                              int capacity = DefaultCapacity)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
            _maxBytes = maxBytes;
            _keep = keep;
            _queue = new BoundedLogQueue(capacity);
            _size = FileLength(path);

            _thread = new Thread(DrainLoop)
            {
                IsBackground = true,
                Name = "OrbitalSIP-Log",
            };
            _thread.Start();
        }

        /// <summary>Path of the live log file.</summary>
        public string Path { get; }

        /// <summary>Lines discarded because the queue was full.</summary>
        public long DroppedCount => _queue.DroppedCount;

        /// <summary>Queues one line. Never blocks on the disk and never throws.</summary>
        public void Write(string line)
        {
            if (_disposed || line == null) return;

            if (_queue.TryEnqueue(line))
            {
                _idle.Reset();
                try { _pending.Release(); } catch (SemaphoreFullException) { /* the drain loop is already awake */ }
            }
        }

        /// <summary>Waits until the queue is drained to disk. Returns false on timeout.</summary>
        public bool Flush(TimeSpan timeout)
        {
            var deadline = Environment.TickCount64 + (long)Math.Max(0, timeout.TotalMilliseconds);

            while (true)
            {
                if (_queue.Count == 0 && _idle.IsSet) return true;
                if (Environment.TickCount64 >= deadline) return false;
                Thread.Sleep(1);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _shutdown.Cancel();
            try { _pending.Release(); } catch (SemaphoreFullException) { }
            _thread.Join(TimeSpan.FromSeconds(5));

            DrainOnce();   // in case the thread died on us, the queued lines still land on disk

            _shutdown.Dispose();
            _pending.Dispose();
            _idle.Dispose();
        }

        // ── Writer thread ─────────────────────────────────────────────

        private void DrainLoop()
        {
            while (!_shutdown.IsCancellationRequested)
            {
                try { _pending.Wait(PollIntervalMs, _shutdown.Token); }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }

                DrainOnce();
            }

            DrainOnce();   // final pass so shutdown does not lose the tail
        }

        private void DrainOnce()
        {
            if (_queue.Count == 0)
            {
                SetIdle();
                return;
            }

            try
            {
                var batch = new List<string>();
                while (_queue.TryDequeue(out var line)) batch.Add(line);
                WriteBatch(batch);
            }
            catch
            {
                // Logging is never worth taking the app down for. The lines are already
                // gone from the queue, so a failed write is dropped rather than retried
                // forever against a disk that is full or locked.
            }

            if (_queue.Count == 0) SetIdle();
        }

        private void SetIdle()
        {
            try { _idle.Set(); } catch (ObjectDisposedException) { /* shutting down */ }
        }

        private void WriteBatch(List<string> batch)
        {
            EnsureDirectory();

            int index = 0;
            while (index < batch.Count)
            {
                using (var stream = new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.Read))
                using (var writer = new StreamWriter(stream, Utf8NoBom))
                {
                    for (; index < batch.Count; index++)
                    {
                        int bytes = Utf8NoBom.GetByteCount(batch[index]) + _newLineBytes;
                        if (LogRotation.ShouldRotate(_size, bytes, _maxBytes)) break;

                        writer.WriteLine(batch[index]);
                        _size += bytes;
                    }
                }

                // The stream is closed here, so the live file is free to be renamed.
                // Rotating resets the size to zero, and an empty file never rotates,
                // so the loop always makes progress.
                if (index < batch.Count) Rotate();
            }
        }

        private void Rotate()
        {
            foreach (var (from, to) in LogRotation.RollPlan(Path, _keep))
            {
                try
                {
                    if (!File.Exists(from)) continue;
                    if (File.Exists(to)) File.Delete(to);
                    File.Move(from, to);
                }
                catch { /* a locked archive just means one lost generation */ }
            }

            if (_keep <= 0)
            {
                try { File.Delete(Path); } catch { /* nothing else to try */ }
            }

            _size = FileLength(Path);
        }

        private void EnsureDirectory()
        {
            var directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        }

        private static long FileLength(string path)
        {
            try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
            catch { return 0; }
        }
    }
}
