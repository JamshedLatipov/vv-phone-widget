using OrbitalSIP.Services.Audio;
using Xunit;

namespace OrbitalSIP.Tests
{
    public class AudioGainTests
    {
        [Fact]
        public void Unity_LeavesSamplesUnchanged()
        {
            short[] pcm = { 0, 100, -200, 32767, -32768 };
            short[] expected = (short[])pcm.Clone();
            AudioGain.Apply(pcm, 1.0f);
            Assert.Equal(expected, pcm);
        }

        [Fact]
        public void Attenuation_HalvesLowLevelSample()
        {
            short[] pcm = { 1000, -1000 };
            AudioGain.Apply(pcm, 0.5f);
            Assert.InRange(pcm[0], 499, 501);
            Assert.InRange(pcm[1], -501, -499);
        }

        [Fact]
        public void ZeroGain_SilencesEverything()
        {
            short[] pcm = { 1000, -1000, 32767, -32768 };
            AudioGain.Apply(pcm, 0.0f);
            Assert.All(pcm, s => Assert.Equal(0, s));
        }

        [Fact]
        public void Boost_NeverExceedsInt16Range()
        {
            short[] pcm = { 30000, -30000, 32767, -32768, 16000, -16000 };
            AudioGain.Apply(pcm, 2.0f);
            Assert.All(pcm, s => Assert.InRange(s, short.MinValue, short.MaxValue));
        }

        [Fact]
        public void Boost_PreservesSign()
        {
            short[] pcm = { 30000, -30000 };
            AudioGain.Apply(pcm, 2.0f);
            Assert.True(pcm[0] > 0);
            Assert.True(pcm[1] < 0);
        }

        [Fact]
        public void Boost_IsMonotonic()
        {
            short[] a = { 1000 };
            short[] b = { 2000 };
            AudioGain.Apply(a, 2.0f);
            AudioGain.Apply(b, 2.0f);
            Assert.True(b[0] >= a[0]);
        }

        [Fact]
        public void Boost_IsMonotonic_AcrossTheKnee()
        {
            // Both samples land above the 0.8 knee at gain 2.0 (20000/32768*2 = 1.22,
            // 30000/32768*2 = 1.83), so this exercises the tanh compressive region.
            short[] a = { 20000 };
            short[] b = { 30000 };
            AudioGain.Apply(a, 2.0f);
            AudioGain.Apply(b, 2.0f);
            Assert.True(b[0] >= a[0]);
            Assert.InRange(a[0], (short)1, short.MaxValue);   // compressed, still positive
        }

        // ── Peak ──────────────────────────────────────────────────────
        //
        // A muted or dead microphone still delivers buffers on time, 50 a second, full of
        // zeros — so frame counters, packet counters and every device check report a
        // perfectly healthy call while the far end hears nothing at all. The amplitude is
        // the only thing that tells the two apart.

        [Fact]
        public void Peak_OfDigitalSilenceIsZero()
        {
            Assert.Equal(0, AudioGain.Peak(new short[160]));
        }

        [Fact]
        public void Peak_IsTheLoudestSample()
        {
            short[] pcm = { 100, -300, 250, 0 };

            Assert.Equal(300, AudioGain.Peak(pcm));
        }

        [Fact]
        public void Peak_MeasuresMagnitudeSoASingleNegativeCounts()
        {
            Assert.Equal(1200, AudioGain.Peak(new short[] { -1200 }));
        }

        [Fact]
        public void Peak_HandlesTheMostNegativeSampleWithoutOverflowing()
        {
            // -32768 negates to 32768, which does not fit in a short. Reporting it as a
            // negative peak would read as "quieter than silence" in the log.
            Assert.Equal(32768, AudioGain.Peak(new short[] { short.MinValue }));
        }

        [Fact]
        public void Peak_OfAnEmptyBufferIsZero()
        {
            Assert.Equal(0, AudioGain.Peak(Array.Empty<short>()));
        }
    }
}
