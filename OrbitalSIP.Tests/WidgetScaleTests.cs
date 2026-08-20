using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

/// <summary>
/// The views are laid out at fixed logical sizes chosen against a 1080p screen, and
/// Windows only stretches them when the display carries a DPI scaling factor — which
/// most of the machines in the field do not. WidgetScale is what keeps a 1366x768
/// laptop from getting a panel that eats the screen, and a 4K monitor from getting one
/// the operator has to lean in to read.
/// </summary>
public class WidgetScaleTests
{
    // ── Auto ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(768,  0.75)]   // 1366x768 laptop — the complaint this feature came from
    [InlineData(800,  1.00)]
    [InlineData(1080, 1.00)]
    [InlineData(1440, 1.25)]
    [InlineData(2160, 1.50)]
    public void AutoFactor_StepsWithTheScreenHeight(double logicalHeight, double expected)
    {
        Assert.Equal(expected, WidgetScale.AutoFactor(logicalHeight));
    }

    [Fact]
    public void Auto_OnATypicalLaptop_ShrinksTheLayout()
    {
        Assert.Equal(0.75, WidgetScale.Resolve(WidgetScale.Auto, 1366, 768));
    }

    [Fact]
    public void Auto_OnAnUnscaled4KScreen_GrowsTheLayout()
    {
        Assert.Equal(1.50, WidgetScale.Resolve(WidgetScale.Auto, 3840, 2160));
    }

    [Fact]
    public void Auto_OnA4KScreenWindowsAlreadyScales_LeavesItAlone()
    {
        // 3840x2160 at 200% DPI reaches Avalonia as 1920x1080 logical units. Scaling that
        // a second time is the oversized widget, not the fix for it.
        Assert.Equal(1.00, WidgetScale.Resolve(WidgetScale.Auto, 1920, 1080));
    }

    // ── Explicit choice ───────────────────────────────────────────────

    [Fact]
    public void AnExplicitPercentage_IsUsedAsGiven()
    {
        Assert.Equal(1.25, WidgetScale.Resolve(125, 1920, 1080));
    }

    [Fact]
    public void AnExplicitPercentageTooLargeForTheScreen_IsCappedToWhatFits()
    {
        // 600 px of panel at 150% is 900, past the bottom of a 768-tall work area — and a
        // chrome-less window pushed off the edge has no title bar to drag it back by.
        var factor = WidgetScale.Resolve(150, 1366, 768);

        Assert.True(factor < 1.5);
        Assert.True(600 * factor <= 768 * 0.85);
    }

    [Fact]
    public void AScreenTooSmallForEvenTheSmallestLayout_StopsAtTheFloor()
    {
        Assert.Equal(WidgetScale.MinPercent / 100.0, WidgetScale.Resolve(100, 320, 240));
    }

    [Fact]
    public void APercentageOutsideTheSupportedRange_IsClamped()
    {
        Assert.Equal(WidgetScale.MaxPercent / 100.0, WidgetScale.Resolve(500, 7680, 4320));
        Assert.Equal(WidgetScale.MinPercent / 100.0, WidgetScale.Resolve(1, 1920, 1080));
    }

    [Fact]
    public void NoScreenMeasuredYet_TakesTheSettingAtFaceValue()
    {
        Assert.Equal(1.25, WidgetScale.Resolve(125, 0, 0));
    }

    // ── Settings combo mapping ────────────────────────────────────────

    [Fact]
    public void EveryOfferedChoiceRoundTripsThroughTheCombo()
    {
        foreach (var percent in WidgetScale.Choices)
            Assert.Equal(percent, WidgetScale.FromListPosition(WidgetScale.ListPosition(percent)));
    }

    [Fact]
    public void AutoIsTheFirstRow()
    {
        Assert.Equal(0, WidgetScale.ListPosition(WidgetScale.Auto));
        Assert.Equal(WidgetScale.Auto, WidgetScale.FromListPosition(0));
    }

    [Fact]
    public void APercentageNoLongerOffered_ReadsAsAutoRatherThanSelectingNothing()
    {
        // A settings file written by a build with a different set of steps, or edited by
        // hand. Avalonia answers an out-of-range row with -1, the screen forces that to 0,
        // and the next save would write whatever row 0 happens to be.
        Assert.Equal(0, WidgetScale.ListPosition(137));
    }

    [Fact]
    public void ARowOutsideTheListSavesAsAuto()
    {
        Assert.Equal(WidgetScale.Auto, WidgetScale.FromListPosition(-1));
        Assert.Equal(WidgetScale.Auto, WidgetScale.FromListPosition(99));
    }
}
