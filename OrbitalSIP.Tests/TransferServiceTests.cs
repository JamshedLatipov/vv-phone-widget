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
    public async Task TransferAsync_TreatsAMissingBackendConfigurationAsChannelUnresolved()
    {
        // No backend configured at all is squarely "could not ask" — REFER
        // needs no backend, so this must not be Failed (which offers no
        // fallback), matching TransferOutcome's own contract in TransferModels.cs.
        using var handler = new RecordingHandler(_ => JsonResponse("""{ "ok": true }"""));
        using var client = new HttpClient(handler);
        using var service = new TransferService(
            client,
            () => new SipSettings { BackendUrl = "", AccessToken = "" },
            ownsHttpClient: false);

        var result = await service.TransferAsync(
            TransferTargetKind.Extension, "1042", "+992900000000", CancellationToken.None);

        Assert.Equal(TransferOutcome.ChannelUnresolved, result.Outcome);
    }

    [Fact]
    public async Task TransferAsync_TreatsAThrowingSettingsProviderAsChannelUnresolved()
    {
        // A settings read failing happens before backendUrl/accessToken are
        // even known — strictly before any request could be built, let
        // alone sent. Exactly as "could not ask" as no-backend-configured.
        var captured = new List<HttpRequestMessage>();
        using var handler = new RecordingHandler(request =>
        {
            captured.Add(request);
            return JsonResponse("""{ "ok": true }""");
        });
        using var client = new HttpClient(handler);
        using var service = new TransferService(
            client,
            () => throw new InvalidOperationException("settings unavailable"),
            ownsHttpClient: false);

        var result = await service.TransferAsync(
            TransferTargetKind.Extension, "1042", "+992900000000", CancellationToken.None);

        Assert.Equal(TransferOutcome.ChannelUnresolved, result.Outcome);
        Assert.Empty(captured);
    }

    [Fact]
    public async Task TransferAsync_TreatsA5xxFromTheLookupAsChannelUnresolved()
    {
        using var handler = new RecordingHandler(request =>
            request.RequestUri!.AbsolutePath.Contains("channel-uniqueid")
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("boom", Encoding.UTF8, "text/plain"),
                }
                : JsonResponse("""{ "ok": true }"""));
        using var client = new HttpClient(handler);
        using var service = new TransferService(client, Settings, ownsHttpClient: false);

        var result = await service.TransferAsync(
            TransferTargetKind.Extension, "1042", "+992900000000", CancellationToken.None);

        Assert.Equal(TransferOutcome.ChannelUnresolved, result.Outcome);
    }

    [Fact]
    public async Task TransferAsync_TreatsAMalformedLookupBodyAsChannelUnresolved()
    {
        using var handler = new RecordingHandler(request =>
            request.RequestUri!.AbsolutePath.Contains("channel-uniqueid")
                ? JsonResponse("not json")
                : JsonResponse("""{ "ok": true }"""));
        using var client = new HttpClient(handler);
        using var service = new TransferService(client, Settings, ownsHttpClient: false);

        var result = await service.TransferAsync(
            TransferTargetKind.Extension, "1042", "+992900000000", CancellationToken.None);

        Assert.Equal(TransferOutcome.ChannelUnresolved, result.Outcome);
    }

    [Fact]
    public async Task TransferAsync_TreatsANonStringUniqueIdAsChannelUnresolved()
    {
        // uniqueid sent as a number (or any non-string) must not throw
        // InvalidOperationException out of GetString() and land in Failed —
        // it is exactly as unusable as a missing uniqueid.
        using var handler = new RecordingHandler(request =>
            request.RequestUri!.AbsolutePath.Contains("channel-uniqueid")
                ? JsonResponse("""{ "uniqueid": 12345 }""")
                : JsonResponse("""{ "ok": true }"""));
        using var client = new HttpClient(handler);
        using var service = new TransferService(client, Settings, ownsHttpClient: false);

        var result = await service.TransferAsync(
            TransferTargetKind.Extension, "1042", "+992900000000", CancellationToken.None);

        Assert.Equal(TransferOutcome.ChannelUnresolved, result.Outcome);
    }

    [Fact]
    public async Task TransferAsync_KeepsAPostPhaseServerErrorAsFailedNotChannelUnresolved()
    {
        // Once the channel IS resolved, a broken transfer POST is "was told
        // no" territory, not "could not ask" — the backend answered this
        // request, so it stays Failed rather than risking a REFER racing
        // whatever the backend did before it errored.
        using var handler = new RecordingHandler(request =>
            request.RequestUri!.AbsolutePath.Contains("channel-uniqueid")
                ? JsonResponse("""{ "uniqueid": "1719990000.42" }""")
                : new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("boom", Encoding.UTF8, "text/plain"),
                });
        using var client = new HttpClient(handler);
        using var service = new TransferService(client, Settings, ownsHttpClient: false);

        var result = await service.TransferAsync(
            TransferTargetKind.Extension, "1042", "+992900000000", CancellationToken.None);

        Assert.Equal(TransferOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task TransferAsync_PropagatesCancellationRatherThanReportingFailed()
    {
        using var handler = new RecordingHandler(_ => JsonResponse("""{ "uniqueid": "1719990000.42" }"""));
        using var client = new HttpClient(handler);
        using var service = new TransferService(client, Settings, ownsHttpClient: false);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.TransferAsync(TransferTargetKind.Extension, "1042", "+992900000000", cts.Token));
    }

    [Fact]
    public async Task TransferAsync_TreatsADeadlineTimeoutDuringTheLookupAsChannelUnresolved()
    {
        // Nothing ever answers, so the shortened deadline is what ends this —
        // simulates the backend not responding to the channel lookup in time.
        using var handler = new DelayedHandler(_ => null);
        using var client = new HttpClient(handler);
        using var service = new ShortDeadlineTransferService(client, Settings);

        var result = await service.TransferAsync(
            TransferTargetKind.Extension, "1042", "+992900000000", CancellationToken.None);

        Assert.Equal(TransferOutcome.ChannelUnresolved, result.Outcome);
    }

    [Fact]
    public async Task TransferAsync_TreatsADeadlineTimeoutDuringThePostAsFailedNotChannelUnresolved()
    {
        // The lookup answers immediately; only the transfer POST hangs, so
        // the shortened deadline fires while the backend may already have
        // the request in hand — this must not license a REFER on top of it.
        using var handler = new DelayedHandler(request =>
            request.RequestUri!.AbsolutePath.Contains("channel-uniqueid")
                ? JsonResponse("""{ "uniqueid": "1719990000.42" }""")
                : null);
        using var client = new HttpClient(handler);
        using var service = new ShortDeadlineTransferService(client, Settings);

        var result = await service.TransferAsync(
            TransferTargetKind.Extension, "1042", "+992900000000", CancellationToken.None);

        Assert.Equal(TransferOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task TransferAsync_PropagatesCallerCancellationMidPostRatherThanReportingEitherOutcome()
    {
        // The lookup answers immediately; the POST hangs so there is time
        // for the caller's own token to fire while it is in flight, with
        // the full ten-second shared deadline nowhere near elapsing. Confirms
        // the linked token source does not swallow or relabel a genuine
        // caller cancellation as ChannelUnresolved or Failed — the operator
        // abandoning the panel is not a transfer result either way.
        using var handler = new DelayedHandler(request =>
            request.RequestUri!.AbsolutePath.Contains("channel-uniqueid")
                ? JsonResponse("""{ "uniqueid": "1719990000.42" }""")
                : null);
        using var client = new HttpClient(handler);
        using var service = new TransferService(client, Settings, ownsHttpClient: false);
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.TransferAsync(TransferTargetKind.Extension, "1042", "+992900000000", cts.Token));
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

    [Fact]
    public async Task GetTargetsAsync_PropagatesCancellationRatherThanReportingAFailedLoad()
    {
        using var handler = new RecordingHandler(_ => JsonResponse("""{ "operators": [], "queues": [] }"""));
        using var client = new HttpClient(handler);
        using var service = new TransferService(client, Settings, ownsHttpClient: false);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetTargetsAsync(cts.Token));
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

    /// <summary>
    /// A handler where any request the responder declines (returns null
    /// for) hangs until its token cancels, instead of completing on its
    /// own. Lets a test put the shared deadline in the middle of a
    /// specific phase without waiting out the real ten-second RequestTimeout.
    /// </summary>
    private sealed class DelayedHandler(Func<HttpRequestMessage, HttpResponseMessage?> responder) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = responder(request);
            if (response != null)
                return response;

            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable — Task.Delay(Infinite) only returns via cancellation");
        }
    }

    /// <summary>Shrinks TransferDeadline so a test can reach the shared-deadline catch clauses fast.</summary>
    private sealed class ShortDeadlineTransferService(HttpClient httpClient, Func<SipSettings> settingsProvider)
        : TransferService(httpClient, settingsProvider, ownsHttpClient: false)
    {
        // 250ms, not something smaller: this is still wall clock racing the
        // test host, and this suite has a flakiness history (see
        // AssemblyInfo.cs) that cost a separate fix to close. Neither
        // DelayedHandler request can self-resolve and test-class
        // parallelism is off assembly-wide, so nothing real races this
        // timer — the margin is headroom for a loaded machine, not slack to
        // be trimmed back down.
        protected override TimeSpan TransferDeadline => TimeSpan.FromMilliseconds(250);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(responder(request));
        }
    }
}
