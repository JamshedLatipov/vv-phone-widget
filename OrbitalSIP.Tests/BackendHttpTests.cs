using System;
using System.Net.Http;
using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

/// <summary>
/// Every backend caller now shares one connection pool. The failure that would hurt
/// is a service disposing the client out from under everyone else, so that is what
/// these check: constructing and disposing a service must leave the shared client
/// usable for the next one.
/// </summary>
public class BackendHttpTests
{
    /// <summary>
    /// Setting Timeout runs a disposed check first. A live client either accepts the
    /// assignment or, once it has sent something, rejects it as already started —
    /// only a disposed one reports itself disposed.
    /// </summary>
    private static bool IsDisposed(HttpClient client)
    {
        try
        {
            client.Timeout = client.Timeout;
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    [Fact]
    public void SharedClientIsOneInstance()
    {
        Assert.Same(BackendHttp.Client, BackendHttp.Client);
    }

    [Fact]
    public void CreateClientCarriesTheRequestedTimeout()
    {
        using var client = BackendHttp.CreateClient(TimeSpan.FromSeconds(20));

        Assert.Equal(TimeSpan.FromSeconds(20), client.Timeout);
        Assert.NotSame(BackendHttp.Client, client);
    }

    [Fact]
    public void DisposingAClientFromCreateClientLeavesTheSharedOneAlive()
    {
        BackendHttp.CreateClient(TimeSpan.FromSeconds(5)).Dispose();

        Assert.False(IsDisposed(BackendHttp.Client));
    }

    [Fact]
    public void DisposingACallInfoServiceLeavesTheSharedClientAlive()
    {
        new CallInfoService().Dispose();

        Assert.False(IsDisposed(BackendHttp.Client));
    }

    [Fact]
    public void DisposingALeadServiceLeavesTheSharedClientAlive()
    {
        new LeadService().Dispose();

        Assert.False(IsDisposed(BackendHttp.Client));
    }

    [Fact]
    public void DisposingATaskServiceLeavesTheSharedClientAlive()
    {
        new TaskService().Dispose();

        Assert.False(IsDisposed(BackendHttp.Client));
    }

    [Fact]
    public void DisposingAScriptServiceLeavesTheSharedClientAlive()
    {
        new ScriptService().Dispose();

        Assert.False(IsDisposed(BackendHttp.Client));
    }

    [Fact]
    public void DisposingAFlowsServiceLeavesTheSharedClientAlive()
    {
        // FlowsService owns its client for the shorter timeout but not the handler.
        new FlowsService().Dispose();

        Assert.False(IsDisposed(BackendHttp.Client));
    }

    /// <summary>
    /// SmsService used to build and own its own HttpClient. Now that it shares the pool
    /// like everyone else, it also has to stop disposing it — this is precisely the
    /// regression that would take every other service down with it.
    /// </summary>
    [Fact]
    public void DisposingAnSmsServiceLeavesTheSharedClientAlive()
    {
        new SmsService().Dispose();

        Assert.False(IsDisposed(BackendHttp.Client));
    }

    // ── Insecure-transport detection ────────────────────────────────────────

    [Theory]
    [InlineData("http://10.10.103.46")]
    [InlineData("http://crm.internal/api")]
    [InlineData("HTTP://CRM.INTERNAL")]
    [InlineData("  http://crm.internal")]   // pasted into the settings box with whitespace
    public void PlainHttpBackendIsInsecure(string url)
    {
        Assert.True(BackendHttp.IsInsecure(url));
    }

    /// <summary>
    /// «https» starts with «http», so a naive prefix test would flag every secure backend
    /// and train the operator to ignore the warning.
    /// </summary>
    [Theory]
    [InlineData("https://crm.internal")]
    [InlineData("HTTPS://crm.internal")]
    [InlineData("https://crm.internal/api/v1")]
    public void HttpsBackendIsNotInsecure(string url)
    {
        Assert.False(BackendHttp.IsInsecure(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UnconfiguredBackendIsNotReportedAsInsecure(string? url)
    {
        Assert.False(BackendHttp.IsInsecure(url));
    }
}
