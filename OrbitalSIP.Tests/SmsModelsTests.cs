using System;
using System.Text.Json;
using OrbitalSIP.Models;
using Xunit;

namespace OrbitalSIP.Tests;

public class SmsModelsTests
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Theory]
    [InlineData("active", "asterisk-primary-unique-id")]
    [InlineData("history", "cdr-uuid")]
    public void SendCallSmsRequest_SerializesCallSourceWithoutPhone(string sourceType, string sourceId)
    {
        var request = new SendCallSmsRequest(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            new SmsCallSource(sourceType, sourceId),
            "Перезвоните, пожалуйста",
            null);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(request, WriteOptions));
        var root = document.RootElement;

        Assert.Equal("11111111-1111-1111-1111-111111111111", root.GetProperty("requestId").GetString());
        Assert.Equal(sourceType, root.GetProperty("source").GetProperty("type").GetString());
        Assert.Equal(sourceId, root.GetProperty("source").GetProperty("id").GetString());
        Assert.Equal("Перезвоните, пожалуйста", root.GetProperty("content").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("templateId").ValueKind);
        Assert.DoesNotContain("phone", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }
}
