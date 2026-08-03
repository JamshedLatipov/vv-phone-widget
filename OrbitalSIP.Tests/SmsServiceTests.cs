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

public class SmsServiceTests
{
    [Fact]
    public async Task GetTemplatesAsync_UsesSmsPaginationUrlAndBearerToken()
    {
        using var handler = new RecordingHandler(_ => JsonResponse("""
            { "data": [{ "id": "22222222-2222-2222-2222-222222222222", "name": "Напоминание", "content": "Текст" }] }
            """));
        using var service = CreateService(handler);

        var templates = await service.GetTemplatesAsync();

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://crm.example/api/messages/templates?channel=sms&isActive=true&page=1&limit=100", request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Authorization!.Scheme);
        Assert.Equal("widget-token", request.Authorization.Parameter);
        var template = Assert.Single(templates);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), template.Id);
        Assert.Equal("Напоминание", template.Name);
        Assert.Equal("Текст", template.Content);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{ \"data\": null }")]
    [InlineData("{ \"data\": {} }")]
    public async Task GetTemplatesAsync_RejectsMissingNullOrNonArrayData(string body)
    {
        using var handler = new RecordingHandler(_ => JsonResponse(body));
        using var service = CreateService(handler);

        var error = await Assert.ThrowsAsync<SmsApiException>(() => service.GetTemplatesAsync());

        Assert.Equal(HttpStatusCode.OK, error.StatusCode);
        Assert.Equal("SMS API returned an invalid response.", error.ApiMessage);
    }

    [Theory]
    [InlineData("{ \"data\": [{ \"id\": \"00000000-0000-0000-0000-000000000000\", \"name\": \"Шаблон\", \"content\": \"Текст\" }] }")]
    [InlineData("{ \"data\": [{ \"id\": \"22222222-2222-2222-2222-222222222222\", \"name\": \"   \", \"content\": \"Текст\" }] }")]
    [InlineData("{ \"data\": [{ \"id\": \"22222222-2222-2222-2222-222222222222\", \"name\": \"Шаблон\" }] }")]
    public async Task GetTemplatesAsync_RejectsTemplateItemsMissingRequiredComposeFields(string body)
    {
        using var handler = new RecordingHandler(_ => JsonResponse(body));
        using var service = CreateService(handler);

        var error = await Assert.ThrowsAsync<SmsApiException>(() => service.GetTemplatesAsync());

        Assert.Equal("SMS API returned an invalid response.", error.ApiMessage);
    }

    [Fact]
    public async Task SendFromCallAsync_SendsOnlyCallAnchoredPayloadAndReturnsQueuedResult()
    {
        using var handler = new RecordingHandler(_ => JsonResponse("""
            { "messageId": "33333333-3333-3333-3333-333333333333", "status": "queued" }
            """));
        using var service = CreateService(handler);
        var request = new SendCallSmsRequest(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            new SmsCallSource("history", "cdr-uuid"),
            "Подтверждённый текст",
            Guid.Parse("22222222-2222-2222-2222-222222222222"));

        var result = await service.SendFromCallAsync(request);

        var captured = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, captured.Method);
        Assert.Equal("https://crm.example/api/messages/sms/send-from-call", captured.RequestUri!.ToString());
        Assert.Equal("Bearer", captured.Authorization!.Scheme);
        Assert.Equal("widget-token", captured.Authorization.Parameter);
        using var payload = JsonDocument.Parse(captured.Body);
        Assert.Equal("11111111-1111-1111-1111-111111111111", payload.RootElement.GetProperty("requestId").GetString());
        Assert.Equal("history", payload.RootElement.GetProperty("source").GetProperty("type").GetString());
        Assert.Equal("cdr-uuid", payload.RootElement.GetProperty("source").GetProperty("id").GetString());
        Assert.Equal("Подтверждённый текст", payload.RootElement.GetProperty("content").GetString());
        Assert.Equal("22222222-2222-2222-2222-222222222222", payload.RootElement.GetProperty("templateId").GetString());
        Assert.DoesNotContain("phone", captured.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Guid.Parse("33333333-3333-3333-3333-333333333333"), result.MessageId);
        Assert.Equal("queued", result.Status);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{ \"messageId\": null, \"status\": \"queued\" }")]
    [InlineData("{ \"messageId\": \"00000000-0000-0000-0000-000000000000\", \"status\": \"queued\" }")]
    [InlineData("{ \"messageId\": \"33333333-3333-3333-3333-333333333333\", \"status\": null }")]
    [InlineData("{ \"messageId\": \"33333333-3333-3333-3333-333333333333\", \"status\": \" \" }")]
    [InlineData("{ \"messageId\": \"33333333-3333-3333-3333-333333333333\", \"status\": \"sent\" }")]
    public async Task SendFromCallAsync_RejectsMissingOrInvalidQueuedResponse(string body)
    {
        using var handler = new RecordingHandler(_ => JsonResponse(body));
        using var service = CreateService(handler);

        var error = await Assert.ThrowsAsync<SmsApiException>(() => service.SendFromCallAsync(ValidSmsRequest()));

        Assert.Equal(HttpStatusCode.OK, error.StatusCode);
        Assert.Equal("SMS API returned an invalid response.", error.ApiMessage);
    }

    [Fact]
    public async Task SendFromCallAsync_PropagatesCancellationWithoutSendingRequest()
    {
        using var handler = new RecordingHandler(_ => JsonResponse("{}"));
        using var service = CreateService(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SendFromCallAsync(
            new SendCallSmsRequest(Guid.NewGuid(), new SmsCallSource("active", "unique-id"), "Текст", null),
            cancellation.Token));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SendFromCallAsync_ExtractsApiErrorMessage()
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("""{ "message": "SMS запрещено политикой контакта" }""", Encoding.UTF8, "application/json"),
        });
        using var service = CreateService(handler);

        var error = await Assert.ThrowsAsync<SmsApiException>(() => service.SendFromCallAsync(
            new SendCallSmsRequest(Guid.NewGuid(), new SmsCallSource("active", "unique-id"), "Текст", null)));

        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
        Assert.Equal("SMS запрещено политикой контакта", error.ApiMessage);
    }

    [Theory]
    [InlineData("<html>token=top-secret</html>")]
    [InlineData("{ \"error\": \"top-secret\" }")]
    public async Task SendFromCallAsync_DoesNotReflectUntrustedErrorBodies(string body)
    {
        using var handler = new RecordingHandler(_ => ErrorResponse(body));
        using var service = CreateService(handler);

        var error = await Assert.ThrowsAsync<SmsApiException>(() => service.SendFromCallAsync(ValidSmsRequest()));

        Assert.Equal("SMS API вернул ошибку (HTTP 403).", error.ApiMessage);
        Assert.DoesNotContain("top-secret", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendFromCallAsync_CapsAllowlistedApiErrorMessage()
    {
        var longMessage = new string('x', 600);
        using var handler = new RecordingHandler(_ => ErrorResponse($$"""{ "message": "{{longMessage}}" }"""));
        using var service = CreateService(handler);

        var error = await Assert.ThrowsAsync<SmsApiException>(() => service.SendFromCallAsync(ValidSmsRequest()));

        Assert.True(error.ApiMessage.Length <= 256);
        Assert.EndsWith("…", error.ApiMessage);
    }

    [Fact]
    public async Task Dispose_DoesNotDisposeExternallyProvidedHttpClient()
    {
        using var handler = new RecordingHandler(_ => JsonResponse("{}"));
        using var client = new HttpClient(handler);
        var service = new SmsService(client, SettingsProvider);

        service.Dispose();
        using var response = await client.GetAsync("https://crm.example/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Dispose_DisposesOwnedHttpClient()
    {
        using var handler = new RecordingHandler(_ => JsonResponse("{}"));
        using var client = new HttpClient(handler);
        var service = new SmsService(client, SettingsProvider, ownsHttpClient: true);

        service.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetAsync("https://crm.example/health"));
    }

    private static SmsService CreateService(HttpMessageHandler handler) => new(
        new HttpClient(handler),
        SettingsProvider,
        ownsHttpClient: true);

    private static SipSettings SettingsProvider() => new() { BackendUrl = "https://crm.example/", AccessToken = "widget-token" };

    private static SendCallSmsRequest ValidSmsRequest() => new(
        Guid.NewGuid(), new SmsCallSource("active", "unique-id"), "Текст", null);

    private static HttpResponseMessage JsonResponse(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage ErrorResponse(string body) => new(HttpStatusCode.Forbidden)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
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
