using System;
using System.Text.Json.Serialization;

namespace OrbitalSIP.Models;

/// <summary>Server-resolved call anchor for an SMS composed in the softphone.</summary>
public sealed record SmsCallSource(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("id")] string Id);

/// <summary>
/// Payload for POST /api/messages/sms/send-from-call. The recipient is resolved
/// by the server from <see cref="Source"/> and is deliberately not represented here.
/// </summary>
public sealed record SendCallSmsRequest(
    [property: JsonPropertyName("requestId")] Guid RequestId,
    [property: JsonPropertyName("source")] SmsCallSource Source,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("templateId")] Guid? TemplateId);

/// <summary>Accepted SMS enqueue result returned by the call-SMS endpoint.</summary>
public sealed record SendCallSmsResult(
    [property: JsonPropertyName("messageId")] Guid MessageId,
    [property: JsonPropertyName("status")] string Status);

/// <summary>Subset of an existing message template needed by the compose dialog.</summary>
public sealed record MessageTemplateDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("content")] string? Content);
