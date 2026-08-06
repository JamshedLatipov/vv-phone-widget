using System;
using System.Linq;
using OrbitalSIP.Services.Audio;
using Xunit;

namespace OrbitalSIP.Tests
{
    public class PcmBufferTests
    {
        /// <summary>Deterministic pseudo-PCM so the pinning tests below are reproducible.</summary>
        private static byte[] SampleBytes(int count)
        {
            var rng = new Random(1234);
            var bytes = new byte[count];
            rng.NextBytes(bytes);
            return bytes;
        }

        [Fact]
        public void SampleCount_IsHalfTheByteCount()
        {
            Assert.Equal(160, PcmBuffer.SampleCount(320));
        }

        [Fact]
        public void SampleCount_DropsDanglingOddByte()
        {
            // A half sample is not a sample — the old LINQ path read one byte past
            // the recorded length here instead of discarding it.
            Assert.Equal(2, PcmBuffer.SampleCount(5));
        }

        [Fact]
        public void SampleCount_OfEmptyIsZero()
        {
            Assert.Equal(0, PcmBuffer.SampleCount(0));
        }

        [Fact]
        public void ToSamples_ReadsBytePairsLittleEndian()
        {
            byte[] pcmBytes = { 0x01, 0x02, 0x03, 0x04 };
            var samples = new short[2];

            PcmBuffer.ToSamples(pcmBytes, samples);

            Assert.Equal(new short[] { 0x0201, 0x0403 }, samples);
        }

        [Fact]
        public void ToSamples_MatchesBitConverter()
        {
            // Pins the replacement against the byte order the previous
            // BitConverter.ToInt16 path produced.
            var pcmBytes = SampleBytes(320);
            var expected = Enumerable.Range(0, 160)
                                     .Select(i => BitConverter.ToInt16(pcmBytes, i * 2))
                                     .ToArray();
            var samples = new short[160];

            PcmBuffer.ToSamples(pcmBytes, samples);

            Assert.Equal(expected, samples);
        }

        [Fact]
        public void ToSamples_PreservesNegativeSamples()
        {
            byte[] pcmBytes = { 0xFF, 0xFF, 0x00, 0x80 };
            var samples = new short[2];

            PcmBuffer.ToSamples(pcmBytes, samples);

            Assert.Equal(new short[] { -1, short.MinValue }, samples);
        }

        [Fact]
        public void ToSamples_IgnoresDanglingOddByte()
        {
            byte[] pcmBytes = { 0x01, 0x02, 0x03 };
            var samples = new short[] { 0, 4242 };

            PcmBuffer.ToSamples(pcmBytes, samples);

            Assert.Equal(0x0201, samples[0]);
            Assert.Equal(4242, samples[1]);   // untouched — no half sample was written
        }

        [Fact]
        public void ToSamples_LeavesTrailingSlackUntouched()
        {
            // The end point hands over a scratch buffer that may be longer than the packet.
            byte[] pcmBytes = { 0x01, 0x02 };
            var samples = new short[] { 0, 999, 999 };

            PcmBuffer.ToSamples(pcmBytes, samples);

            Assert.Equal(new short[] { 0x0201, 999, 999 }, samples);
        }

        [Fact]
        public void ToBytes_MatchesBitConverterGetBytes()
        {
            short[] samples = { 0, 1, -1, short.MaxValue, short.MinValue, 12345 };
            var expected = samples.SelectMany(BitConverter.GetBytes).ToArray();
            var pcmBytes = new byte[samples.Length * 2];

            PcmBuffer.ToBytes(samples, pcmBytes);

            Assert.Equal(expected, pcmBytes);
        }

        [Fact]
        public void ToBytes_LeavesTrailingSlackUntouched()
        {
            short[] samples = { 0x0201 };
            var pcmBytes = new byte[] { 0, 0, 0xEE, 0xEE };

            PcmBuffer.ToBytes(samples, pcmBytes);

            Assert.Equal(new byte[] { 0x01, 0x02, 0xEE, 0xEE }, pcmBytes);
        }

        [Fact]
        public void RoundTrip_ReturnsTheOriginalBytes()
        {
            var pcmBytes = SampleBytes(320);
            var samples = new short[160];
            var roundTripped = new byte[320];

            PcmBuffer.ToSamples(pcmBytes, samples);
            PcmBuffer.ToBytes(samples, roundTripped);

            Assert.Equal(pcmBytes, roundTripped);
        }

        [Fact]
        public void EnsureExact_AllocatesWhenTheBufferIsEmpty()
        {
            var buffer = Array.Empty<short>();

            PcmBuffer.EnsureExact(ref buffer, 160);

            Assert.Equal(160, buffer.Length);
        }

        [Fact]
        public void EnsureExact_ReusesTheBufferWhenTheLengthAlreadyMatches()
        {
            var buffer = new short[160];
            var original = buffer;

            PcmBuffer.EnsureExact(ref buffer, 160);

            Assert.Same(original, buffer);
        }

        [Fact]
        public void EnsureExact_ShrinksAnOversizedBuffer()
        {
            // The encoder reads the whole array, so a longer buffer would append garbage.
            var buffer = new short[320];

            PcmBuffer.EnsureExact(ref buffer, 160);

            Assert.Equal(160, buffer.Length);
        }

        [Fact]
        public void EnsureAtLeast_GrowsWhenTheBufferIsTooSmall()
        {
            var buffer = new byte[64];

            PcmBuffer.EnsureAtLeast(ref buffer, 320);

            Assert.True(buffer.Length >= 320);
        }

        [Fact]
        public void EnsureAtLeast_KeepsAnOversizedBuffer()
        {
            var buffer = new byte[640];
            var original = buffer;

            PcmBuffer.EnsureAtLeast(ref buffer, 320);

            Assert.Same(original, buffer);
        }
    }
}
