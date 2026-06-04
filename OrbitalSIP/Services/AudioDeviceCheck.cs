using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NAudio.Wave;

namespace OrbitalSIP.Services
{
    /// <summary>
    /// Startup audio self-test. Verifies the configured (or system-default)
    /// microphone and speakers can actually be OPENED in the SIP codec format —
    /// not merely that a device is enumerated. This reproduces the same
    /// "NAudio.MmException: InvalidParameter calling waveInOpen" that otherwise
    /// only surfaces mid-call (one-way audio / dropped call), and warns the
    /// operator up front via the UI banner (HttpErrorNotifier).
    /// </summary>
    public static class AudioDeviceCheck
    {
        // Narrowband PCM — the format WindowsAudioEndPoint records/plays for SIP.
        private static readonly WaveFormat ProbeFormat = new WaveFormat(8000, 16, 1);

        /// <summary>Fire-and-forget probe; never throws, logs + banners on problems.</summary>
        public static void RunInBackground()
        {
            _ = Task.Run(() =>
            {
                try
                {
                    var problems = Probe();
                    if (problems.Count > 0)
                    {
                        var msg = I18nService.Instance.Get("audio.problemHead", "Проблема со звуком")
                                  + ": " + string.Join(" ", problems);
                        HttpErrorNotifier.Notify(msg);
                    }
                }
                catch (Exception ex) { AppLogger.Log("AudioDeviceCheck", $"probe threw: {ex}"); }
            });
        }

        /// <summary>
        /// Open-probe the configured mic + speakers in the SIP format. Returns a list
        /// of human-readable problems (empty = everything OK). Never throws.
        /// </summary>
        public static List<string> Probe()
        {
            var i18n = I18nService.Instance;
            var settings = SipSettings.Load();
            var problems = new List<string>();

            // ── Microphone (capture) ──────────────────────────────────────
            if (WaveIn.DeviceCount == 0)
            {
                problems.Add(i18n.Get("audio.noMic", "Микрофон не найден (нет устройств записи)."));
            }
            else
            {
                int inIdx = settings.AudioInDeviceIndex; // -1 = system default (WAVE_MAPPER)
                if (inIdx >= WaveIn.DeviceCount)
                    problems.Add(i18n.Get("audio.micStale", "Выбранный микрофон недоступен — выберите устройство в Настройках."));
                else if (!TryOpenInput(inIdx, out var err))
                    problems.Add(i18n.Get("audio.micFail", "Не удаётся открыть микрофон") + (string.IsNullOrEmpty(err) ? "" : $": {err}"));
            }

            // ── Speakers (render) ─────────────────────────────────────────
            if (WaveOut.DeviceCount == 0)
            {
                problems.Add(i18n.Get("audio.noSpk", "Динамики не найдены (нет устройств воспроизведения)."));
            }
            else
            {
                int outIdx = settings.AudioOutDeviceIndex; // -1 = system default
                if (outIdx >= WaveOut.DeviceCount)
                    problems.Add(i18n.Get("audio.spkStale", "Выбранные динамики недоступны — выберите устройство в Настройках."));
                else if (!TryOpenOutput(outIdx, out var err))
                    problems.Add(i18n.Get("audio.spkFail", "Не удаётся открыть динамики") + (string.IsNullOrEmpty(err) ? "" : $": {err}"));
            }

            AppLogger.Log("AudioDeviceCheck",
                problems.Count == 0
                    ? $"OK — mic devices={WaveIn.DeviceCount}, speaker devices={WaveOut.DeviceCount}"
                    : "Problems: " + string.Join(" | ", problems));
            return problems;
        }

        private static bool TryOpenInput(int deviceNumber, out string? error)
        {
            error = null;
            WaveInEvent? wi = null;
            try
            {
                wi = new WaveInEvent
                {
                    DeviceNumber = deviceNumber,
                    WaveFormat = ProbeFormat,
                    BufferMilliseconds = 50,
                };
                wi.StartRecording(); // opens the capture device (waveInOpen)
                wi.StopRecording();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                AppLogger.Log("AudioDeviceCheck", $"mic open failed (idx={deviceNumber}): {ex.GetType().Name} — {ex.Message}");
                return false;
            }
            finally
            {
                try { wi?.Dispose(); } catch { /* ignore */ }
            }
        }

        private static bool TryOpenOutput(int deviceNumber, out string? error)
        {
            error = null;
            WaveOutEvent? wo = null;
            try
            {
                wo = new WaveOutEvent { DeviceNumber = deviceNumber };
                // Empty buffer = silence; Init+Play opens the render device (waveOutOpen).
                wo.Init(new BufferedWaveProvider(ProbeFormat));
                wo.Play();
                wo.Stop();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                AppLogger.Log("AudioDeviceCheck", $"speaker open failed (idx={deviceNumber}): {ex.GetType().Name} — {ex.Message}");
                return false;
            }
            finally
            {
                try { wo?.Dispose(); } catch { /* ignore */ }
            }
        }
    }
}
