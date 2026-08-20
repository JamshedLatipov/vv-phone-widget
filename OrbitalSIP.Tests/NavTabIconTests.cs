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
}
