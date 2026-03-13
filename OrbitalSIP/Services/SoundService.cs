using System;
using System.IO;
using Avalonia.Threading;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace OrbitalSIP.Services
{
    /// <summary>
    /// Plays contextual sounds in response to SIP call state transitions.
    ///
    /// Sound files are resolved from the "sounds/" subdirectory next to the
    /// executable.  If a file is missing, the corresponding sound is silently
    /// skipped so the app remains fully functional without audio assets.
    ///
    /// State → sound mapping:
    ///   IncomingRinging  → ring.mp3          (looped)
    ///   Ringing          → ring-calling.mp3  (looped, outgoing ringback)
    ///   Active           → (loop stops)
    ///   Idle ← Active    → call_end.mp3      (one-shot)
    ///   Idle ← Ringing   → call_fail.mp3     (one-shot, outgoing rejected/failed)
    ///   Idle ← IncomingRinging → busy.mp3    (one-shot, caller cancelled)
    ///
    /// THREADING: Called from SIPSorcery background threads. All MediaPlayer
    /// (WinRT COM) operations are dispatched to the UI thread to avoid
    /// COM-apartment deadlocks that would freeze the application.
    /// </summary>
    public sealed class SoundService : IDisposable
    {
        private static string SoundsDir =>
            Path.Combine(AppContext.BaseDirectory, "sounds");

        private MediaPlayer? _loopPlayer;
        private MediaPlayer? _oneShotPlayer;
        private CallState    _prevState = CallState.Idle;

        /// <summary>
        /// Called (potentially from any thread) when the SIP call state changes.
        /// Dispatches the actual work to the UI thread so WinRT MediaPlayer
        /// objects are always created/used on the correct COM apartment.
        /// </summary>
        public void OnStateChanged(CallState newState)
        {
            // Capture prev-state on the calling thread before dispatching so
            // rapid successive calls preserve correct ordering.
            var prev = _prevState;
            _prevState = newState;

            Dispatcher.UIThread.Post(() => ApplyState(newState, prev));
        }

        private void ApplyState(CallState newState, CallState prev)
        {
            switch (newState)
            {
                case CallState.IncomingRinging:
                    PlayLoop("ring.mp3");
                    break;

                case CallState.Ringing:
                    PlayLoop("ring-calling.mp3");
                    break;

                case CallState.Active:
                    StopLoop();
                    break;

                case CallState.Idle:
                    StopLoop();
                    if      (prev == CallState.Active)          PlayOnce("call_end.mp3");
                    else if (prev == CallState.Ringing)         PlayOnce("call_fail.mp3");
                    else if (prev == CallState.IncomingRinging) PlayOnce("busy.mp3");
                    break;
            }
        }

        private void PlayLoop(string file)
        {
            StopLoop();
            var path = Path.Combine(SoundsDir, file);
            if (!File.Exists(path)) return;

            _loopPlayer = new MediaPlayer { IsLoopingEnabled = true };
            _loopPlayer.Source = MediaSource.CreateFromUri(new Uri(path));
            _loopPlayer.Play();
        }

        private void StopLoop()
        {
            if (_loopPlayer == null) return;
            // Setting Source to null stops playback immediately without requiring
            // a specific PlaybackState — Pause() throws COMException if the media
            // hasn't started loading yet.
            try { _loopPlayer.Source = null; } catch { }
            _loopPlayer.Dispose();
            _loopPlayer = null;
        }

        private void PlayOnce(string file)
        {
            var path = Path.Combine(SoundsDir, file);
            if (!File.Exists(path)) return;

            _oneShotPlayer?.Dispose();
            _oneShotPlayer = new MediaPlayer();
            _oneShotPlayer.Source = MediaSource.CreateFromUri(new Uri(path));
            _oneShotPlayer.Play();
        }

        public void Dispose()
        {
            StopLoop();
            _oneShotPlayer?.Dispose();
            _oneShotPlayer = null;
        }
    }
}
