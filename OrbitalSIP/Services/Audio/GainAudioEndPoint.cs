//-----------------------------------------------------------------------------
// Filename: WindowsAudioSession.cs
//
// Description: Example of an RTP session that uses NAUdio for audio
// capture and rendering on Windows.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 17 Apr 2020  Aaron Clauson	Created, Dublin, Ireland.
// 01 Jun 2020  Aaron Clauson   Refactored to use RtpAudioSession base class.
// 15 Aug 2020  Aaron Clauson   Moved from examples into SIPSorceryMedia.Windows
//                              assembly.
// 21 Jan 2021  Aaron Clauson   Adjust playback rate dependent on selected audio format.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using SIPSorceryMedia.Abstractions;

namespace OrbitalSIP.Services.Audio
{
    public class GainAudioEndPoint : IAudioSource, IAudioSink, IDisposable
    {
        private const int DEVICE_BITS_PER_SAMPLE = 16;
        private const int DEVICE_CHANNELS = 1;
        private const int INPUT_BUFFERS = 2;          // See https://github.com/sipsorcery/sipsorcery/pull/148.
        private const int AUDIO_SAMPLE_PERIOD_MILLISECONDS = 20;
        private const int AUDIO_INPUTDEVICE_INDEX = -1;
        private const int AUDIO_OUTPUTDEVICE_INDEX = -1;

        /// <summary>
        /// Microphone input is sampled at 8KHz.
        /// </summary>
        public readonly static AudioSamplingRatesEnum DefaultAudioSourceSamplingRate = AudioSamplingRatesEnum.Rate8KHz;

        public readonly static AudioSamplingRatesEnum DefaultAudioPlaybackRate = AudioSamplingRatesEnum.Rate8KHz;

        private ILogger logger = SIPSorcery.LogFactory.CreateLogger<GainAudioEndPoint>();

        private WaveFormat _waveSinkFormat;
        private WaveFormat _waveSourceFormat;

        /// <summary>
        /// Audio render device. Null whenever no device is open — before initialisation, after a
        /// failed open, and after close/dispose. The render path treats null as "drop the audio".
        /// </summary>
        private WaveOutEvent? _waveOutEvent;

        /// <summary>
        /// Buffer for audio samples to be rendered. Published only alongside a live
        /// <see cref="_waveOutEvent"/>, so it is never a buffer that nothing drains.
        /// </summary>
        private BufferedWaveProvider? _waveProvider;

        /// <summary>
        /// Audio capture device. Null whenever no device is open.
        /// </summary>
        private WaveInEvent? _waveInEvent;

        private IAudioEncoder _audioEncoder;
        private MediaFormatManager<AudioFormat> _audioFormatManager;

        private bool _disableSink;
        private int _audioOutDeviceIndex;
        private int _audioInDeviceIndex;
        private bool _disableSource;

        protected bool _isAudioSourceStarted;
        protected bool _isAudioSinkStarted;
        protected bool _isAudioSourcePaused;
        protected bool _isAudioSinkPaused;
        protected bool _isAudioSourceClosed;
        protected bool _isAudioSinkClosed;

        /// <summary>Set once <see cref="Dispose"/> has run, so a repeat close is a no-op.</summary>
        private bool _disposed;

        /// <summary>
        /// Whether a render device is actually open. The constructor initialises playback before
        /// any caller can subscribe to <see cref="OnAudioSinkError"/>, so the failure that matters
        /// most — the one at call setup — is only visible by reading this back afterwards.
        /// False also while the sink is closed or disposed.
        /// </summary>
        public bool IsPlaybackDeviceOpen => _waveOutEvent != null;

        /// <summary>Outgoing (mic) gain factor. 1.0 = unity. Written on the UI thread, read on the audio thread; volatile for timely cross-thread visibility (float writes are already atomic).</summary>
        public volatile float SourceGain = 1f;
        /// <summary>Incoming (speaker) gain factor. 1.0 = unity.</summary>
        public volatile float SinkGain = 1f;

        /// <summary>
        /// Reusable scratch for the capture path. Both directions run 50 times a second,
        /// so allocating per packet is what starves the audio threads on a slow machine —
        /// these buffers are sized once and then reused for the life of the call.
        /// Only ever touched on the NAudio capture callback thread.
        /// </summary>
        private short[] _captureSamples = Array.Empty<short>();

