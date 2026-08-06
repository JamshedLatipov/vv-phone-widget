using System;
using System.Text;
using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

/// <summary>
/// The backend signs "sub" as the numeric user id (@PrimaryGeneratedColumn id: number),
/// so the claim arrives as a JSON number. Deserialising that into a string threw, and
/// Decode's catch-all turned one bad claim into a null payload — which is why tasks
/// created off a call came out unassigned while every other consumer quietly fell back
/// to settings.Username and nobody noticed.
/// </summary>
public class JwtDecoderTests
{
    private static string TokenWithPayload(string payloadJson)
    {
        var body = Base64Url(Encoding.UTF8.GetBytes(payloadJson));
        return $"{Base64Url(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\"}"))}.{body}.signature";
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Fact]
    public void NumericSub_IsReadAsItsDigits()
    {
        var payload = JwtDecoder.Decode(TokenWithPayload("{\"sub\":7,\"username\":\"op\"}"));

        Assert.NotNull(payload);
        Assert.Equal("7", payload!.Sub);
    }

    [Fact]
    public void NumericSub_ParsesToTheUserIdTheTaskAssignmentNeeds()
    {
        var payload = JwtDecoder.Decode(TokenWithPayload("{\"sub\":42}"));

        Assert.True(int.TryParse(payload?.Sub, out var userId));
        Assert.Equal(42, userId);
    }

    [Fact]
    public void NumericSub_NoLongerDiscardsTheRestOfThePayload()
    {
        var payload = JwtDecoder.Decode(TokenWithPayload(
            "{\"sub\":7,\"username\":\"op\",\"fullName\":\"Оператор\",\"roles\":[\"operator\"]," +
            "\"operator\":{\"username\":\"1001\",\"password\":\"x\"}}"));

        Assert.NotNull(payload);
        Assert.Equal("op", payload!.Username);
        Assert.Equal("Оператор", payload.FullName);
        Assert.Equal(new[] { "operator" }, payload.Roles);
        Assert.Equal("1001", payload.Operator?.Username);
    }

    [Fact]
    public void StringSub_StillDecodes()
    {
        // The Zitadel path hands over an ID token whose sub is an opaque string.
        var payload = JwtDecoder.Decode(TokenWithPayload("{\"sub\":\"313200925293052161\"}"));

        Assert.Equal("313200925293052161", payload?.Sub);
    }

    [Fact]
    public void MissingSub_LeavesItNullWithoutLosingThePayload()
    {
        var payload = JwtDecoder.Decode(TokenWithPayload("{\"username\":\"op\"}"));

        Assert.NotNull(payload);
        Assert.Null(payload!.Sub);
        Assert.Equal("op", payload.Username);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-jwt")]
    [InlineData("only.two")]
    public void MalformedToken_ReturnsNull(string token)
    {
        Assert.Null(JwtDecoder.Decode(token));
    }
}
