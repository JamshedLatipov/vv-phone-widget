using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OrbitalSIP.Models;
using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

public class TaskServiceTests
{
    [Fact]
    public async Task GetTaskTypesAsync_FiltersInactiveAndOrdersBySortOrderThenName()
    {
        using var handler = new RecordingHandler(_ => JsonResponse("""
            [
              { "id": 3, "name": "Б", "sortOrder": 1, "isActive": true },
              { "id": 1, "name": "А", "sortOrder": 1, "isActive": true },
              { "id": 2, "name": "Скрытый", "sortOrder": 0, "isActive": false },
              { "id": 4, "name": "В", "sortOrder": 2, "isActive": true }
            ]
            """));
        using var service = CreateService(handler);

        var types = await service.GetTaskTypesAsync();

        // id 2 is inactive and must be gone; the two sortOrder-1 survivors (3 then 1 in
        // the wire order) must come back by Name, not by their original position.
        Assert.Equal(new[] { 1, 3, 4 }, types.Select(t => t.Id));
    }

    [Fact]
    public async Task GetTaskTypesAsync_ReturnsEmptyListRatherThanNullOnFailure()
    {
        using var handler = new RecordingHandler(_ => ErrorResponse(HttpStatusCode.InternalServerError));
        using var service = CreateService(handler);

        var types = await service.GetTaskTypesAsync();

        Assert.NotNull(types);
        Assert.Empty(types);
    }

    [Fact]
    public async Task GetMyTasksAsync_AsksForTheOperatorsOwnTasksWithBearerToken()
    {
        using var handler = new RecordingHandler(_ => JsonResponse("""
            { "data": [{ "id": 7, "title": "Перезвонить", "status": "pending" }], "total": 1 }
            """));
        using var service = CreateService(handler);

        var result = await service.GetMyTasksAsync("pending");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "https://crm.example/api/tasks?assignedToId=42&page=1&limit=50&status=pending",
            request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Authorization!.Scheme);
        Assert.Equal("widget-token", request.Authorization.Parameter);