        /// <summary>Reusable scratch for the render path, guarded because RTP delivery is not contractually single-threaded.</summary>
        private byte[] _renderBytes = Array.Empty<byte>();
        private readonly object _renderLock = new object();

        /// <summary>Backlog to settle at after a trim — roughly four 20 ms packets of jitter cushion.</summary>
        private static readonly TimeSpan PlaybackTargetLatency = TimeSpan.FromMilliseconds(80);

        /// <summary>Backlog at which the oldest audio starts being dropped.</summary>
        private static readonly TimeSpan PlaybackTriggerLatency = TimeSpan.FromMilliseconds(200);

        /// <summary>Hard ceiling for the render buffer, in case trimming ever stops running.</summary>
        private static readonly TimeSpan PlaybackBufferCeiling = TimeSpan.FromSeconds(1);

        /// <summary>Sink for discarded backlog. Fixed size; the trim loops over it.</summary>
        private readonly byte[] _discardScratch = new byte[4096];

        /// <summary>Rendered packet count, used only to sample the backlog into the log.</summary>
        private int _renderPacketCount;

        // The four events below implement SIPSorceryMedia.Abstractions interfaces that are
        // not nullable-annotated, so they cannot be declared `?` without trading nine
        // CS8618s for four nullability-mismatch warnings. `= null!` states the same thing
        // the interface already does — an unsubscribed event is null, and every raise site
        // here uses `?.Invoke`.

        /// <summary>
        /// Not used by this audio source.
        /// </summary>
        public event EncodedSampleDelegate OnAudioSourceEncodedSample = null!;

        /// <summary>
        /// Encoded capture frame ready for the RTP transport. VoIPMediaSession still sends from
        /// <see cref="OnAudioSourceEncodedSample"/>, so this stays unsubscribed in practice and the
        /// frame is only built when something actually listens.
        /// </summary>
        public event Action<EncodedAudioFrame> OnAudioSourceEncodedFrameReady = null!;

        /// <summary>
        /// This audio source DOES NOT generate raw samples. Subscribe to the encoded samples event
        /// to get samples ready for passing to the RTP transport layer.
        /// </summary>
        [Obsolete("The audio source only generates encoded samples.")]
        public event RawAudioSampleDelegate OnAudioSourceRawSample { add { } remove { } }

        public event SourceErrorDelegate OnAudioSourceError = null!;

        public event SourceErrorDelegate OnAudioSinkError = null!;

        /// <summary>
        /// Creates a new basic RTP session that captures and renders audio to/from the default system devices.
        /// </summary>
        /// <param name="audioEncoder">An audio encoder that can be used to encode and decode
        /// specific audio codecs.</param>
        /// <param name="externalSource">Optional. An external source to use in combination with the source
        /// provided by this end point. The application will need to signal which source is active.</param>
        /// <param name="disableSource">Set to true to disable the use of the audio source functionality, i.e.
        /// don't capture input from the microphone.</param>
        /// <param name="disableSink">Set to true to disable the use of the audio sink functionality, i.e.
        /// don't playback audio to the speaker.</param>
        public GainAudioEndPoint(IAudioEncoder audioEncoder,
            int audioOutDeviceIndex = AUDIO_OUTPUTDEVICE_INDEX,
            int audioInDeviceIndex = AUDIO_INPUTDEVICE_INDEX,
            bool disableSource = false,
            bool disableSink = false)
        {
            logger = SIPSorcery.LogFactory.CreateLogger<GainAudioEndPoint>();

            _audioFormatManager = new MediaFormatManager<AudioFormat>(audioEncoder.SupportedFormats);
            _audioEncoder = audioEncoder;

            _audioOutDeviceIndex = audioOutDeviceIndex;
            _audioInDeviceIndex = audioInDeviceIndex;
            _disableSource = disableSource;
            _disableSink = disableSink;

            if (!_disableSink)
            {
                InitPlaybackDevice(_audioOutDeviceIndex, DefaultAudioPlaybackRate.GetHashCode());
            }

            if (!_disableSource)
            {
                InitCaptureDevice(_audioInDeviceIndex, (int)DefaultAudioSourceSamplingRate);
            }
        }

        public void RestrictFormats(Func<AudioFormat, bool> filter) => _audioFormatManager.RestrictFormats(filter);
        public List<AudioFormat> GetAudioSourceFormats() => _audioFormatManager.GetSourceFormats();
        public List<AudioFormat> GetAudioSinkFormats() => _audioFormatManager.GetSourceFormats();

