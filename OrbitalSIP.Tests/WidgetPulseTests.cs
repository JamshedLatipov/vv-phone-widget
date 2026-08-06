using OrbitalSIP.Models;
using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

/// <summary>
/// The widget's ring animated permanently, which kept a transparent top-most window
/// repainting through an entire shift and made the pulse mean nothing. It now runs
/// only in the states an operator should react to.
/// </summary>
public class WidgetPulseTests
{
    [Fact]
    public void RegisteredAndNotPaused_IsTheRestingState()
    {
        Assert.False(WidgetPulse.ShouldPulse(RegistrationState.Registered, new StatusState()));
    }

    [Fact]
    public void RegisteredButPausedByTheOperator_Pulses()
    {
        var paused = new StatusState { ManualStatus = "break" };

        Assert.True(WidgetPulse.ShouldPulse(RegistrationState.Registered, paused));
    }

    [Fact]
    public void RegisteredButPausedByASupervisor_Pulses()
    {
        var paused = new StatusState { SupervisorPausedBy = 42 };

        Assert.True(WidgetPulse.ShouldPulse(RegistrationState.Registered, paused));
    }

    [Theory]
    [InlineData(RegistrationState.Unregistered)]
    [InlineData(RegistrationState.Failed)]
    [InlineData(RegistrationState.Paused)]
    public void UnhealthyRegistration_PulsesRegardlessOfQueueState(RegistrationState state)
    {
        Assert.True(WidgetPulse.ShouldPulse(state, new StatusState()));
    }

    [Fact]
    public void MissingQueueState_IsTreatedAsNotPaused()
    {
        Assert.False(WidgetPulse.ShouldPulse(RegistrationState.Registered, null));
    }
}
