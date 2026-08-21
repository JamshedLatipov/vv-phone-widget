using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

/// <summary>
/// Drives whole polls through the injectable constructor. Everything here was called
/// untestable once — "it needs App, an HttpClient it builds itself and a DispatcherTimer"
/// — and none of that was true: the service needed a seam, in the shape TaskService,
/// SmsService and ScriptService already had, and the two bugs the seam was supposed to be
/// unable to reach are the first two tests below.
/// </summary>
public class NavBadgeServiceTests
{
    /// <summary>
    /// A failed fetch keeps the last known figures — the panel is meant to hold a stale
    /// number rather than blink to zeros. Clearing the cache on every poll instead of only
    /// on a handover is the mutation this kills; a poll that succeeds would refill it in
    /// the same breath and hide the difference, so the assertion has to be made across a
    /// poll that fails.
    /// </summary>
    [Fact]
    public async Task RefreshNowAsync_KeepsTheCachedStatsWhenTheFetchFails()
    {
        var settings = Settings();
        var healthy = true;
        using var handler = new StubHandler(req =>
            IsDetails(req) && !healthy ? Error(HttpStatusCode.InternalServerError)
                                       : Json(BodyFor(req, missed: 4)));
        using var http = new HttpClient(handler);
        using var service = Service(http, () => settings);

        await service.RefreshNowAsync();
        Assert.Equal(4, service.OperatorStats?.MissedCalls);

        healthy = false;
        await service.RefreshNowAsync();

        Assert.Equal(4, service.OperatorStats?.MissedCalls);
    }

    /// <summary>
    /// The other direction: the same failing fetch, but the operator changed. A shared
    /// terminal handed over keeps the process, and the incoming operator must not open the
    /// dialer on the outgoing one's call figures.
    /// </summary>
    [Fact]
    public async Task RefreshNowAsync_ClearsTheCachedStatsWhenTheOperatorChanges()
    {
        var settings = Settings("alice");
        var healthy = true;
        using var handler = new StubHandler(req =>
            IsDetails(req) && !healthy ? Error(HttpStatusCode.InternalServerError)
                                       : Json(BodyFor(req, missed: 4)));
        using var http = new HttpClient(handler);
        using var service = Service(http, () => settings);

        await service.RefreshNowAsync();
        Assert.NotNull(service.OperatorStats);

        healthy = false;
        settings = Settings("bob");
        await service.RefreshNowAsync();

        Assert.Null(service.OperatorStats);
    }

    /// <summary>
    /// Two polls at once must not interleave. Ungated, the second poll runs to completion
    /// while the first is parked on its response, and the first's older total then lands
    /// last and wins — and because it is the higher of the two, the state reads it as a
    /// midnight rollover, reseats the watermark to zero and reports the whole day as
    /// unseen. Gated, the polls run in order and the newer total is the one that stands.
    ///
    /// The wait below is not a synchronisation primitive; with the gate in place nothing
    /// will ever complete it, so the correct build always runs it out and proceeds. It can
    /// only cost this test its sensitivity to the mutation, never turn correct code red.
    /// Yields rather than wall-clock time, and a 25 ms backstop rather than a long sleep:
    /// a test that idles for a third of a second widens the window in which every other
    /// collection runs beside it, and PrimaryLinkedIdClientTests asserts on a process-wide
    /// HttpErrorNotifier that any concurrent test can trip.
    /// </summary>
    [Fact]
    public async Task RefreshNowAsync_SerialisesOverlappingPollsSoTheNewerTotalWins()
    {
        var settings = Settings();
        var detailsCalls = 0;
        var firstDetailsHeld = new TaskCompletionSource();
        var secondDetailsSeen = new TaskCompletionSource();

        using var handler = new StubHandler(async req =>
        {
            if (!IsDetails(req)) return Json(BodyFor(req, missed: 0));

            var n = Interlocked.Increment(ref detailsCalls);
            if (n == 1)
            {
                await firstDetailsHeld.Task;
                return Json(BodyFor(req, missed: 10));
            }

            secondDetailsSeen.TrySetResult();
            return Json(BodyFor(req, missed: 3));
        });
        using var http = new HttpClient(handler);
        using var service = Service(http, () => settings);

        var first = service.RefreshNowAsync();
        var second = service.RefreshNowAsync();

        for (var i = 0; i < 200 && !secondDetailsSeen.Task.IsCompleted; i++) await Task.Yield();
        await Task.WhenAny(secondDetailsSeen.Task, Task.Delay(25));
        firstDetailsHeld.SetResult();
        await first;
        await second;

        Assert.Equal(3, service.NewMissed);
    }

