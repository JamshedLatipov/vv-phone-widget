using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

public class TaskServiceTests
{
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
        Assert.Equal(7, task.Id);
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

    [Fact]
    public async Task GetMyTasksAsync_ServerErrorDoesNotLatchTheNoAccessFlag()
    {
        using var handler = new RecordingHandler(_ => ErrorResponse(HttpStatusCode.InternalServerError));
        using var service = CreateService(handler);

        var result = await service.GetMyTasksAsync("pending");

        Assert.Null(result);
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
