using System;

namespace OrbitalSIP.Services.Audio
{
    /// <summary>
    /// Stateless per-sample digital gain with a soft limiter.
    /// gain 1.0 = unity passthrough. Below the knee (T) the response is linear;
    /// above it a tanh soft-clip smoothly asymptotes to full scale, so the output
    /// never wraps or overflows Int16 no matter how high the gain.
    /// </summary>
    public static class AudioGain
    {
        private const float T = 0.8f;          // soft-knee threshold, fraction of full scale
        private const float FullScale = 32768f;

        /// <summary>Amplify PCM in place. gain: 0.0 = silence, 1.0 = unity, 2.0 = +6 dB.</summary>
        public static void Apply(Span<short> pcm, float gain)
        {
            if (gain == 1.0f) return;          // fast path: unity is a no-op (no coloring)

            for (int i = 0; i < pcm.Length; i++)
            {
                float s = gain * (pcm[i] / FullScale);   // normalize to [-1,1] and apply gain
                float y = SoftClip(s);
                int v = (int)MathF.Round(y * FullScale);
                if (v > short.MaxValue) v = short.MaxValue;
                else if (v < short.MinValue) v = short.MinValue;
                pcm[i] = (short)v;
            }
        }

        /// <summary>
        /// Loudest sample magnitude in the buffer, 0 to 32768.
        ///
        /// This is the one measurement that separates a working microphone from a muted one.
        /// Everything else about the send path — frames captured, samples encoded, packets
        /// counted — looks identical either way: a muted device still delivers its buffers on
        /// time, 50 a second, and the encoder faithfully compresses digital silence into
        /// perfectly well-formed RTP. Only the amplitude is different.
        /// </summary>
        public static int Peak(ReadOnlySpan<short> pcm)
        {
            int peak = 0;

            for (int i = 0; i < pcm.Length; i++)
            {
                // Negating short.MinValue overflows a short, so widen before taking the
                // magnitude — otherwise the quietest possible reading is a negative peak.
                int magnitude = Math.Abs((int)pcm[i]);
                if (magnitude > peak) peak = magnitude;
            }

            return peak;
        }

        private static float SoftClip(float s)
        {
            float a = MathF.Abs(s);
            if (a <= T) return s;                          // linear region
            float sign = s < 0 ? -1f : 1f;
            float over = (a - T) / (1f - T);
            return sign * (T + (1f - T) * MathF.Tanh(over)); // smooth knee, ->±1.0
        }
    }
}