        public bool HasEncodedAudioSubscribers() => OnAudioSourceEncodedSample != null;
        public bool IsAudioSourcePaused() => _isAudioSourcePaused;
        public bool IsAudioSinkPaused() => _isAudioSinkPaused;
        public void ExternalAudioSourceRawSample(AudioSamplingRatesEnum samplingRate, uint durationMilliseconds, short[] sample) =>
            throw new NotImplementedException();

        public void SetAudioSourceFormat(AudioFormat audioFormat)
        {
            _audioFormatManager.SetSelectedFormat(audioFormat);

            if (!_disableSource)
            {
                if (_waveSourceFormat.SampleRate != _audioFormatManager.SelectedFormat.ClockRate)
                {
                    // Reinitialise the audio capture device.
                    logger.LogDebug($"Windows audio end point adjusting capture rate from {_waveSourceFormat.SampleRate} to {_audioFormatManager.SelectedFormat.ClockRate}.");

                    InitCaptureDevice(_audioInDeviceIndex, _audioFormatManager.SelectedFormat.ClockRate);
                }
            }
        }

        public void SetAudioSinkFormat(AudioFormat audioFormat)
        {
            _audioFormatManager.SetSelectedFormat(audioFormat);

            if (!_disableSink)
            {
                if (_waveSinkFormat.SampleRate != _audioFormatManager.SelectedFormat.ClockRate)
                {
                    // Reinitialise the audio output device.
                    logger.LogDebug($"Windows audio end point adjusting playback rate from {_waveSinkFormat.SampleRate} to {_audioFormatManager.SelectedFormat.ClockRate}.");

                    InitPlaybackDevice(_audioOutDeviceIndex, _audioFormatManager.SelectedFormat.ClockRate);
                }
            }
        }

        public MediaEndPoints ToMediaEndPoints()
        {
            return new MediaEndPoints
            {
                AudioSource = (_disableSource) ? null : this,
                AudioSink = (_disableSink) ? null : this,
            };
        }

