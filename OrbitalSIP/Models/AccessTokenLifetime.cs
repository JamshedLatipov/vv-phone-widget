using System;

namespace OrbitalSIP.Models;

/// <summary>
/// Decides when the access token is due for renewal. Pure, and separate from
/// <c>BackendAuth</c>, because the interesting cases here — a missing claim, a clock that
/// disagrees with the server's, the boundary itself — are the ones that decide whether an
/// operator keeps working or is bounced to the login screen, and none of them are
/// reachable from a test that has to stand up an HttpClient first.
/// </summary>
public static class AccessTokenLifetime
{
    /// <summary>
    /// How far ahead of the stated expiry a token counts as spent. Covers clock skew
    /// between this PC and the backend plus a slow request; being wrong in this direction
    /// costs one extra POST, being wrong in the other costs a failed request.
    /// </summary>
    public static readonly TimeSpan DefaultSkew = TimeSpan.FromMinutes(2);

    /// <summary>
    /// True when the token should be renewed before use.
    ///
    /// A null <paramref name="exp"/> reads as spent rather than as «good forever»: a
    /// token whose lifetime we cannot read is exactly the one worth replacing, and the
    /// single-flight gate in BackendAuth keeps that from becoming a refresh per request.
    /// </summary>
    public static bool IsSpent(long? exp, DateTimeOffset utcNow, TimeSpan skew)
    {
        if (exp == null) return true;

        // Outside what DateTimeOffset can represent the claim is not a time we can reason
        // about — same answer as a missing one.
        if (exp.Value < DateTimeOffset.MinValue.ToUnixTimeSeconds() ||
            exp.Value > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
            return true;

        return utcNow + skew >= DateTimeOffset.FromUnixTimeSeconds(exp.Value);
    }

    /// <summary>Convenience overload using <see cref="DefaultSkew"/> and the current time.</summary>
    public static bool IsSpent(long? exp) => IsSpent(exp, DateTimeOffset.UtcNow, DefaultSkew);
}
