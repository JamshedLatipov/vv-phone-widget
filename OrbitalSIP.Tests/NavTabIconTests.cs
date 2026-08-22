using System;
using Material.Icons;
using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

public class NavTabIconTests
{
    [Fact]
    public void SignedInAndIdleShowsTheDialPad()
    {
        Assert.Equal(MaterialIconKind.Dialpad,
            NavTabIcon.ForDialerTab(loginMode: false));
    }

    [Fact]
    public void LoginModeTurnsTheSlotIntoABackArrow()
    {
        Assert.Equal(MaterialIconKind.ArrowLeft,
            NavTabIcon.ForDialerTab(loginMode: true));
    }

    /// <summary>
    /// The tooltip is worded from the glyph rather than from the flags a second time, so
    /// these go through both calls the way the control does. The point is not the mapping
    /// on its own but that the wording cannot drift from the arrow it hangs on — a back
    /// arrow tooltipped "Dialer" was the state this replaced.
    /// </summary>
    [Theory]
    [InlineData(false, "Dialer")]
    [InlineData(true, "Back")]
    public void TooltipWordingFollowsTheGlyph(bool loginMode, string expectedKey)
    {
        Assert.Equal(expectedKey,
            NavTabIcon.TooltipKeyFor(NavTabIcon.ForDialerTab(loginMode)));
    }

    /// <summary>
    /// The slot grows a third affordance the day someone adds a glyph to ForDialerTab,
    /// and the cases above cannot see it: they are parameterised over loginMode, so a new
    /// state dimension lands outside them entirely. Falling back to "Dialer" there would
    /// label the new glyph as the dial pad — the same lying tooltip this pair of methods
    /// exists to prevent.
    /// </summary>
    [Fact]
    public void AGlyphWithNoWordingFailsRatherThanGuessing()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => NavTabIcon.TooltipKeyFor(MaterialIconKind.Cog));
    }
}