        /// <summary>
        /// Starts the media capturing/source devices.
        /// </summary>
        public Task StartAudio()
        {
            if (!_isAudioSourceStarted)
            {
                _isAudioSourceStarted = true;
                _waveInEvent?.StartRecording();
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Closes the session.
        /// </summary>
        public Task CloseAudio()
        {
            if (!_isAudioSourceClosed)
            {
                _isAudioSourceClosed = true;
                DisposeCaptureDevice();
            }

            return Task.CompletedTask;
        }

        public Task PauseAudio()
        {
            _isAudioSourcePaused = true;
            _waveInEvent?.StopRecording();
            return Task.CompletedTask;
        }

        public Task ResumeAudio()
        {
            _isAudioSourcePaused = false;
            _waveInEvent?.StartRecording();
            return Task.CompletedTask;
        }

        private void InitPlaybackDevice(int audioOutDeviceIndex, int audioSinkSampleRate)
        {
            // Stop() only issues waveOutReset — the winmm handle stays open until Dispose().
            // Every re-init used to stack a fresh WaveOutEvent on top of the old one, so the
            // handles accumulated for the life of the process until waveOutOpen started failing.
            DisposePlaybackDevice();

            int deviceCount = WaveOutDevices.Count;
            int deviceNumber = audioOutDeviceIndex;

            // WAVE_MAPPER resolves on any machine that owns a playback device at all, so
            // failing this check means there is nothing left to fall back to.
            if (!PlaybackDevice.IsUsable(AUDIO_OUTPUTDEVICE_INDEX, deviceCount))
            {
                ReportSinkFailure("No audio playback devices are available.");
                return;
            }

            if (!PlaybackDevice.IsUsable(deviceNumber, deviceCount))
            {
                // The saved index outlived the device it named: a headset unplugged, a dock
                // detached, a driver renumbering the list. The capture side has always checked
                // this, while the render side let the stale index reach waveOutOpen and throw.
                // Detecting it is not enough — falling back is what keeps the operator on a
                // call they can actually hear.
                ReportSinkFailure(
                    $"Playback device index {deviceNumber} is not among the {deviceCount} device(s) present; falling back to the system default.");
                deviceNumber = AUDIO_OUTPUTDEVICE_INDEX;
            }

            try
            {
                _waveSinkFormat = new WaveFormat(
                    audioSinkSampleRate,
                    DEVICE_BITS_PER_SAMPLE,
                    DEVICE_CHANNELS);

                // Playback device. Built locally and only published once Init has actually
                // opened the device, so a failure cannot leave a half-live render path behind.
                var waveOut = new WaveOutEvent();
                waveOut.DeviceNumber = deviceNumber;
                var provider = new BufferedWaveProvider(_waveSinkFormat);
                // NAudio defaults this to 5 seconds. That is the worst case the operator
                // can end up hearing, since the backlog never drains on its own — active
                // trimming keeps it near the target and this is only the backstop.
                provider.BufferDuration = PlaybackBufferCeiling;
                provider.DiscardOnBufferOverflow = true;
                waveOut.Init(provider);

                _waveOutEvent = waveOut;
                lock (_renderLock) { _waveProvider = provider; }
            }
            catch (Exception excp)
            {
                // Publish nothing. A provider that no device drains swallows the entire call
                // in silence: RTP keeps arriving, the trim keeps discarding it, the PBX keeps
                // recording a perfectly two-sided call, and the operator hears nothing.
                DisposePlaybackDevice();

                logger.LogWarning(0, excp, "WindowsAudioEndPoint failed to initialise playback device.");
                AppLogger.Log("Audio",
                    $"Playback device failed to open (index {audioOutDeviceIndex}, "
                  + $"{audioSinkSampleRate} Hz): {excp.GetType().Name} — {excp.Message}");
                OnAudioSinkError?.Invoke($"WindowsAudioEndPoint failed to initialise playback device. {excp.Message}");
            }
        }

        /// <summary>
        /// Releases the render device. Stopping is not releasing: <see cref="WaveOutEvent.Stop"/>
        /// issues waveOutReset and leaves the handle open, only Dispose issues waveOutClose.
        /// Never throws — it runs on call teardown, where an MmException would take the
        /// teardown with it.
        /// </summary>
        /// <summary>
        /// Reports a render-side failure the same way the catch below does. The constructor runs
        /// before anything can subscribe to <see cref="OnAudioSinkError"/>, so the log line is the
        /// part that always survives; <see cref="IsPlaybackDeviceOpen"/> is what callers read back.
        /// </summary>
        private void ReportSinkFailure(string message)
        {
            logger.LogWarning(message);
            AppLogger.Log("Audio", message);
            OnAudioSinkError?.Invoke(message);
        }

        private void DisposePlaybackDevice()
        {
            var waveOut = _waveOutEvent;
            _waveOutEvent = null;
            lock (_renderLock) { _waveProvider = null; }

            if (waveOut == null) return;

            try { waveOut.Dispose(); }
            catch (Exception ex)
            {
                AppLogger.Log("Audio", $"Playback device dispose threw: {ex.GetType().Name} — {ex.Message}");
            }
        }

        /// <summary>
        /// Releases the capture device. Same handle-lifetime rule as the render side:
        /// StopRecording issues waveInReset, only Dispose issues waveInClose. Never throws.
        /// </summary>
        private void DisposeCaptureDevice()
        {
            var waveIn = _waveInEvent;
            _waveInEvent = null;

            if (waveIn == null) return;

            try
            {
                waveIn.DataAvailable -= LocalAudioSampleAvailable;
                waveIn.StopRecording();
            }
            catch (Exception ex)
            {
                AppLogger.Log("Audio", $"Capture device stop threw: {ex.GetType().Name} — {ex.Message}");
            }

            try { waveIn.Dispose(); }
            catch (Exception ex)
            {
                AppLogger.Log("Audio", $"Capture device dispose threw: {ex.GetType().Name} — {ex.Message}");
            }
        }

        private void InitCaptureDevice(int audioInDeviceIndex, int audioSourceSampleRate)
        {
            if (WaveInEvent.DeviceCount > 0)
            {
                if (WaveInEvent.DeviceCount > audioInDeviceIndex)
                {
                    DisposeCaptureDevice();

                    _waveSourceFormat = new WaveFormat(
                           audioSourceSampleRate,
                           DEVICE_BITS_PER_SAMPLE,
                           DEVICE_CHANNELS);

                    _waveInEvent = new WaveInEvent();
                    _waveInEvent.BufferMilliseconds = AUDIO_SAMPLE_PERIOD_MILLISECONDS;
                    _waveInEvent.NumberOfBuffers = INPUT_BUFFERS;
                    _waveInEvent.DeviceNumber = audioInDeviceIndex;
                    _waveInEvent.WaveFormat = _waveSourceFormat;
                    _waveInEvent.DataAvailable += LocalAudioSampleAvailable;
                }
                else
                {
                    logger.LogWarning($"The requested audio input device index {audioInDeviceIndex} exceeds the maximum index of {WaveInEvent.DeviceCount - 1}.");
                    OnAudioSourceError?.Invoke($"The requested audio input device index {audioInDeviceIndex} exceeds the maximum index of {WaveInEvent.DeviceCount - 1}.");
                }
            }
            else
            {
                logger.LogWarning("No audio capture devices are available.");
                OnAudioSourceError?.Invoke("No audio capture devices are available.");
            }
        }

        /// <summary>
        /// Event handler for audio sample being supplied by local capture device.
        /// </summary>
        private void LocalAudioSampleAvailable(object? sender, WaveInEventArgs args)
        {
            // Note NAudio.Wave.WaveBuffer.ShortBuffer does not take into account little endian.
            // https://github.com/naudio/NAudio/blob/master/NAudio/Wave/WaveOutputs/WaveBuffer.cs
            // PcmBuffer does the same little-endian reinterpret, but without copying the
            // capture buffer twice on every 20 ms tick.

            int sampleCount = PcmBuffer.SampleCount(args.BytesRecorded);
            if (sampleCount == 0) return;

            // The encoder reads the whole array, so the scratch has to match the recorded
            // length exactly. NAudio's buffer size is fixed, so this resizes once per call.
            PcmBuffer.EnsureExact(ref _captureSamples, sampleCount);
            PcmBuffer.ToSamples(args.Buffer.AsSpan(0, args.BytesRecorded), _captureSamples);

            AudioGain.Apply(_captureSamples, SourceGain);
            byte[] encodedSample = _audioEncoder.EncodeAudio(_captureSamples, _audioFormatManager.SelectedFormat);
            OnAudioSourceEncodedSample?.Invoke((uint)encodedSample.Length, encodedSample);

            var frameReady = OnAudioSourceEncodedFrameReady;
            if (frameReady != null)
            {
                var format = _audioFormatManager.SelectedFormat;
                frameReady(new EncodedAudioFrame(0, format,
                    EncodedFrameDurationMs(sampleCount, format), encodedSample));
            }
        }

        /// <summary>Duration the encoded frame covers, needed for the RTP timestamp.</summary>
        private static uint EncodedFrameDurationMs(int totalPcmSamples, AudioFormat audioFormat)
        {
            int channels = audioFormat.ChannelCount;
            int sampleRate = audioFormat.ClockRate;
            if (channels <= 0 || sampleRate <= 0) return 0;

            return (uint)Math.Round(totalPcmSamples / (double)channels / sampleRate * 1000.0);
        }

        /// <summary>
        /// Event handler for playing audio samples received from the remote call party.
        /// </summary>
        /// <param name="pcmSample">Raw PCM sample from remote party.</param>
        public void GotAudioSample(byte[] pcmSample)
        {
            var provider = _waveProvider;
            if (provider == null) return;

            lock (_renderLock)
            {
                provider.AddSamples(pcmSample, 0, pcmSample.Length);
                TrimPlaybackBacklog(provider);
            }
        }

        /// <summary>
        /// Drops the oldest buffered audio once the playback backlog grows past the trigger.
        ///
        /// Nothing else bounds it: WaveOut consumes in real time and RTP arrives in real time,
        /// so a stall at call start, a GC pause, or a sample-clock difference between the two
        /// ends all turn into latency that lasts the rest of the call. Caller holds _renderLock.
        /// </summary>
        private void TrimPlaybackBacklog(BufferedWaveProvider provider)
        {
            // Sampled into the log so a single call shows whether the backlog grows steadily
            // (clock drift) or jumps once and plateaus (a stall around playback start).
            if (++_renderPacketCount % 250 == 0)
                AppLogger.Log("Audio",
                    $"Playback backlog {provider.BufferedDuration.TotalMilliseconds:F0} ms " +
                    $"after {_renderPacketCount} packets");

            int excess = JitterTrim.ExcessBytes(
                provider.BufferedBytes, provider.WaveFormat, PlaybackTargetLatency, PlaybackTriggerLatency);
            if (excess <= 0) return;

            var before = provider.BufferedDuration;

            int remaining = excess;
            while (remaining > 0)
            {
                int read = provider.Read(_discardScratch, 0, Math.Min(remaining, _discardScratch.Length));
                if (read <= 0) break;
                remaining -= read;
            }

            AppLogger.Log("Audio",
                $"Playback backlog trimmed {before.TotalMilliseconds:F0} ms -> " +
                $"{provider.BufferedDuration.TotalMilliseconds:F0} ms ({excess - remaining} bytes dropped)");
        }

        /// <summary>
        /// Playback path for audio received from the remote party. From SIPSorcery 10 this is the
        /// only one that runs: VoIPMediaSession wires the sink to OnAudioFrameReceived and never
        /// calls GotAudioRtp again.
        /// </summary>
        public void GotEncodedMediaFrame(EncodedAudioFrame encodedMediaFrame)
        {
            var format = encodedMediaFrame.AudioFormat;
            if (format.IsEmpty()) return;

            // The format the far end actually sends is only known once media arrives, and it is not
            // always the negotiated one. Rendering 16 KHz G.722 through a device opened at 8 KHz
            // plays it at half speed, so the device follows the frame.
            if (_waveSinkFormat != null && _waveSinkFormat.SampleRate != format.ClockRate)
            {
                SetAudioSinkFormat(format);

                // A re-initialised playback device comes back stopped.
                if (_isAudioSinkStarted) _waveOutEvent?.Play();
            }

            Render(encodedMediaFrame.EncodedAudio, format);
        }

        /// <summary>
        /// Obsolete receive path. Implemented only because IAudioSink still declares it; nothing in
        /// SIPSorcery 10 calls it.
        /// </summary>
        [Obsolete("Use GotEncodedMediaFrame instead.")]
        public void GotAudioRtp(IPEndPoint remoteEndPoint, uint ssrc, uint seqnum, uint timestamp, int payloadID, bool marker, byte[] payload)
            => Render(payload, _audioFormatManager.SelectedFormat);

        private void Render(byte[] payload, AudioFormat format)
        {
            // Snapshot the provider — InitPlaybackDevice can swap it from another thread.
            var provider = _waveProvider;
            if (provider == null || _audioEncoder == null) return;

            var pcmSample = _audioEncoder.DecodeAudio(payload, format);
            AudioGain.Apply(pcmSample, SinkGain);

            // BufferedWaveProvider copies into its own ring buffer, so the scratch is
            // free for reuse the moment AddSamples returns.
            lock (_renderLock)
            {
                int byteCount = pcmSample.Length * 2;
                PcmBuffer.EnsureAtLeast(ref _renderBytes, byteCount);
                PcmBuffer.ToBytes(pcmSample, _renderBytes);
                provider.AddSamples(_renderBytes, 0, byteCount);

                TrimPlaybackBacklog(provider);
            }
        }

        public Task PauseAudioSink()
        {
            _isAudioSinkPaused = true;
            _waveOutEvent?.Pause();
            return Task.CompletedTask;
        }

        public Task ResumeAudioSink()
        {
            _isAudioSinkPaused = false;
            _waveOutEvent?.Play();
            return Task.CompletedTask;
        }

        public Task StartAudioSink()
        {
            if (!_isAudioSinkStarted)
            {
                _isAudioSinkStarted = true;
                _waveOutEvent?.Play();
            }
            return Task.CompletedTask;
        }

        public Task CloseAudioSink()
        {
            if (!_isAudioSinkClosed)
            {
                _isAudioSinkClosed = true;
                DisposePlaybackDevice();
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Releases both winmm devices. This is the only thing that closes them: NAudio's
        /// Stop/StopRecording reset the stream but keep the handle open, so a widget left
        /// running for a shift leaked one capture and one render handle per call. Once the
        /// driver ran out, waveOutOpen failed and the next call opened no speaker at all —
        /// while the PBX went on recording a healthy two-sided conversation.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _isAudioSourceClosed = true;
            _isAudioSinkClosed = true;

            DisposeCaptureDevice();
            DisposePlaybackDevice();
        }
    }
}
