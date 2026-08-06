using System;
using NAudio.Wave;

namespace OrbitalSIP.Services.Audio
{
    /// <summary>
    /// Decides how much of the oldest buffered audio to drop to keep playback latency bounded.
    ///
    /// The render path has no natural restoring force: WaveOut consumes in real time and RTP
    /// arrives in real time, so the backlog between them never shrinks on its own. A stall at
    /// call start, a GC pause, or a sample-clock difference between the two ends all turn into
    /// latency that lasts the rest of the call. Trimming is what gives that backlog a ceiling.
    /// </summary>
    public static class JitterTrim
    {
        /// <summary>
        /// Bytes of backlog to discard. Zero means leave it alone: the backlog is still under
        /// <paramref name="trigger"/>, or the arguments do not describe a usable policy.
        /// The result is always a whole number of sample frames — dropping a partial frame
        /// would shift every following sample by a byte and turn the stream into noise.
        /// </summary>
        public static int ExcessBytes(int bufferedBytes, WaveFormat format, TimeSpan target, TimeSpan trigger)
        {
            if (bufferedBytes <= 0) return 0;
            if (format == null || format.AverageBytesPerSecond <= 0) return 0;

            // A target that is not below the trigger describes no reachable steady state.
            // Doing nothing is better than trimming to a nonsense level.
            if (trigger <= TimeSpan.Zero || target < TimeSpan.Zero || target >= trigger) return 0;

            long triggerBytes = (long)(trigger.TotalSeconds * format.AverageBytesPerSecond);
            if (bufferedBytes <= triggerBytes) return 0;

            long targetBytes = (long)(target.TotalSeconds * format.AverageBytesPerSecond);
            long excess = bufferedBytes - targetBytes;
            if (excess <= 0) return 0;

            int frame = Math.Max(1, format.BlockAlign);
            excess -= excess % frame;

            return (int)Math.Min(excess, bufferedBytes - (bufferedBytes % frame));
        }
    }
}
