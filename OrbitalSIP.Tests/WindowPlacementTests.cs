using Avalonia;
using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

/// <summary>
/// SurveyDialog is SystemDecorations="None", so its own header bar is the only
/// drag handle it has. CenterOwner over the softphone widget — which operators
/// park against a screen edge — could push that header off-screen, leaving the
/// window unreachable. Placement is clamped into the working area instead.
/// </summary>
public class WindowPlacementTests
{
    private static readonly PixelRect Screen = new(0, 0, 1920, 1080);

    [Fact]
    public void WindowAlreadyInside_IsLeftWhereItIs()
    {
        var placed = WindowPlacement.ClampToWorkingArea(new PixelRect(400, 200, 420, 560), Screen);

        Assert.Equal(new PixelPoint(400, 200), placed);
    }

    [Fact]
    public void HeaderPushedAboveTheTop_IsPulledBackOnScreen()
    {
        var placed = WindowPlacement.ClampToWorkingArea(new PixelRect(400, -260, 420, 560), Screen);

        Assert.Equal(new PixelPoint(400, 0), placed);
    }

    [Fact]
    public void WindowRunningOffTheRightEdge_IsPulledLeftUntilItFits()
    {
        var placed = WindowPlacement.ClampToWorkingArea(new PixelRect(1800, 200, 420, 560), Screen);

        Assert.Equal(new PixelPoint(1500, 200), placed);
    }

    [Fact]
    public void WindowRunningOffTheBottomEdge_IsPulledUpUntilItFits()
    {
        var placed = WindowPlacement.ClampToWorkingArea(new PixelRect(400, 900, 420, 560), Screen);

        Assert.Equal(new PixelPoint(400, 520), placed);
    }

    [Fact]
    public void WorkingAreaOffsetByATaskbar_ClampsToThatOriginNotToZero()
    {
        var workingArea = new PixelRect(0, 40, 1920, 1000);

        var placed = WindowPlacement.ClampToWorkingArea(new PixelRect(400, -260, 420, 560), workingArea);

        Assert.Equal(new PixelPoint(400, 40), placed);
    }

    [Fact]
    public void WindowTallerThanTheScreen_PinsToTheOriginSoTheHeaderStaysReachable()
    {
        var placed = WindowPlacement.ClampToWorkingArea(new PixelRect(400, 300, 420, 1400), Screen);

        Assert.Equal(new PixelPoint(400, 0), placed);
    }
}
