namespace OrbitalSIP.Services.Audio
{
    /// <summary>
    /// Whether a saved playback device index still points at something that can play audio.
    ///
    /// Settings persist a waveOut index, but the device list behind it is not stable: a USB
    /// headset gets unplugged, Bluetooth is switched off, a dock is detached, a driver update
    /// renumbers everything. The index survives all of that and ends up addressing a device
    /// that is no longer there.
    ///
    /// Handing such an index to <c>WaveOutEvent.DeviceNumber</c> makes <c>Init</c> throw, and
    /// the operator is left on a call they cannot hear. The capture side has always checked
    /// its index against <c>WaveInEvent.DeviceCount</c>; this is the same check for playback.
    /// </summary>
    public static class PlaybackDevice
    {
        /// <summary>
        /// True when <paramref name="requestedIndex"/> can be opened on a machine reporting
        /// <paramref name="deviceCount"/> waveOut devices.
        ///
        /// A negative index is WAVE_MAPPER — "whatever Windows considers the default" — which
        /// is a routing directive rather than a device, so it is usable as long as the machine
        /// has any playback device at all. With none, nothing is usable, mapper included.
        /// </summary>
        public static bool IsUsable(int requestedIndex, int deviceCount)
        {
            if (deviceCount <= 0) return false;
            if (requestedIndex < 0) return true;

            return requestedIndex < deviceCount;
        }
    }
}
