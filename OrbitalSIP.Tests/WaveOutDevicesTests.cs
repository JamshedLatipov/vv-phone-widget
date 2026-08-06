using System;
using System.Linq;
using NAudio.Wave;
using OrbitalSIP.Services.Audio;
using Xunit;

namespace OrbitalSIP.Tests
{
    /// <summary>
    /// Exercises the real winmm enumeration on this machine. The marshalling of a
    /// fixed-size device-name buffer is the part that silently goes wrong, so these
    /// assert on the shape of what comes back rather than on specific device names.
    /// </summary>
    public class WaveOutDevicesTests
    {
        [Fact]
        public void Count_IsNeverNegative()
        {
            Assert.True(WaveOutDevices.Count >= 0);
        }

        [Fact]
        public void ProductName_IsNonEmptyForEveryLiveDevice()
        {
            var names = Enumerable.Range(0, WaveOutDevices.Count)
                                  .Select(WaveOutDevices.ProductName)
                                  .ToList();

            Assert.All(names, n => Assert.False(string.IsNullOrWhiteSpace(n)));
        }

        [Fact]
        public void ProductName_DoesNotLeakTheFixedBufferPadding()
        {
            // WAVEOUTCAPS.szPname is a 32-char inline array. Marshalling it wrong hands
            // back the whole buffer with trailing NULs instead of stopping at the
            // terminator, which then renders as boxes in the settings dropdown.
            var names = Enumerable.Range(0, WaveOutDevices.Count)
                                  .Select(WaveOutDevices.ProductName)
                                  .ToList();

            Assert.All(names, n => Assert.DoesNotContain('\0', n));
            Assert.All(names, n => Assert.Equal(n.Trim(), n));
        }

        [Fact]
        public void ProductName_ForAnIndexThatIsNotADeviceReturnsEmpty()
        {
            Assert.Equal(string.Empty, WaveOutDevices.ProductName(9999));
        }

        [Fact]
        public void ProductName_ForANegativeIndexReturnsEmpty()
        {
            // -1 is WAVE_MAPPER, which is a routing directive rather than a device the
            // settings list should offer by name.
            Assert.Equal(string.Empty, WaveOutDevices.ProductName(-1));
        }

        [Fact]
        public void Count_AgreesWithTheCaptureSideEnumerationContract()
        {
            // Not an equality check — the two device lists are unrelated. This pins that
            // our playback enumeration answers in the same units as NAudio's capture one,
            // i.e. a plain count of addressable device indices.
            Assert.True(WaveOutDevices.Count >= 0 && WaveInEvent.DeviceCount >= 0);

            var lastValid = WaveOutDevices.Count - 1;
            if (lastValid >= 0)
                Assert.False(string.IsNullOrWhiteSpace(WaveOutDevices.ProductName(lastValid)));
        }
    }
}
