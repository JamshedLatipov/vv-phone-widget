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
    private const int MaxErrorBodyCharacters = 4096;
    private const int MaxApiMessageCharacters = 256;
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpClient _httpClient;
    private readonly Func<SipSettings> _settingsProvider;
    private readonly bool _ownsHttpClient;

    /// <summary>
    /// Creates the application client over the shared backend pool.
    ///
    /// It used to build its own HttpClient, which cost it a second socket pool against
    /// the same host, no PooledConnectionLifetime (so it pinned the first address it
    /// resolved for a process that runs all day), and — once the shared pool grew one —
    /// no access to the token refresh every other service now gets for free.
    /// </summary>
    public SmsService()
        : this(
            BackendHttp.Client,
            () => App.SipService?.CurrentSettings ?? SipSettings.Load(),
            ownsHttpClient: false)
    {
    }

    /// <summary>Injectable boundary used by tests and host composition.</summary>
    public SmsService(HttpClient httpClient, Func<SipSettings> settingsProvider, bool ownsHttpClient = false)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
        _ownsHttpClient = ownsHttpClient;
    }

    public async Task<IReadOnlyList<MessageTemplateDto>> GetTemplatesAsync(CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(
            HttpMethod.Get,
            "/api/messages/templates?channel=sms&isActive=true&page=1&limit=100");
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        try
        {
            await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
            return ParseTemplates(response.StatusCode, document.RootElement);
        }
        catch (JsonException)
        {
            throw InvalidSuccessResponse(response.StatusCode);
        }
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

        try
        {
            await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
            return ParseQueuedResult(response.StatusCode, document.RootElement);
        }
        catch (JsonException)
        {
            throw InvalidSuccessResponse(response.StatusCode);
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }

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

        var body = await ReadBodyAtMostAsync(response.Content, cancellationToken);
        throw new SmsApiException(response.StatusCode, ExtractErrorMessage(response.StatusCode, body));
    }

    private static IReadOnlyList<MessageTemplateDto> ParseTemplates(HttpStatusCode statusCode, JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array)
            throw InvalidSuccessResponse(statusCode);

        var templates = new List<MessageTemplateDto>();
        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !TryGetNonEmptyGuid(item, "id", out var id) ||
                !TryGetNonBlankString(item, "name", out var name))
                throw InvalidSuccessResponse(statusCode);

            if (TryGetNonBlankString(item, "content", out var content))
                templates.Add(new MessageTemplateDto(id, name, content));
        }

        return templates;
    }

    private static SendCallSmsResult ParseQueuedResult(HttpStatusCode statusCode, JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !TryGetNonEmptyGuid(root, "messageId", out var messageId) ||
            !TryGetNonBlankString(root, "status", out var status) ||
            !string.Equals(status, "queued", StringComparison.Ordinal))
            throw InvalidSuccessResponse(statusCode);

        return new SendCallSmsResult(messageId, status);
    }

    private static bool TryGetNonEmptyGuid(JsonElement value, string propertyName, out Guid guid)
    {
        guid = Guid.Empty;
        return value.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String &&
               Guid.TryParse(property.GetString(), out guid) &&
               guid != Guid.Empty;
    }

    private static bool TryGetNonBlankString(JsonElement value, string propertyName, out string result)
    {
        result = string.Empty;
        if (!value.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
            return false;

        result = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(result);
    }

    private static async Task<string> ReadBodyAtMostAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var reader = new System.IO.StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
        var buffer = new char[MaxErrorBodyCharacters + 1];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken);
            if (read == 0)
                break;
            total += read;
        }

        return new string(buffer, 0, Math.Min(total, MaxErrorBodyCharacters));
    }

    private static string ExtractErrorMessage(HttpStatusCode statusCode, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return SafeStatusMessage(statusCode);

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("message", out var message))
            {
                if (message.ValueKind == JsonValueKind.String)
                    return CapMessage(message.GetString());
                if (message.ValueKind == JsonValueKind.Array)
                {
                    var parts = message.EnumerateArray()
                        .Where(item => item.ValueKind == JsonValueKind.String)
                        .Select(item => item.GetString())
                        .Where(item => !string.IsNullOrWhiteSpace(item));
                    var combined = string.Join("; ", parts);
                    if (!string.IsNullOrWhiteSpace(combined))
                        return CapMessage(combined);
                }
            }
        }
        catch (JsonException)
        {
            // Raw responses are deliberately never reflected in exception messages.
        }

        return SafeStatusMessage(statusCode);
    }

    private static string CapMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "SMS API вернул ошибку.";

        return message.Length <= MaxApiMessageCharacters
            ? message
            : $"{message[..(MaxApiMessageCharacters - 1)]}…";
    }

    private static string SafeStatusMessage(HttpStatusCode statusCode) =>
        $"SMS API вернул ошибку (HTTP {(int)statusCode}).";

    private static SmsApiException InvalidSuccessResponse(HttpStatusCode statusCode) =>
        new(statusCode, "SMS API returned an invalid response.");
}

public sealed class SmsApiException(HttpStatusCode statusCode, string apiMessage) : HttpRequestException(apiMessage, null, statusCode)
{
    public string ApiMessage { get; } = apiMessage;
}
