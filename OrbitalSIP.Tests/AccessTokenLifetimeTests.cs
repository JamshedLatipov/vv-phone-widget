using System;
using OrbitalSIP.Models;
using Xunit;

namespace OrbitalSIP.Tests;

public class AccessTokenLifetimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Skew = TimeSpan.FromMinutes(2);

    private static long Unix(DateTimeOffset moment) => moment.ToUnixTimeSeconds();

    [Fact]
    public void TokenWithHoursLeftIsNotSpent()
    {
        Assert.False(AccessTokenLifetime.IsSpent(Unix(Now.AddHours(6)), Now, Skew));
    }

    [Fact]
    public void ExpiredTokenIsSpent()
    {
        Assert.True(AccessTokenLifetime.IsSpent(Unix(Now.AddMinutes(-1)), Now, Skew));
    }

    /// <summary>
    /// The whole point of the skew: a token that is still technically valid but will die
    /// mid-request has to be renewed before the request, not after it fails.
    /// </summary>
    [Fact]
    public void TokenInsideTheSkewWindowIsSpent()
    {
        Assert.True(AccessTokenLifetime.IsSpent(Unix(Now.AddSeconds(90)), Now, Skew));
    }

    [Fact]
    public void TokenJustOutsideTheSkewWindowIsNotSpent()
    {
        Assert.False(AccessTokenLifetime.IsSpent(Unix(Now.AddMinutes(2).AddSeconds(1)), Now, Skew));
    }

    /// <summary>Exactly at the boundary counts as spent — renew rather than gamble.</summary>
    [Fact]
    public void TokenExactlyAtTheSkewBoundaryIsSpent()
    {
        Assert.True(AccessTokenLifetime.IsSpent(Unix(Now.Add(Skew)), Now, Skew));
    }

    /// <summary>
    /// An issuer that omits `exp` must not read as «good forever» — that is the one shape
    /// that would keep a dead token in use for the rest of the shift.
    /// </summary>
    [Fact]
    public void MissingExpiryIsSpent()
    {
        Assert.True(AccessTokenLifetime.IsSpent(null, Now, Skew));
    }

    [Theory]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void UnrepresentableExpiryIsSpentRatherThanThrowing(long exp)
    {
        Assert.True(AccessTokenLifetime.IsSpent(exp, Now, Skew));
    }

    /// <summary>A backend clock running ahead of this PC must not read as already expired.</summary>
    [Fact]
    public void ClockSkewInTheOperatorsFavourIsNotSpent()
    {
        Assert.False(AccessTokenLifetime.IsSpent(Unix(Now.AddDays(2)), Now, Skew));
    }
}
