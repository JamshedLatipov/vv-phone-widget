using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

public class DtmfPadPresenterTests
{
    [Fact]
    public void ActiveCallSendsTones()
    {
        Assert.True(DtmfPadPresenter.CanSend(onHold: false));
    }

    /// <summary>
    /// SendDtmfAsync itself no-ops while the call is on hold — the re-INVITE has taken
    /// the media path down, so no tone would reach the far end. Before this decision
    /// existed the pad did not know that, so every press taken on hold vanished with no
    /// feedback at all.
    /// </summary>
    [Fact]
    public void HeldCallBlocksTones()
    {
        Assert.False(DtmfPadPresenter.CanSend(onHold: true));
    }
}
