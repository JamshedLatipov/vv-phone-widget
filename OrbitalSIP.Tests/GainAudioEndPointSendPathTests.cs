using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using OrbitalSIP.Services.Audio;
using SIPSorcery.Media;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace OrbitalSIP.Tests
{
    /// <summary>
    /// Guards the microphone-to-transport half of <see cref="GainAudioEndPoint"/>.
    ///
    /// Every other symptom of a broken call is visible from somewhere: no playback device
    /// raises an error, no RTP arriving shows up as a received-packet count of zero. A dead
    /// send path shows up nowhere at all — the call connects, the operator hears the far end,
    /// and the far end hears silence until somebody says so out loud. <c>EncodedSampleCount</c>
    /// exists to make it observable, and these tests are what keep it honest.
    /// </summary>
    public class GainAudioEndPointSendPathTests
    {
        /// <summary>Generous: NAudio hands over the first 20 ms buffer well inside this, even on a loaded agent.</summary>
        private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(5);

        private static GainAudioEndPoint Create()
        {
            // Playback is irrelevant here and a render device is not always present; leaving the
            // sink out keeps the test about the send path alone.
            var endPoint = new GainAudioEndPoint(new AudioEncoder(), disableSink: true);
            endPoint.RestrictFormats(f => f.FormatName == "PCMU" || f.FormatName == "G722");
            return endPoint;
        }

        private static AudioFormat FormatNamed(GainAudioEndPoint endPoint, string name) =>
            endPoint.GetAudioSourceFormats().First(f => f.FormatName == name);

        private static bool WaitForFrames(GainAudioEndPoint endPoint, long moreThan)
        {
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < CaptureTimeout)
            {
                if (endPoint.EncodedSampleCount > moreThan) return true;
                Thread.Sleep(20);
            }

            return false;
        }

        [Fact]
        public void EncodedSampleCount_StartsAtZero()
        {
            using var endPoint = Create();

            Assert.Equal(0, endPoint.EncodedSampleCount);
        }

        [Fact]
        public void EncodedSampleCount_StaysAtZeroUntilTheSourceIsStarted()
        {
            // The device is opened in the constructor but must not be recording yet: a widget
            // sitting idle between calls has no business holding a live microphone.
            using var endPoint = Create();

            Thread.Sleep(200);

            Assert.Equal(0, endPoint.EncodedSampleCount);
        }

        [RequiresCaptureDeviceFact]
        public void StartAudio_ProducesEncodedFrames()
        {
            using var endPoint = Create();
            endPoint.SetAudioSourceFormat(FormatNamed(endPoint, "PCMU"));

            endPoint.StartAudio();

            Assert.True(WaitForFrames(endPoint, 0),
                "the microphone produced no encoded frames — this is the silent one-way call.");
        }

        [RequiresCaptureDeviceFact]
        public void AFormatChangeMidCall_LeavesTheMicrophoneRunning()
        {
            // The regression this exists for: changing the clock rate re-creates the capture
            // device, and a re-created device comes back stopped. StartAudio is one-shot, so
            // nothing started it again and the microphone was dead for the rest of the call —
            // with the device open, the gain applied and not one line in any log.
            using var endPoint = Create();
            endPoint.SetAudioSourceFormat(FormatNamed(endPoint, "PCMU"));
            endPoint.StartAudio();
            Assert.True(WaitForFrames(endPoint, 0), "precondition: the microphone must be running first.");

            var beforeSwitch = endPoint.EncodedSampleCount;
            endPoint.SetAudioSourceFormat(FormatNamed(endPoint, "G722"));   // 16 kHz against PCMU's 8 kHz

            Assert.True(WaitForFrames(endPoint, beforeSwitch),
                "the microphone stopped when the codec changed — the far end would hear nothing from here on.");
        }

        [RequiresCaptureDeviceFact]
        public void PauseAudio_StopsTheFramesAndResumeBringsThemBack()
        {
            using var endPoint = Create();
            endPoint.SetAudioSourceFormat(FormatNamed(endPoint, "PCMU"));
            endPoint.StartAudio();
            Assert.True(WaitForFrames(endPoint, 0), "precondition: the microphone must be running first.");

            endPoint.PauseAudio();
            Thread.Sleep(200);
            var whilePaused = endPoint.EncodedSampleCount;
            Thread.Sleep(200);
            Assert.Equal(whilePaused, endPoint.EncodedSampleCount);

            endPoint.ResumeAudio();

            Assert.True(WaitForFrames(endPoint, whilePaused),
                "un-muting did not bring the microphone back.");
        }

        [RequiresCaptureDeviceFact]
        public void CloseAudio_StopsTheFrames()
        {
            using var endPoint = Create();
            endPoint.SetAudioSourceFormat(FormatNamed(endPoint, "PCMU"));
            endPoint.StartAudio();
            Assert.True(WaitForFrames(endPoint, 0), "precondition: the microphone must be running first.");

            endPoint.CloseAudio();
            Thread.Sleep(200);
            var afterClose = endPoint.EncodedSampleCount;
            Thread.Sleep(200);

            Assert.Equal(afterClose, endPoint.EncodedSampleCount);
        }
    }
}
