using OrbitalSIP.Services.Audio;
using Xunit;

namespace OrbitalSIP.Tests
{
    public class PlaybackDeviceTests
    {
        [Fact]
        public void SystemDefaultIsUsableWhenTheMachineHasSpeakers()
        {
            Assert.True(PlaybackDevice.IsUsable(-1, 3));
        }

        [Fact]
        public void SystemDefaultIsNotUsableWhenThereIsNothingToPlayThrough()
        {
            Assert.False(PlaybackDevice.IsUsable(-1, 0));
        }

        [Fact]
        public void TheFirstDeviceIsUsable()
        {
            Assert.True(PlaybackDevice.IsUsable(0, 3));
        }

        [Fact]
        public void TheLastDeviceIsUsable()
        {
            Assert.True(PlaybackDevice.IsUsable(2, 3));
        }

        /// <summary>
        /// The saved index is one past the end. This is the shape the bug took: settings
        /// keep an index, the operator's headset goes away, and the index outlives it.
        /// </summary>
        [Fact]
        public void AnIndexOnePastTheLastDeviceIsNotUsable()
        {
            Assert.False(PlaybackDevice.IsUsable(3, 3));
        }

        [Fact]
        public void AnIndexLeftBehindByAnUnpluggedHeadsetIsNotUsable()
        {
            Assert.False(PlaybackDevice.IsUsable(7, 2));
        }

        [Fact]
        public void NoDeviceIsUsableOnAMachineWithNoPlaybackDevices()
        {
            Assert.False(PlaybackDevice.IsUsable(0, 0));
        }
    }
}
