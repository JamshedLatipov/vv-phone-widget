using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OrbitalSIP.Models;
using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

/// <summary>
/// A transfer is two calls, not one: the channel id is resolved first and the
/// move is requested second. Swap or skip the first and the backend gets a
/// request naming no live call, and answers with a refusal the operator cannot
/// act on. The refusal also arrives as HTTP 200 with `ok:false`, so a service
/// that only checks the status code reports a transfer that never happened.
/// </summary>
public class TransferServiceTests
{
    [Fact]
    public void ParseTargets_ReadsBothListsAndTheDisabledReason()
    {
        var targets = TransferService.ParseTargets("""
        {
          "operators": [{ "extension": "1042", "fullName": "Иванов" }],
          "queues": [
            { "name": "sales", "description": "Продажи", "disabledReason": null },
            { "name": "Отдел продаж", "description": null, "disabledReason": "unsafe-name" }
          ]
        }
        """);

        Assert.NotNull(targets);
        Assert.Equal("1042", targets!.Operators![0].Extension);
        Assert.Null(targets.Queues![0].DisabledReason);
        Assert.Equal("unsafe-name", targets.Queues[1].DisabledReason);
    }

    [Fact]
    public void ParseTargets_ReturnsNullOnGarbageRatherThanThrowing()
    {
        Assert.Null(TransferService.ParseTargets("not json"));
    }

    [Fact]
    public void ParseTransferResult_TreatsOkFalseAsFailureDespiteHttp200()
    {
        var result = TransferService.ParseTransferResult("""{ "ok": false, "error": "Unknown queue" }""");

        Assert.Equal(TransferOutcome.Failed, result.Outcome);
        Assert.Equal("Unknown queue", result.Error);
    }

    [Fact]
    public void ParseTransferResult_ReadsSuccess()
    {
        Assert.Equal(TransferOutcome.Ok, TransferService.ParseTransferResult("""{ "ok": true }""").Outcome);
    }

    [Fact]
    public async Task TransferAsync_ResolvesTheChannelBeforePosting()
    {
        var captured = new List<HttpRequestMessage>();
        using var handler = new RecordingHandler(request =>
        {
            captured.Add(request);
            return request.RequestUri!.AbsolutePath.Contains("channel-uniqueid")
                ? JsonResponse("""{ "uniqueid": "1719990000.42" }""")
                : JsonResponse("""{ "ok": true }""");
        });
        using var client = new HttpClient(handler);
        using var service = new TransferService(client, Settings, ownsHttpClient: false);

        var result = await service.TransferAsync(
            TransferTargetKind.Extension, "1042", "+992900000000", CancellationToken.None);

        Assert.Equal(TransferOutcome.Ok, result.Outcome);
        Assert.Equal(2, captured.Count);
        Assert.Contains("channel-uniqueid", captured[0].RequestUri!.AbsoluteUri);
        Assert.Contains("/api/calls/transfer", captured[1].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task TransferAsync_SendsTheQueueKindOnTheWire()
    {
        string? body = null;
        using var handler = new RecordingHandler(request =>
        {
            if (!request.RequestUri!.AbsolutePath.Contains("channel-uniqueid"))
                body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return request.RequestUri.AbsolutePath.Contains("channel-uniqueid")
                ? JsonResponse("""{ "uniqueid": "1719990000.42" }""")
                : JsonResponse("""{ "ok": true }""");
        });
        using var client = new HttpClient(handler);
        using var service = new TransferService(client, Settings, ownsHttpClient: false);

        await service.TransferAsync(
            TransferTargetKind.Queue, "sales", "+992900000000", CancellationToken.None);

        Assert.NotNull(body);
        Assert.Contains("\"targetKind\":\"queue\"", body);
        Assert.Contains("\"target\":\"sales\"", body);
        Assert.Contains("\"channelId\":\"1719990000.42\"", body);
    }

    [Fact]
    public async Task TransferAsync_ReportsAnUnresolvedChannelDistinctlySoTheCallerCanFallBack()
    {
        using var handler = new RecordingHandler(_ => JsonResponse("""{ }"""));
        using var client = new HttpClient(handler);
        using var service = new TransferService(client, Settings, ownsHttpClient: false);

        var result = await service.TransferAsync(
            TransferTargetKind.Extension, "1042", "+992900000000", CancellationToken.None);

        Assert.Equal(TransferOutcome.ChannelUnresolved, result.Outcome);
    }

    [Fact]
    public async Task TransferAsync_DoesNotPostWhenTheChannelIsUnresolved()
    {
        var captured = new List<HttpRequestMessage>();
        using var handler = new RecordingHandler(request =>
        {
            captured.Add(request);
            return JsonResponse("""{ }""");
        });
        using var client = new HttpClient(handler);
        using var service = new TransferService(client, Settings, ownsHttpClient: false);

        await service.TransferAsync(
            TransferTargetKind.Extension, "1042", "+992900000000", CancellationToken.None);

        Assert.Single(captured);
    }

    [Fact]
    public async Task GetTargetsAsync_SurfacesForbiddenSeparatelyFromAFailedLoad()
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        });
        using var client = new HttpClient(handler);
        using var service = new TransferService(client, Settings, ownsHttpClient: false);

        var response = await service.GetTargetsAsync(CancellationToken.None);

        Assert.True(response.Forbidden);
        Assert.Null(response.Targets);
    }

    [Fact]
    public async Task GetTargetsAsync_TreatsAMissingEndpointAsForbidden()
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        });
        using var client = new HttpClient(handler);
        using var service = new TransferService(client, Settings, ownsHttpClient: false);

        var response = await service.GetTargetsAsync(CancellationToken.None);

        Assert.True(response.Forbidden);
    }

    private static Func<SipSettings> Settings => () => new SipSettings
    {
        BackendUrl = "https://crm.example/",
        AccessToken = "widget-token",
    };

    private static HttpResponseMessage JsonResponse(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(responder(request));
        }
    }
}
