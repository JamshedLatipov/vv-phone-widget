namespace OrbitalSIP.Models
{
    /// <summary>
    /// Maps between the saved waveOut/waveIn device index and its row in the Settings combo.
    ///
    /// The combo reads ["System Default", device 0, device 1, ...], so index and row differ
    /// by one. The part that was missing is what happens when the saved device is not
    /// present right now — a headset unplugged, a dock detached, a driver that has not come
    /// up yet. The old arithmetic produced an out-of-range row, Avalonia answered -1, the
    /// screen forced that to 0, and the next save wrote "System Default" over the
    /// operator's choice. Unplugging a headset and then changing the interface language was
    /// enough to lose it for good, and the operator ended up on laptop speakers in a room
    /// they cannot hear over.
    ///
    /// An absent device keeps a row of its own at the end of the list. Leaving that row
    /// selected means the operator did not touch the setting, so the saved index survives.
    /// </summary>
    public static class AudioDeviceChoice
    {
        /// <summary>True when a real device index was saved but no such device is enumerated now.</summary>
        public static bool IsMissing(int savedIndex, int deviceCount) =>
            savedIndex >= 0 && savedIndex >= deviceCount;

        /// <summary>Row to select for <paramref name="savedIndex"/>; the row after the live ones when it is absent.</summary>
        public static int ListPosition(int savedIndex, int deviceCount) =>
            IsMissing(savedIndex, deviceCount)
                ? deviceCount + 1
                : savedIndex + 1;

        /// <summary>
        /// Index to store for the selected row. <paramref name="savedIndexBefore"/> is what
        /// the settings file already held, and is what the placeholder row resolves back to.
        /// </summary>
        public static int SavedIndex(int listPosition, int deviceCount, int savedIndexBefore)
        {
            // Row 0 is System Default; a combo with nothing selected means the same thing.
            if (listPosition <= 0) return -1;

            // Rows 1..deviceCount are the devices actually present.
            if (listPosition <= deviceCount) return listPosition - 1;

            // The placeholder for a device that is not here at the moment.
            return savedIndexBefore;
        }
    }
}
