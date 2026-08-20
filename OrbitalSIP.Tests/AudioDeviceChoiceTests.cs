using OrbitalSIP.Models;
using Xunit;

namespace OrbitalSIP.Tests;

/// <summary>
/// Mapping between the saved waveOut/waveIn device index and its row in the Settings
/// combo box.
///
/// The combo is ["System Default", device 0, device 1, ...], so the saved index and the
/// list position differ by one — and the settings screen did that arithmetic in two
/// places with no agreement about what happens when the saved device is not plugged in
/// right now. A stale index produced an out-of-range position, Avalonia answered -1, the
/// code forced it to 0, and the next save wrote "System Default" over the operator's
/// choice. Unplugging a headset and then changing the interface language was enough to
/// lose it permanently.
/// </summary>
public class AudioDeviceChoiceTests
{
    [Fact]
    public void SystemDefaultIsTheFirstRow()
    {
        Assert.Equal(0, AudioDeviceChoice.ListPosition(savedIndex: -1, deviceCount: 3));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    public void ALiveDeviceSitsOneRowBelowItsIndex(int savedIndex, int expectedPosition)
    {
        Assert.Equal(expectedPosition, AudioDeviceChoice.ListPosition(savedIndex, deviceCount: 3));
    }

    [Fact]
    public void AnAbsentDeviceGetsItsOwnRowAfterTheLiveOnes()
    {
        // Saved device 5, only three present: rows 0..3 are taken, so the placeholder is 4.
        Assert.Equal(4, AudioDeviceChoice.ListPosition(savedIndex: 5, deviceCount: 3));
    }

    [Fact]
    public void ReportsWhetherTheSavedDeviceIsPresent()
    {
        Assert.False(AudioDeviceChoice.IsMissing(savedIndex: -1, deviceCount: 3));
        Assert.False(AudioDeviceChoice.IsMissing(savedIndex: 2, deviceCount: 3));
        Assert.True(AudioDeviceChoice.IsMissing(savedIndex: 3, deviceCount: 3));
        Assert.True(AudioDeviceChoice.IsMissing(savedIndex: 0, deviceCount: 0));
    }

    [Fact]
    public void SelectingSystemDefaultSavesMinusOne()
    {
        Assert.Equal(-1, AudioDeviceChoice.SavedIndex(listPosition: 0, deviceCount: 3, savedIndexBefore: 2));
    }

    [Fact]
    public void SelectingALiveDeviceSavesItsIndex()
    {
        Assert.Equal(1, AudioDeviceChoice.SavedIndex(listPosition: 2, deviceCount: 3, savedIndexBefore: -1));
    }

    /// <summary>
    /// The regression. Leaving the placeholder row selected means the operator did not
    /// touch the setting, so the absent device must survive the save.
    /// </summary>
    [Fact]
    public void LeavingTheAbsentDeviceSelectedKeepsIt()
    {
        var position = AudioDeviceChoice.ListPosition(savedIndex: 5, deviceCount: 3);

        Assert.Equal(5, AudioDeviceChoice.SavedIndex(position, deviceCount: 3, savedIndexBefore: 5));
    }

    /// <summary>
    /// A machine that reports no playback devices at all still must not erase the choice —
    /// that is a driver that has not come up yet, not an operator changing their mind.
    /// </summary>
    [Fact]
    public void KeepsTheChoiceWhenNoDevicesAreEnumeratedAtAll()
    {
        var position = AudioDeviceChoice.ListPosition(savedIndex: 2, deviceCount: 0);

        Assert.Equal(2, AudioDeviceChoice.SavedIndex(position, deviceCount: 0, savedIndexBefore: 2));
    }

    /// <summary>Anything the UI cannot explain falls back to System Default rather than a wild index.</summary>
    [Fact]
    public void AnUnselectedComboFallsBackToSystemDefault()
    {
        Assert.Equal(-1, AudioDeviceChoice.SavedIndex(listPosition: -1, deviceCount: 3, savedIndexBefore: 2));
    }

    /// <summary>
    /// The property that actually matters: opening Settings and saving without touching
    /// the audio section never changes what is stored, whatever is plugged in.
    /// </summary>
    [Theory]
    [InlineData(-1, 3)]
    [InlineData(0, 3)]
    [InlineData(2, 3)]
    [InlineData(5, 3)]
    [InlineData(0, 0)]
    [InlineData(7, 0)]
    public void RoundTripsEverySavedIndex(int savedIndex, int deviceCount)
    {
        var position = AudioDeviceChoice.ListPosition(savedIndex, deviceCount);

        Assert.Equal(savedIndex, AudioDeviceChoice.SavedIndex(position, deviceCount, savedIndexBefore: savedIndex));
    }
}
