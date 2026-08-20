using OrbitalSIP.Models;
using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

public class NavPulseTests
{
    [Theory]
    [InlineData(NavTab.Recents)]
    [InlineData(NavTab.Tasks)]
    [InlineData(NavTab.Settings)]
    public void CallRunningOffScreenDrawsTheOperatorBack(NavTab currentTab)
    {
        Assert.True(NavPulse.ShouldPulse(inCall: true, currentTab));
    }

    /// <summary>
    /// The call screen is already in front of the operator, so there is nothing to draw
    /// their eye to. Animating anyway would repaint a transparent topmost window for the
    /// length of every call, which is the cost WidgetPulse exists to avoid.
    /// </summary>
    [Fact]
    public void CallScreenItselfDoesNotPulse()
    {
        Assert.False(NavPulse.ShouldPulse(inCall: true, NavTab.Dialer));
    }

    [Theory]
    [InlineData(NavTab.Dialer)]
    [InlineData(NavTab.Recents)]
    [InlineData(NavTab.Tasks)]
    [InlineData(NavTab.Settings)]
    public void NoCallNeverPulses(NavTab currentTab)
    {
        Assert.False(NavPulse.ShouldPulse(inCall: false, currentTab));
    }
}