    /// <summary>
    /// A JWT whose sub is not a user id — every Zitadel SSO token — means the tasks half
    /// cannot be asked at all. Counting that as a backend failure climbed the backoff on
    /// every poll and dragged the missed-calls half, whose endpoint was answering
    /// perfectly, down to a ten-minute refresh for the rest of the shift.
    ///
    /// Two polls, because one failure does not move the interval: PollBackoff's first step
    /// is the healthy interval itself, and only the second doubles it.
    /// </summary>
    [Fact]
    public async Task RefreshNowAsync_LeavesTheMissedBadgeAtItsHealthyCadenceWhenTheJwtHasNoAssignee()
    {
        var settings = Settings(sub: "9f8c1b2e-sso-subject");
        using var handler = new StubHandler(req => Json(BodyFor(req, missed: 2)));
        using var http = new HttpClient(handler);
        using var service = Service(http, () => settings);

        await service.RefreshNowAsync();
        await service.RefreshNowAsync();

        Assert.Equal(TimeSpan.FromMinutes(2), service.PollInterval);
        Assert.Equal(2, service.NewMissed);
        Assert.Equal(0, service.OpenTasks);

        // And it stopped asking rather than asking and swallowing the answer.
        Assert.DoesNotContain(handler.Paths, p => p.Contains("/api/tasks/stats"));
    }

    /// <summary>
    /// The half that is answering keeps its cadence; the half that is failing is what
    /// stretches the interval. Guards the reading of the test above — without this, a
    /// build that never backed off at all would pass it.
    /// </summary>
    [Fact]
    public async Task RefreshNowAsync_StretchesTheIntervalWhenTheBackendIsFailing()
    {
        var settings = Settings();
        using var handler = new StubHandler(_ => Error(HttpStatusCode.InternalServerError));
        using var http = new HttpClient(handler);
        using var service = Service(http, () => settings);

        await service.RefreshNowAsync();
        await service.RefreshNowAsync();

        Assert.Equal(TimeSpan.FromMinutes(4), service.PollInterval);
    }

    /// <summary>
    /// Stop() ends the session, and a poll already past the gate must not write into the
    /// next one. Held open at the details endpoint, stopped underneath, then released with
    /// a perfectly good answer: those are the outgoing operator's missed calls, and the
    /// session they were asked for is over, so nothing they say may land.
    ///
    /// The answer has to succeed for this to test anything. A failing one moves neither
    /// the badge nor — on a single failure — the interval, because PollBackoff's first step
    /// is the healthy interval itself, so the assertions hold whether the guard is there or
    /// not. That is exactly how the first version of this test passed against a build with
    /// the guard neutered.
    /// </summary>
    [Fact]
    public async Task Stop_DisownsAPollThatWasAlreadyInFlight()
    {
        var settings = Settings();
        var reached = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        using var handler = new StubHandler(async req =>
        {
            if (!IsDetails(req)) return Json(BodyFor(req, missed: 0));
            reached.TrySetResult();
            await release.Task;
            return Json(BodyFor(req, missed: 7));
        });
        using var http = new HttpClient(handler);
        using var service = Service(http, () => settings);

        var poll = service.RefreshNowAsync();
        await reached.Task;

        service.Stop();
        release.SetResult();
        await poll;

        Assert.Equal(0, service.NewMissed);
        Assert.Null(service.OperatorStats);
    }

    /// <summary>
    /// The mirror, so the test above cannot pass on a build that drops every poll: the same
    /// answer, nobody stopping anything, does reach the badge.
    /// </summary>
    [Fact]
    public async Task APollNobodyStoppedStillLands()
    {
        var settings = Settings();
        using var handler = new StubHandler(req => Json(BodyFor(req, missed: 7)));
        using var http = new HttpClient(handler);
        using var service = Service(http, () => settings);

        await service.RefreshNowAsync();

        Assert.Equal(7, service.NewMissed);
        Assert.Equal(7, service.OperatorStats?.MissedCalls);
    }

    // ── Harness ───────────────────────────────────────────────────────

    private static NavBadgeService Service(HttpClient http, Func<SipSettings> settings) =>
        new(http, settings, () => new TaskService(http, settings));

    private static SipSettings Settings(string operatorLogin = "alice", string sub = "42") => new()
    {
        BackendUrl = "https://crm.example/",
        AccessToken = "widget-token",
        Username = "sip-user",
        DecodedToken = new JwtPayload
        {
            Sub = sub,
            Operator = new OperatorTokenInfo { Username = operatorLogin },
        },
    };

    private static bool IsDetails(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath.Contains("/api/contact-center/operators/");

    /// <summary>The shape each endpoint answers with, so one responder can serve both.</summary>
    private static string BodyFor(HttpRequestMessage request, int missed) =>
        IsDetails(request)
            ? $$"""{ "stats": { "missedCalls": {{missed}}, "totalCalls": 7 } }"""
            : """{ "pending": 2, "inProgress": 1, "overdue": 0 }""";

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage Error(HttpStatusCode code) => new(code)
    {
        Content = new StringContent("""{ "message": "nope" }""", Encoding.UTF8, "application/json"),
    };

    /// <summary>
    /// Answers asynchronously, unlike TaskServiceTests' RecordingHandler: the serialisation
    /// test needs to hold one response open while another request arrives.
    /// </summary>
    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        private readonly List<string> _paths = [];

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            : this(req => Task.FromResult(responder(req)))
        {
        }

        public IReadOnlyList<string> Paths
        {
            get { lock (_paths) return _paths.ToList(); }
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_paths) _paths.Add(request.RequestUri!.AbsolutePath);
            return responder(request);
        }
    }
}
