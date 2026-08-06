using System;
using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

/// <summary>
/// SurveyDialog runs FlowsService calls inside a modal window over the active
/// call, so an unbounded request (HttpClient's 100s default) reads to the
/// operator as a frozen softphone. Pins the timeout short enough that a slow
/// backend surfaces as an error instead.
/// </summary>
public class FlowsServiceTests
{
    [Fact]
    public void RequestTimeout_IsBoundedWellBelowHttpClientDefault()
    {
        Assert.True(FlowsService.RequestTimeout <= TimeSpan.FromSeconds(20));
        Assert.True(FlowsService.RequestTimeout > TimeSpan.Zero);
    }

    [Fact]
    public void Constructor_AppliesRequestTimeoutToHttpClient()
    {
        using var service = new FlowsService();

        Assert.Equal(FlowsService.RequestTimeout, service.HttpClientTimeoutForTests);
    }
}
