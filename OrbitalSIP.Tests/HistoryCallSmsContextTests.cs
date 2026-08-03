using System;
using System.Text.Json;
using OrbitalSIP.Models;
using Xunit;

namespace OrbitalSIP.Tests;

public class HistoryCallSmsContextTests
{
    [Fact]
    public void TryCreate_UsesOnlyCdrUuidForHistorySourceAndKeepsDisplayNumberOutOfRequest()
    {
        var cdr = new CdrEntry
        {
            Id = "11111111-1111-1111-1111-111111111111",
            UniqueId = "asterisk-unique-id",
            Src = "+992 900 000 001",
            Dst = "+992 900 000 002",
        };

        var created = HistoryCallSmsContext.TryCreate(cdr, "+992 ** *** 12 34", out var context);

        Assert.True(created);
        Assert.NotNull(context);
        Assert.Equal("history", context.Source.Type);
        Assert.Equal("11111111-1111-1111-1111-111111111111", context.Source.Id);
        Assert.Equal("+992 ** *** 12 34", context.LockedDisplayNumber);

        var request = new SendCallSmsRequest(Guid.NewGuid(), context.Source, "Тест", null);
        var payload = JsonSerializer.Serialize(request);
        Assert.DoesNotContain("+992", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("asterisk-unique-id", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("phone", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("asterisk-unique-id")]
    public void TryCreate_BlocksComposeWhenCdrIdIsMissingOrNotUuid(string? cdrId)
    {
        var cdr = new CdrEntry
        {
            Id = cdrId,
            UniqueId = "asterisk-unique-id",
            Src = "+992 900 000 001",
            Dst = "+992 900 000 002",
        };

        var created = HistoryCallSmsContext.TryCreate(cdr, "+992 ** *** 12 34", out var context);

        Assert.False(created);
        Assert.Null(context);
    }
}
