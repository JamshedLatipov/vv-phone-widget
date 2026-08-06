using System;
using System.Runtime.InteropServices;

namespace OrbitalSIP.Services.Audio
{
    /// <summary>
    /// Allocation-free conversions between raw little-endian 16-bit PCM bytes and
    /// <see cref="short"/> samples, plus the scratch-buffer sizing helpers the audio
    /// end point uses to keep its per-packet work off the heap.
    ///
    /// The conversions are a raw reinterpret of the underlying memory, which matches
    /// <see cref="BitConverter"/> byte-for-byte on a little-endian machine. The app
    /// targets net8.0-windows, so that is always the case here.
    /// </summary>
    public static class PcmBuffer
    {
        /// <summary>Whole 16-bit samples contained in <paramref name="byteCount"/> bytes; a dangling odd byte is dropped.</summary>
        public static int SampleCount(int byteCount) => byteCount / 2;

        /// <summary>Reads little-endian 16-bit PCM into <paramref name="samples"/>. Writes <c>SampleCount(pcmBytes.Length)</c> entries and leaves any trailing slack alone.</summary>
        public static void ToSamples(ReadOnlySpan<byte> pcmBytes, Span<short> samples)
        {
            var source = MemoryMarshal.Cast<byte, short>(pcmBytes);   // truncates a dangling odd byte
            source.CopyTo(samples.Slice(0, source.Length));
        }

        /// <summary>Writes <paramref name="samples"/> back out as little-endian 16-bit PCM, leaving any trailing slack alone.</summary>
        public static void ToBytes(ReadOnlySpan<short> samples, Span<byte> pcmBytes)
        {
            var source = MemoryMarshal.AsBytes(samples);
            source.CopyTo(pcmBytes.Slice(0, source.Length));
        }

        /// <summary>Reallocates only when the length differs — for consumers that read the whole array.</summary>
        public static void EnsureExact<T>(ref T[] buffer, int length)
        {
            if (buffer.Length != length) buffer = new T[length];
        }

        /// <summary>Reallocates only when the buffer is too small — for consumers that take an explicit count.</summary>
        public static void EnsureAtLeast<T>(ref T[] buffer, int length)
        {
            if (buffer.Length < length) buffer = new T[length];
        }
    }
}
