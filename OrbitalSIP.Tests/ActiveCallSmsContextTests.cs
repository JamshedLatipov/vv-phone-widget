using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OrbitalSIP.Models;
using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

public class ActiveCallSmsContextTests
{
    [Fact]
    public void TryCreate_UsesPrimaryLinkedIdForActiveSourceAndNeverPutsDisplayNumberInRequest()
    {
        var created = ActiveCallSmsContext.TryCreate("1719990000.42", "+992 ** *** 12 34", out var context);

        Assert.True(created);
        Assert.NotNull(context);
        Assert.Equal("active", context.Source.Type);
        Assert.Equal("1719990000.42", context.Source.Id);
        Assert.Equal("+992 ** *** 12 34", context.LockedDisplayNumber);

        var request = new SendCallSmsRequest(Guid.NewGuid(), context.Source, "Test", null);
        var payload = JsonSerializer.Serialize(request);
        Assert.DoesNotContain("+992", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("phone", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryCreate_BlocksComposeWhenPrimaryLinkedIdIsMissing(string? primaryLinkedId)
    {
        var created = ActiveCallSmsContext.TryCreate(primaryLinkedId, "+992 ** *** 12 34", out var context);

        Assert.False(created);
        Assert.Null(context);
    }
}

public class PrimaryLinkedIdClientTests
{
    [Fact]
    public async Task GetPrimaryLinkedIdAsync_MapsLegacyUniqueidResponsePropertyToPrimaryLinkedId()
    {
        using var handler = new RecordingHandler(_ => JsonResponse("""{ "uniqueid": "1719990000.42" }"""));
        using var client = new HttpClient(handler);
        using var service = new ScriptService(client, () => new SipSettings
        {
            BackendUrl = "https://crm.example/",
            AccessToken = "widget-token",
        }, ownsHttpClient: false);

        var primaryLinkedId = await service.GetPrimaryLinkedIdAsync("+992 900 000 000");

        Assert.Equal("1719990000.42", primaryLinkedId);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://crm.example/api/cdr/channel-uniqueid?callerNumber=%2B992%20900%20000%20000", request.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer", request.Authorization!.Scheme);
        Assert.Equal("widget-token", request.Authorization.Parameter);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{ \"uniqueid\": null }")]
    [InlineData("{ \"uniqueid\": \"   \" }")]
    public async Task GetPrimaryLinkedIdAsync_ReturnsNullWhenPrimaryLinkedIdCannotBeVerified(string body)
    {
        using var handler = new RecordingHandler(_ => JsonResponse(body));
        using var client = new HttpClient(handler);
        using var service = new ScriptService(client, () => new SipSettings
        {
            BackendUrl = "https://crm.example/",
            AccessToken = "widget-token",
        }, ownsHttpClient: false);

        var primaryLinkedId = await service.GetPrimaryLinkedIdAsync("+992 900 000 000");

        Assert.Null(primaryLinkedId);
    }

    [Fact]
    public async Task GetPrimaryLinkedIdAsync_DoesNotPublishUntrustedLookupFailureDetails()
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("""{ "message": "internal lookup detail" }""", Encoding.UTF8, "application/json"),
        });
        using var client = new HttpClient(handler);
        using var service = new ScriptService(client, () => new SipSettings
        {
            BackendUrl = "https://crm.example/",
            AccessToken = "widget-token",
        }, ownsHttpClient: false);
        string? notification = null;
        Action<string> capture = message => notification = message;
        HttpErrorNotifier.ErrorOccurred += capture;

        try
        {
            var primaryLinkedId = await service.GetPrimaryLinkedIdAsync("+992 900 000 000");

            Assert.Null(primaryLinkedId);
            Assert.Null(notification);
        }
        finally
        {
            HttpErrorNotifier.ErrorOccurred -= capture;
        }
    }

    private static HttpResponseMessage JsonResponse(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri,
                request.Headers.Authorization,
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
            return responder(request);
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, Uri? RequestUri, System.Net.Http.Headers.AuthenticationHeaderValue? Authorization, string Body);
}
