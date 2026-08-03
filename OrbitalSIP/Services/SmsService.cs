using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OrbitalSIP.Models;

namespace OrbitalSIP.Services;

/// <summary>Typed client for the server-resolved, call-anchored SMS endpoints.</summary>
public sealed class SmsService : IDisposable
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpClient _httpClient;
    private readonly Func<SipSettings> _settingsProvider;

    /// <summary>
    /// Creates the application client with the platform's default TLS validation.
    /// </summary>
    public SmsService()
        : this(new HttpClient(new HttpClientHandler()), () => App.SipService?.CurrentSettings ?? SipSettings.Load())
    {
    }

    /// <summary>Injectable boundary used by tests and host composition.</summary>
    public SmsService(HttpClient httpClient, Func<SipSettings> settingsProvider)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
    }

    public async Task<IReadOnlyList<MessageTemplateDto>> GetTemplatesAsync(CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(
            HttpMethod.Get,
            "/api/messages/templates?channel=sms&isActive=true&page=1&limit=100");
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        var page = await JsonSerializer.DeserializeAsync<TemplatePage>(content, ReadOptions, cancellationToken);
        return page?.Data ?? [];
    }

    public async Task<SendCallSmsResult> SendFromCallAsync(
        SendCallSmsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var httpRequest = CreateRequest(HttpMethod.Post, "/api/messages/sms/send-from-call");
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(request, WriteOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<SendCallSmsResult>(content, ReadOptions, cancellationToken)
            ?? throw new SmsApiException(response.StatusCode, "SMS API returned an empty response.");
    }

    public void Dispose() => _httpClient.Dispose();

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativeUrl)
    {
        var settings = _settingsProvider();
        var backendUrl = settings.BackendUrl?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(backendUrl))
            throw new InvalidOperationException("SMS API backend URL is not configured.");
        if (string.IsNullOrWhiteSpace(settings.AccessToken))
            throw new InvalidOperationException("SMS API access token is not configured.");

        var request = new HttpRequestMessage(method, $"{backendUrl}{relativeUrl}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.AccessToken);
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new SmsApiException(response.StatusCode, ExtractErrorMessage(body));
    }

    private static string ExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "SMS API request failed.";

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("message", out var message))
            {
                if (message.ValueKind == JsonValueKind.String)
                    return message.GetString() ?? "SMS API request failed.";
                if (message.ValueKind == JsonValueKind.Array)
                    return string.Join("; ", message.EnumerateArray()
                        .Where(item => item.ValueKind == JsonValueKind.String)
                        .Select(item => item.GetString()));
            }
        }
        catch (JsonException)
        {
            // The response is not JSON; the raw body remains the most useful error.
        }

        return body;
    }

    private sealed record TemplatePage(IReadOnlyList<MessageTemplateDto>? Data);
}

public sealed class SmsApiException(HttpStatusCode statusCode, string apiMessage) : HttpRequestException(apiMessage, null, statusCode)
{
    public string ApiMessage { get; } = apiMessage;
}
