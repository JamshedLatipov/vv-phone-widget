using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

public class DtmfPadPresenterTests
{
    [Fact]
    public void ActiveCallSendsTones()
    {
        Assert.True(DtmfPadPresenter.CanSend(CallState.Active));
    }

    /// <summary>
    /// SendDtmfAsync itself sends only when its own state snapshot is exactly Active, so
    /// every other state has to block here too — not just OnHold. Idle, Ringing and
    /// IncomingRinging are the ones a boolean "on hold" gate got wrong: none of them are
    /// on hold, so that gate waved every one of them through. Ringing is the one that
    /// actually bit — StartOutgoingCall puts a full ActiveCallView on screen, keys live,
    /// before CallAsync has moved the call past Idle, and nothing repaints the pad again
    /// until Active. A press taken in that window used to echo a digit that was never
    /// sent.
    /// </summary>
    [Theory]
    [InlineData(CallState.Idle)]
    [InlineData(CallState.Ringing)]
    [InlineData(CallState.IncomingRinging)]
    [InlineData(CallState.OnHold)]
    public void EveryOtherStateBlocksTones(CallState state)
    {
        Assert.False(DtmfPadPresenter.CanSend(state));
    }

    [Fact]
    public void ActiveCallNeedsNoHint()
    {
        Assert.Null(DtmfPadPresenter.HintKey(CallState.Active));
    }

    [Fact]
    public void HoldGetsItsOwnHint()
    {
        Assert.Equal("DtmfHoldHint", DtmfPadPresenter.HintKey(CallState.OnHold));
    }

    /// <summary>
    /// "Take the call off hold" is wrong advice before the call has been answered — there
    /// is no hold to take it off. Ringing (the outgoing case that bit) and
    /// IncomingRinging both need the other wording, same as Idle.
    /// </summary>
    [Theory]
    [InlineData(CallState.Idle)]
    [InlineData(CallState.Ringing)]
    [InlineData(CallState.IncomingRinging)]
    public void NotYetAnsweredGetsItsOwnHint(CallState state)
    {
        Assert.Equal("DtmfNotAnsweredHint", DtmfPadPresenter.HintKey(state));
    }
}
