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
    /// file under a global lock.
    ///
    /// One file per day (<c>app-2026-08-22.log</c>), kept for <c>keepDays</c> days.
    /// <c>maxBytes</c> still bounds a single day, because one busy shift of RTP counters can
    /// outgrow anything worth opening in an editor; those overflow generations belong to their
    /// day and expire with it. Size alone used to be the whole policy, which is why the log
    /// directory held August 6th and nothing from the week in between.
    /// </summary>
    public sealed class AsyncLogWriter : IDisposable
    {
        public const long DefaultMaxBytes = 4 * 1024 * 1024;
        public const int DefaultKeep = 2;
        public const int DefaultCapacity = 8192;

        /// <summary>Longest the writer thread parks before it re-checks the queue, so <see cref="Flush"/> never depends on a wake-up signal arriving.</summary>
        private const int PollIntervalMs = 50;

        /// <summary>Free names tried when an adopted legacy file collides with a day that already exists.</summary>
        private const int MaxAdoptionGenerations = 64;

        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private readonly BoundedLogQueue _queue;
        private readonly long _maxBytes;
        private readonly int _keep;
        private readonly int _keepDays;
        private readonly Thread _thread;
        private readonly SemaphoreSlim _pending = new(0);
        private readonly ManualResetEventSlim _idle = new(true);
        private readonly CancellationTokenSource _shutdown = new();

        private readonly int _newLineBytes = Utf8NoBom.GetByteCount(Environment.NewLine);
        private long _size;
        private DateTime _day;
        private volatile bool _disposed;

        public AsyncLogWriter(string path,
                              long maxBytes = DefaultMaxBytes,
                              int keep = DefaultKeep,
                              int capacity = DefaultCapacity,
                              int keepDays = LogRetention.DefaultKeepDays)
        {
            BasePath = path ?? throw new ArgumentNullException(nameof(path));
            _maxBytes = maxBytes;
            _keep = keep;
            _keepDays = keepDays;
            _queue = new BoundedLogQueue(capacity);

            // Before the first path is resolved, so a legacy app.log does not sit next to the
            // dated one it should have become.
            AdoptLegacyFiles();

            _day = DateTime.Now.Date;
            Path = LogRetention.DailyPath(BasePath, _day);
            _size = FileLength(Path);
            Sweep();

            _thread = new Thread(DrainLoop)
            {
                IsBackground = true,
                Name = "OrbitalSIP-Log",
            };
            _thread.Start();
        }

        /// <summary>Undated name the writer was configured with, e.g. <c>…\logs\app.log</c>. Never written to.</summary>
        public string BasePath { get; }

        /// <summary>
        /// Path of the live log file — today's dated file. Reassigned by the writer thread when
        /// the date turns over, so a reader gets either the old day or the new one, never a torn
        /// value: reference assignment is atomic.
        /// </summary>
        public string Path { get; private set; }

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
            RollDayIfNeeded();

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

        /// <summary>
        /// Moves to the new day's file when the clock rolls past midnight, and sweeps while it
        /// is there. A widget left running for a shift used to keep appending to the file it
        /// opened on startup, so "one file per day" only held for a process that restarted daily.
        /// Writer thread only, so <see cref="_day"/> and <see cref="_size"/> need no lock.
        /// </summary>
        private void RollDayIfNeeded()
        {
            var today = DateTime.Now.Date;
            if (today == _day) return;

            _day = today;
            Path = LogRetention.DailyPath(BasePath, today);
            _size = FileLength(Path);
            Sweep();
        }

        /// <summary>
        /// Deletes the days that have fallen out of the window. Never throws and never touches
        /// the live file: logging is not worth losing a line over, let alone the app.
        /// </summary>
        private void Sweep()
        {
            if (_keepDays <= 0) return;

            try
            {
                var directory = LogDirectory();
                if (directory == null) return;

                var dated = Directory.GetFiles(directory, LogRetention.SearchPattern(BasePath));
                foreach (var expired in LogRetention.Expired(BasePath, dated, DateTime.Now, _keepDays))
                {
                    if (string.Equals(expired, Path, StringComparison.OrdinalIgnoreCase)) continue;
                    try { File.Delete(expired); } catch { /* locked by a reader; next sweep gets it */ }
                }
            }
            catch { /* an unreadable log directory is not worth failing a write over */ }
        }

        /// <summary>
        /// Renames the undated files written before daily naming to the day they were last
        /// touched, so <see cref="Sweep"/> can see them at all. Without this the ones already on
        /// disk — the bulk of what the directory holds — would never expire under any policy.
        /// </summary>
        private void AdoptLegacyFiles()
        {
            if (_keepDays <= 0) return;

            try
            {
                var directory = LogDirectory();
                if (directory == null) return;

                var name = System.IO.Path.GetFileNameWithoutExtension(BasePath);
                var extension = System.IO.Path.GetExtension(BasePath);

                foreach (var legacy in Directory.GetFiles(directory, name + "*" + extension))
                {
                    string? adopted;
                    try { adopted = LogRetention.AdoptedPath(BasePath, legacy, File.GetLastWriteTime(legacy)); }
                    catch { continue; }
                    if (adopted == null) continue;

                    // Several legacy generations can share a last-write day, and the day may
                    // already have a file. Land on the first free within-day generation rather
                    // than overwriting history that is still inside the window.
                    for (int generation = 0; generation < MaxAdoptionGenerations; generation++)
                    {
                        var target = LogRotation.ArchivePath(adopted, generation);
                        if (File.Exists(target)) continue;

                        try { File.Move(legacy, target); }
                        catch { /* locked, or another instance got there first */ }
                        break;
                    }
                }
            }
            catch { /* adoption is housekeeping; never let it stop the log from opening */ }
        }

        private string? LogDirectory()
        {
            var directory = System.IO.Path.GetDirectoryName(BasePath);
            if (string.IsNullOrEmpty(directory)) directory = ".";

            return Directory.Exists(directory) ? directory : null;
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
