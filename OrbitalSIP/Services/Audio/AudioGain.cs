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
