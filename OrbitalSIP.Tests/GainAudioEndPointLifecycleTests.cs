using System;
using OrbitalSIP.Services.Audio;
using SIPSorcery.Media;
using Xunit;

namespace OrbitalSIP.Tests
{
    /// <summary>
    /// Guards the device lifecycle of <see cref="GainAudioEndPoint"/>.
    ///
    /// NAudio's Stop/StopRecording reset the stream but leave the winmm handle open — only
    /// Dispose closes it. The endpoint used to be built fresh for every call and never
    /// disposed, so handles accumulated for the life of the widget until waveOutOpen started
    /// failing. The failure was invisible from the PBX side: RTP still flowed both ways and
    /// MixMonitor still recorded a healthy two-sided call, while the operator heard silence.
    ///
    /// These run against the real devices on this machine, matching WaveOutDevicesTests, and
    /// are written to pass on a box with no audio hardware at all (construction degrades to a
    /// closed sink rather than throwing).
    /// </summary>
    public class GainAudioEndPointLifecycleTests
    {
        private static GainAudioEndPoint Create() => new GainAudioEndPoint(new AudioEncoder());

        [Fact]
        public void Dispose_IsIdempotent()
        {
            var endPoint = Create();

            endPoint.Dispose();
            endPoint.Dispose();
            endPoint.Dispose();
        }

        [RequiresPlaybackDeviceFact]
        public void Dispose_ClosesThePlaybackDevice()
        {
            var endPoint = Create();

            endPoint.Dispose();

            Assert.False(endPoint.IsPlaybackDeviceOpen);
        }

        [RequiresPlaybackDeviceFact]
        public void CloseAudioSink_ClosesThePlaybackDevice()
        {
            using var endPoint = Create();

            endPoint.CloseAudioSink();

            Assert.False(endPoint.IsPlaybackDeviceOpen);
        }

        [Fact]
        public void CloseThenDispose_DoesNotThrow()
        {
            var endPoint = Create();

            endPoint.CloseAudio();
            endPoint.CloseAudioSink();
            endPoint.Dispose();
        }

        [RequiresPlaybackDeviceFact]
        public void GotAudioSample_AfterDispose_IsANoOp()
        {
            var endPoint = Create();
            endPoint.Dispose();

            // A packet still in flight when the call tore down must not resurrect the render
            // path or touch a disposed device.
            endPoint.GotAudioSample(new byte[320]);

            Assert.False(endPoint.IsPlaybackDeviceOpen);
        }

        [Fact]
        public void IsPlaybackDeviceOpen_MatchesWhetherThisMachineHasSpeakers()
        {
            using var endPoint = Create();

            // On a machine with a render device the constructor must leave it open — that is
            // the state SipService reads back to decide whether to warn the operator.
            Assert.Equal(WaveOutDevices.Count > 0, endPoint.IsPlaybackDeviceOpen);
        }

        [RequiresPlaybackDeviceFact]
        public void RepeatedCreateAndDispose_KeepsOpeningTheDevice()
        {
            // The leak's shape: each undisposed endpoint held a handle, so the Nth open failed.
            // Disposing every time must keep the very last one succeeding just like the first.
            //
            // The attribute matters here more than anywhere: without a device every endpoint
            // reports closed, so this asserted false == false sixty-four times and passed
            // whether or not anything was being released.
            //
            // Measured honestly: commenting out the body of Dispose() does NOT turn this
            // test red on a desktop Windows driver — 64 stranded handles are simply not
            // enough to exhaust it. Dispose_ClosesThePlaybackDevice and
            // GotAudioSample_AfterDispose_IsANoOp are the two that actually catch that
            // mutation. Keep this one as a smoke test for repeated open/close, not as the
            // guard against the leak.
            using (var first = Create())
            {
                Assert.True(first.IsPlaybackDeviceOpen,
                    "This machine reports a playback device, so the first endpoint must open it.");
            }

            for (int i = 0; i < 64; i++)
            {
                using var endPoint = Create();
                Assert.True(endPoint.IsPlaybackDeviceOpen,
                    $"Endpoint {i} could not open the device — a handle from an earlier one was not released.");
            }
        }
    }
}