        Assert.NotNull(result);
        Assert.Equal(1, result!.Total);
        var task = Assert.Single(result.Data);
        Assert.NotNull(task);
        Assert.Equal(7, task!.Id);
        Assert.Equal("Перезвонить", task.Title);
    }

    [Fact]
    public async Task GetMyTasksAsync_OmitsTheStatusFilterWhenNoneIsAskedFor()
    {
        using var handler = new RecordingHandler(_ => JsonResponse("""{ "data": [], "total": 0 }"""));
        using var service = CreateService(handler);

        await service.GetMyTasksAsync(null);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(
            "https://crm.example/api/tasks?assignedToId=42&page=1&limit=50",
            request.RequestUri!.ToString());
    }

    /// <summary>
    /// tasks:read is a separate ability from the tasks:create the widget already uses, so
    /// a role without it is a real deployment. The screen has to tell "you may not look at
    /// this" apart from "the backend is down", and only the 403 latches.
    /// </summary>
    [Fact]
    public async Task GetMyTasksAsync_ForbiddenLatchesTheNoAccessFlag()
    {
        using var handler = new RecordingHandler(_ => ErrorResponse(HttpStatusCode.Forbidden));
        using var service = CreateService(handler);

        var result = await service.GetMyTasksAsync("pending");

        Assert.Null(result);
        Assert.True(service.TasksForbidden);
    }

    /// <summary>
    /// tasks:update is a separate ability from tasks:read, and only the reads answer the
    /// question TasksForbidden is asked. An operator who may see their tasks but not close
    /// one used to tick a task off, watch it fail honestly, and then have the tasks screen
    /// replace a list it had just fetched successfully with "no access" — for the rest of
    /// the session, since the flag lives as long as the token. TasksView is the only
    /// production caller of SetStatusAsync, which is why the path only became reachable
    /// when it was written.
    /// </summary>
    [Fact]
    public async Task SetStatusAsync_ForbiddenDoesNotLatchTheNoAccessFlag()
    {
        using var handler = new RecordingHandler(_ => ErrorResponse(HttpStatusCode.Forbidden));
        using var service = CreateService(handler);

        var ok = await service.SetStatusAsync(7, "done");

        Assert.False(ok);
        Assert.False(service.TasksForbidden);
    }

    /// <summary>
    /// Same rule from the other side: a task the operator may not create says nothing
    /// about the ones they may read.
    /// </summary>
    [Fact]
    public async Task CreateTaskAsync_ForbiddenDoesNotLatchTheNoAccessFlag()
    {
        using var handler = new RecordingHandler(_ => ErrorResponse(HttpStatusCode.Forbidden));
        using var service = CreateService(handler);

        var ok = await service.CreateTaskAsync(new CreateTaskRequest { Title = "Перезвонить" });

        Assert.False(ok);
        Assert.False(service.TasksForbidden);
    }

    /// <summary>GET /api/task-types is a different resource with its own ability again.</summary>
    [Fact]
    public async Task GetTaskTypesAsync_ForbiddenDoesNotLatchTheNoAccessFlag()
    {
        using var handler = new RecordingHandler(_ => ErrorResponse(HttpStatusCode.Forbidden));
        using var service = CreateService(handler);

        await service.GetTaskTypesAsync();

        Assert.False(service.TasksForbidden);
    }

    /// <summary>The badge poll reads the same tasks, so its 403 does latch.</summary>
    [Fact]
    public async Task GetMyStatsAsync_ForbiddenLatchesTheNoAccessFlag()
    {
        using var handler = new RecordingHandler(_ => ErrorResponse(HttpStatusCode.Forbidden));
        using var service = CreateService(handler);

        await service.GetMyStatsAsync();

        Assert.True(service.TasksForbidden);
    }

    [Fact]
    public async Task GetMyTasksAsync_ServerErrorDoesNotLatchTheNoAccessFlag()
    {
        using var handler = new RecordingHandler(_ => ErrorResponse(HttpStatusCode.InternalServerError));
        using var service = CreateService(handler);

        var result = await service.GetMyTasksAsync("pending");

        Assert.Null(result);
        Assert.False(service.TasksForbidden);
    }

    /// <summary>
    /// TaskService is a process-lifetime singleton (App.TaskService), but a session
    /// inside that process is not: MainWindow.ShowLoginAfterSessionExpiry returns to the
    /// login screen without restarting the app, so a token refresh or a handoff to a
    /// different operator on a shared terminal must not inherit a latch tripped by the
    /// token before it.
    /// </summary>
    [Fact]
    public async Task GetMyTasksAsync_ForbiddenLatchClearsWhenTheSessionTokenChanges()
    {
        var token = "widget-token";
        using var handler = new RecordingHandler(_ => ErrorResponse(HttpStatusCode.Forbidden));
        using var service = new TaskService(
            new HttpClient(handler),
            () => new SipSettings
            {
                BackendUrl = "https://crm.example/",
                AccessToken = token,
                DecodedToken = new JwtPayload { Sub = "42" },
            },
            ownsHttpClient: true);

        await service.GetMyTasksAsync("pending");
        Assert.True(service.TasksForbidden);

        token = "a-different-sessions-token";

        Assert.False(service.TasksForbidden);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{}")]
    public async Task GetMyTasksAsync_SurvivesAnEmptyOrShapelessBody(string body)
    {
        using var handler = new RecordingHandler(_ => JsonResponse(body));
        using var service = CreateService(handler);

        var result = await service.GetMyTasksAsync(null);

        Assert.True(result is null || result.Data.Count == 0);
    }

    /// <summary>
    /// A cancellation the caller asked for is not a failure to report. Task 8's tasks
    /// screen cancels the in-flight load when the operator switches the Open/All filter
    /// chip, and the generic catch used to swallow that into a logged "failure" plus a
    /// UI banner — telling the operator their own tap had gone wrong.
    /// </summary>
    [Fact]
    public async Task GetMyTasksAsync_PropagatesCallerCancellationInsteadOfReportingIt()
    {
        using var handler = new RecordingHandler(_ => JsonResponse("""{ "data": [], "total": 0 }"""));
        using var service = CreateService(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GetMyTasksAsync("pending", cancellation.Token));

        Assert.Empty(handler.Requests);
    }

    /// <summary>
    /// The local HS256 login signs sub as a number but the Zitadel ID token sends a
    /// string, and either can in principle carry something that is not a user id at all.
    /// AssignedToId's int.TryParse guard has to catch that before a request goes out
    /// with a garbage assignedToId, not after — so no request should be sent at all.
    /// </summary>
    [Theory]
    [InlineData("not-a-number")]
    [InlineData(null)]
    public async Task GetMyTasksAsync_ReturnsNullWithoutARequestWhenTheJwtSubIsNotANumber(string? sub)
    {
        using var handler = new RecordingHandler(_ => JsonResponse("""{ "data": [], "total": 0 }"""));
        using var service = new TaskService(
            new HttpClient(handler),
            () => new SipSettings
            {
                BackendUrl = "https://crm.example/",
                AccessToken = "widget-token",
                DecodedToken = new JwtPayload { Sub = sub },
            },
            ownsHttpClient: true);

        var result = await service.GetMyTasksAsync("pending");

        Assert.Null(result);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetMyStatsAsync_AsksForTheOperatorsOwnCounters()
    {
        using var handler = new RecordingHandler(_ => JsonResponse("""
            { "total": 9, "pending": 3, "inProgress": 2, "done": 4, "overdue": 1 }
            """));
        using var service = CreateService(handler);

        var stats = await service.GetMyStatsAsync();

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://crm.example/api/tasks/stats?assigneeId=42", request.RequestUri!.ToString());
        Assert.NotNull(stats);
        Assert.Equal(3, stats!.Pending);
        Assert.Equal(2, stats.InProgress);
        Assert.Equal(1, stats.Overdue);
    }

    /// <summary>
    /// NavBadgeService polls this every two minutes for a whole shift (Task 7). A banner
    /// on every failed poll is exactly what StatusService's ShouldReportFailure exists to
    /// avoid on the presence poll — a badge count is not worth interrupting a call over.
    /// </summary>
    [Fact]
    public async Task GetMyStatsAsync_FailsSilentlyWithoutRaisingTheErrorBanner()
    {
        using var handler = new RecordingHandler(_ => ErrorResponse(HttpStatusCode.InternalServerError));
        using var service = CreateService(handler);
        string? notification = null;
        Action<string> capture = message => notification = message;
        HttpErrorNotifier.ErrorOccurred += capture;

        try
        {
            var stats = await service.GetMyStatsAsync();

            Assert.Null(stats);
            Assert.Null(notification);
        }
        finally
        {
            HttpErrorNotifier.ErrorOccurred -= capture;
        }
    }

    [Fact]
    public async Task SetStatusAsync_PatchesTheTaskAndReportsSuccess()
    {
        using var handler = new RecordingHandler(_ => JsonResponse("""{ "id": 7, "status": "done" }"""));
        using var service = CreateService(handler);

        var ok = await service.SetStatusAsync(7, "done");

        Assert.True(ok);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Patch, request.Method);
        Assert.Equal("https://crm.example/api/tasks/7", request.RequestUri!.ToString());
        Assert.Contains("\"status\":\"done\"", request.Body);
    }

    [Fact]
    public async Task SetStatusAsync_ReportsFailureWithoutThrowing()
    {
        using var handler = new RecordingHandler(_ => ErrorResponse(HttpStatusCode.BadRequest));
        using var service = CreateService(handler);

        Assert.False(await service.SetStatusAsync(7, "done"));
    }

    /// <summary>
    /// The parameterless constructor hands TaskService the process-wide BackendHttp.Client
    /// (see the sibling DisposingATaskServiceLeavesTheSharedClientAlive in
    /// BackendHttpTests), so a flip of ownsHttpClient the other way has to actually take
    /// down a client it was given — otherwise every injected client this service is ever
    /// handed leaks for the life of the process.
    /// </summary>
    [Fact]
    public async Task Dispose_DisposesOwnedHttpClient()
    {
        using var handler = new RecordingHandler(_ => JsonResponse("{}"));
        using var client = new HttpClient(handler);
        var service = new TaskService(client, SettingsProvider, ownsHttpClient: true);

        service.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetAsync("https://crm.example/health"));
    }

    /// <summary>
    /// The BackendHttpTests sibling case only exercises the parameterless constructor
    /// against the global BackendHttp.Client; this pins the constructor path itself, the
    /// direction that matters most since App.TaskService hands out the process-wide
    /// client and a flipped default would dispose it out from under the whole app.
    /// </summary>
    [Fact]
    public async Task Dispose_DoesNotDisposeExternallyProvidedHttpClient()
    {
        using var handler = new RecordingHandler(_ => JsonResponse("{}"));
        using var client = new HttpClient(handler);
        var service = new TaskService(client, SettingsProvider);

        service.Dispose();
        using var response = await client.GetAsync("https://crm.example/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static TaskService CreateService(HttpMessageHandler handler) => new(
        new HttpClient(handler),
        SettingsProvider,
        ownsHttpClient: true);

    private static SipSettings SettingsProvider() => new()
    {
        BackendUrl = "https://crm.example/",
        AccessToken = "widget-token",
        // The numeric user id the tasks API assigns by; TaskService reads it off the JWT.
        // JwtPayload, not "DecodedToken" — that is the property name, not the type.
        DecodedToken = new JwtPayload { Sub = "42" },
    };

    private static HttpResponseMessage JsonResponse(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage ErrorResponse(HttpStatusCode code) => new(code)
    {
        Content = new StringContent("""{ "message": "nope" }""", Encoding.UTF8, "application/json"),
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
