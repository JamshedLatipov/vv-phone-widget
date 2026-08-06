using System;
using NAudio.Wave;
using OrbitalSIP.Services.Audio;
using Xunit;

namespace OrbitalSIP.Tests
{
    public class JitterTrimTests
    {
        /// <summary>Narrowband SIP audio: 8 kHz, 16-bit, mono → 16000 bytes/s, 2 bytes per frame.</summary>
        private static readonly WaveFormat Narrowband = new WaveFormat(8000, 16, 1);

        /// <summary>G.722 wideband: 16 kHz, 16-bit, mono → 32000 bytes/s.</summary>
        private static readonly WaveFormat Wideband = new WaveFormat(16000, 16, 1);

        private static readonly TimeSpan Target = TimeSpan.FromMilliseconds(80);
        private static readonly TimeSpan Trigger = TimeSpan.FromMilliseconds(200);

        private static int Bytes(WaveFormat format, int milliseconds) =>
            (int)(format.AverageBytesPerSecond * (milliseconds / 1000.0));

        [Fact]
        public void NothingIsTrimmedWhenTheBacklogIsBelowTheTrigger()
        {
            int buffered = Bytes(Narrowband, 100);

            Assert.Equal(0, JitterTrim.ExcessBytes(buffered, Narrowband, Target, Trigger));
        }

        [Fact]
        public void NothingIsTrimmedAtExactlyTheTrigger()
        {
            int buffered = Bytes(Narrowband, 200);

            Assert.Equal(0, JitterTrim.ExcessBytes(buffered, Narrowband, Target, Trigger));
        }

        [Fact]
        public void NothingIsTrimmedWhenTheBufferIsEmpty()
        {
            Assert.Equal(0, JitterTrim.ExcessBytes(0, Narrowband, Target, Trigger));
        }

        [Fact]
        public void BacklogOverTheTriggerIsTrimmedDownToTheTarget()
        {
            int buffered = Bytes(Narrowband, 2000);          // the 2 second complaint

            int excess = JitterTrim.ExcessBytes(buffered, Narrowband, Target, Trigger);

            Assert.Equal(Bytes(Narrowband, 2000) - Bytes(Narrowband, 80), excess);
        }

        [Fact]
        public void TrimmingLeavesTheTargetCushionRatherThanEmptyingTheBuffer()
        {
            // Emptying it completely would remove the jitter cushion and trade constant
            // latency for constant underruns.
            int buffered = Bytes(Narrowband, 2000);

            int remaining = buffered - JitterTrim.ExcessBytes(buffered, Narrowband, Target, Trigger);

            Assert.Equal(Bytes(Narrowband, 80), remaining);
        }

        [Fact]
        public void TheTrimIsAlwaysAWholeNumberOfSampleFrames()
        {
            // An odd byte count would shift every subsequent sample by one byte.
            for (int extra = 0; extra < 8; extra++)
            {
                int buffered = Bytes(Narrowband, 2000) + extra;

                int excess = JitterTrim.ExcessBytes(buffered, Narrowband, Target, Trigger);

                Assert.Equal(0, excess % Narrowband.BlockAlign);
            }
        }

        [Fact]
        public void TheTrimNeverExceedsWhatIsActuallyBuffered()
        {
            int buffered = Bytes(Narrowband, 5000);

            int excess = JitterTrim.ExcessBytes(buffered, Narrowband, Target, Trigger);

            Assert.InRange(excess, 0, buffered);
        }

        [Fact]
        public void ThePolicyScalesWithTheSampleRate()
        {
            // Same durations, twice the bytes per second, so twice the trim.
            int narrow = JitterTrim.ExcessBytes(Bytes(Narrowband, 2000), Narrowband, Target, Trigger);
            int wide = JitterTrim.ExcessBytes(Bytes(Wideband, 2000), Wideband, Target, Trigger);

            Assert.Equal(narrow * 2, wide);
        }

        [Fact]
        public void NothingIsTrimmedWhenTheTargetIsNotBelowTheTrigger()
        {
            // A misconfigured policy must do nothing rather than trim to a nonsense level.
            int buffered = Bytes(Narrowband, 2000);

            Assert.Equal(0, JitterTrim.ExcessBytes(buffered, Narrowband, Trigger, Trigger));
            Assert.Equal(0, JitterTrim.ExcessBytes(buffered, Narrowband, TimeSpan.FromMilliseconds(500), Trigger));
        }

        [Fact]
        public void NothingIsTrimmedWithoutAUsableFormat()
        {
            Assert.Equal(0, JitterTrim.ExcessBytes(Bytes(Narrowband, 2000), null!, Target, Trigger));
        }

        [Fact]
        public void NothingIsTrimmedForANegativeBacklog()
        {
            Assert.Equal(0, JitterTrim.ExcessBytes(-1, Narrowband, Target, Trigger));
        }
    }
}
