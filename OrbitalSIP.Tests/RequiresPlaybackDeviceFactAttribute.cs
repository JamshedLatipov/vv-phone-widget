using OrbitalSIP.Services.Audio;
using Xunit;

namespace OrbitalSIP.Tests;

/// <summary>
/// Marks a test that can only observe what it claims to when the machine actually has a
/// waveOut playback device.
///
/// Without one, <c>GainAudioEndPoint</c> degrades to a closed sink and
/// <c>IsPlaybackDeviceOpen</c> is false no matter what the code under test does — so the
/// handle-lifecycle tests asserted false == false and stayed green with the leak fully
/// restored. That is worse than no coverage: it was the regression guard for the defect
/// behind the one-way-audio reports, and on a headless build agent it proved nothing while
/// reading as proof.
///
/// Skipping says so out loud in the run summary instead.
/// </summary>
public sealed class RequiresPlaybackDeviceFactAttribute : FactAttribute
{
    public RequiresPlaybackDeviceFactAttribute()
    {
        if (WaveOutDevices.Count == 0)
        {
            Skip = "No waveOut playback device on this machine — this test cannot observe the device lifecycle.";
        }
    }
}
