using OrbitalSIP.Models;
using Xunit;

namespace OrbitalSIP.Tests;

/// <summary>
/// Every chrome-less dialog the softphone opens can be reached from more than one
/// place — a survey from the active-call button and from the campaign auto-open on
/// answer, a script list from the active call and from the call history — and none
/// of them knew about the others. Two windows over one call is what left operators
/// staring at a softphone they could not click.
/// </summary>
public class SingleWindowGuardTests
{
    [Fact]
    public void TryBegin_AdmitsTheFirstWindow()
    {
        var guard = new SingleWindowGuard();

        Assert.True(guard.TryBegin());
        Assert.True(guard.IsOpen);
    }

    [Fact]
    public void TryBegin_RefusesASecondWindowWhileOneIsOpen()
    {
        var guard = new SingleWindowGuard();
        Assert.True(guard.TryBegin());

        Assert.False(guard.TryBegin());
        Assert.True(guard.IsOpen);
    }

    [Fact]
    public void Complete_ReleasesTheSlotForTheNextCall()
    {
        var guard = new SingleWindowGuard();
        Assert.True(guard.TryBegin());

        guard.Complete();

        Assert.False(guard.IsOpen);
        Assert.True(guard.TryBegin());
    }

    [Fact]
    public void Complete_IsIdempotentSoADoubleCloseCannotOpenASlotTwice()
    {
        var guard = new SingleWindowGuard();
        Assert.True(guard.TryBegin());

        guard.Complete();
        guard.Complete();

        Assert.True(guard.TryBegin());
        Assert.False(guard.TryBegin());
    }

    [Fact]
    public void GuardsAreIndependentSoOneDialogNeverBlocksAnother()
    {
        var surveys = new SingleWindowGuard();
        var scripts = new SingleWindowGuard();

        Assert.True(surveys.TryBegin());

        Assert.True(scripts.TryBegin());
        Assert.True(surveys.IsOpen);
    }
}
