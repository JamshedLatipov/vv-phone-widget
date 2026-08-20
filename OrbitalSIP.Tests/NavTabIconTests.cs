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
            NavTabIcon.ForDialerTab(loginMode: false, inCall: false));
    }

    [Fact]
    public void CallRunningTurnsTheSlotIntoAWayBackToIt()
    {
        Assert.Equal(MaterialIconKind.PhoneInTalk,
            NavTabIcon.ForDialerTab(loginMode: false, inCall: true));
    }

    [Fact]
    public void LoginModeTurnsTheSlotIntoABackArrow()
    {
        Assert.Equal(MaterialIconKind.ArrowLeft,
            NavTabIcon.ForDialerTab(loginMode: true, inCall: false));
    }

    /// <summary>
    /// The combination the control could not previously express: it applied login mode
    /// and the call state from two different setters, so whichever ran last won and the
    /// arrow could be stranded. Login mode wins here whatever order the caller uses —
    /// a signed-out operator has no call to be taken back to.
    /// </summary>
    [Fact]
    public void LoginModeWinsOverACallStillReportedAsRunning()
    {
        Assert.Equal(MaterialIconKind.ArrowLeft,
            NavTabIcon.ForDialerTab(loginMode: true, inCall: true));
    }

    /// <summary>
    /// The tooltip is worded from the glyph rather than from the flags a second time, so
    /// these go through both calls the way the control does. The point is not the mapping
    /// on its own but that the wording cannot drift from the arrow it hangs on — a back
    /// arrow tooltipped "Dialer" was the state this replaced.
    /// </summary>
    [Theory]
    [InlineData(false, false, "Dialer")]
    [InlineData(false, true, "NavInCall")]
    [InlineData(true, false, "Back")]
    [InlineData(true, true, "Back")]
    public void TooltipWordingFollowsTheGlyph(bool loginMode, bool inCall, string expectedKey)
    {
        Assert.Equal(expectedKey,
            NavTabIcon.TooltipKeyFor(NavTabIcon.ForDialerTab(loginMode, inCall)));
    }
}
