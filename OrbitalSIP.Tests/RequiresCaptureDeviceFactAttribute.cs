using NAudio.Wave;
using Xunit;

namespace OrbitalSIP.Tests;

/// <summary>
/// Marks a test that can only observe what it claims to when the machine actually has a
/// waveIn capture device.
///
/// The send-path tests assert that microphone frames keep being encoded. Without a
/// microphone nothing ever calls the capture callback, so the counter stays at zero for the
/// same reason a broken build would leave it at zero — and a test that cannot tell those two
/// apart reads as proof while proving nothing. Skipping says so out loud instead.
/// </summary>
public sealed class RequiresCaptureDeviceFactAttribute : FactAttribute
{
    public RequiresCaptureDeviceFactAttribute()
    {
        if (WaveInEvent.DeviceCount == 0)
        {
            Skip = "No waveIn capture device on this machine — this test cannot observe the send path.";
        }
    }
}
