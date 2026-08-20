using System;
using System.Linq;
using OrbitalSIP.Models;
using Xunit;

namespace OrbitalSIP.Tests;

public class LogRedactionTests
{
    [Theory]
    [InlineData("+992901234567", "+992*******67")]
    [InlineData("74951234567", "7495*****67")]
    [InlineData("9901234567", "9901****67")]   // shortest value that keeps a readable prefix
    public void MiddleOfThePhoneIsMasked(string phone, string expected)
    {
        Assert.Equal(expected, LogRedaction.Phone(phone));
    }

    /// <summary>
    /// The point is that two log lines about the same call still read as the same call.
    /// A mask that lost the prefix would make the log useless for support.
    /// </summary>
    [Fact]
    public void SameNumberMasksIdentically()
    {
        Assert.Equal(LogRedaction.Phone("+992901234567"), LogRedaction.Phone(" +992901234567 "));
    }

    [Fact]
    public void DifferentNumbersOfTheSameLengthStayDistinguishable()
    {
        Assert.NotEqual(LogRedaction.Phone("+992901234567"), LogRedaction.Phone("+998901234567"));
    }

    /// <summary>
    /// A short value cannot keep a prefix, a suffix AND hide enough in between, so it is
    /// masked wholly. Internal extensions and 7-digit local numbers land here — they used
    /// to come out with one or two stars and the rest in the clear, which is not redaction.
    /// </summary>
    [Theory]
    [InlineData("123", "***")]
    [InlineData("1234", "****")]
    [InlineData("123456", "******")]
    [InlineData("1234567", "*******")]
    [InlineData("12345678", "********")]
    [InlineData("9012345", "*******")]
    [InlineData("901234567", "*********")]   // 9 digits: masking it partially left only 3 stars
    public void ShortValuesAreMaskedEntirely(string phone, string expected)
    {
        Assert.Equal(expected, LogRedaction.Phone(phone));
    }

    [Fact]
    public void AtLeastFourCharactersAreAlwaysHidden()
    {
        foreach (var length in Enumerable.Range(1, 20))
        {
            var masked = LogRedaction.Phone(new string('7', length));
            Assert.True(masked.Count(c => c == '*') >= Math.Min(4, length),
                $"A {length}-character value was masked as '{masked}'.");
        }
    }

    // ── URLs ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Several backend routes carry the caller's number in the path or the query, and the
    /// error path logs the URL — which put back on disk exactly what redacting the call
    /// sites removed.
    /// </summary>
    [Theory]
    [InlineData("http://10.10.103.46/api/integrations/call-info/992901234567")]
    [InlineData("http://10.10.103.46/api/leads/call-context?phone=992901234567")]
    [InlineData("http://10.10.103.46/api/cdr/channel-uniqueid?callerNumber=992901234567")]
    public void PhoneNumbersInUrlsAreMasked(string url)
    {
        var masked = LogRedaction.Url(url);

        Assert.DoesNotContain("992901234567", masked);
        Assert.Contains("*", masked);
    }

    /// <summary>The route itself has to stay readable, or the log stops being useful.</summary>
    [Fact]
    public void RoutePartOfTheUrlSurvives()
    {
        var masked = LogRedaction.Url("http://10.10.103.46/api/leads/call-context?phone=992901234567");

        Assert.Contains("/api/leads/call-context", masked);
        Assert.StartsWith("http://", masked);
    }

    /// <summary>Short numeric segments are ids, ports, versions and page numbers, not people.</summary>
    [Theory]
    [InlineData("https://crm.internal/api/leads/42/call-comment", "42")]
    [InlineData("https://crm.internal/api/cdr?page=1&limit=20", "20")]
    [InlineData("https://crm.internal:8443/api/v2/tasks", "8443")]
    public void ShortNumericSegmentsAreLeftReadable(string url, string mustSurvive)
    {
        Assert.Contains(mustSurvive, LogRedaction.Url(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AbsentUrlReadsAsPlaceholder(string? url)
    {
        Assert.Equal("<no url>", LogRedaction.Url(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AbsentNumberReadsAsNone(string? phone)
    {
        Assert.Equal("<none>", LogRedaction.Phone(phone));
    }

    [Fact]
    public void MaskNeverContainsTheMiddleDigits()
    {
        Assert.DoesNotContain("12345", LogRedaction.Phone("+992901234567"));
    }
}
