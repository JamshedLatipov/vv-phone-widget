using System;

namespace OrbitalSIP.Models;

/// <summary>
/// Keeps subscriber numbers out of the log file.
///
/// The log is plain text under %APPDATA% on a machine that is not the operator's alone,
/// and it recorded every caller's full number several times per call — the call-context
/// lookup URL, the CDR anchor lookup, the lead panel. A masked number is still enough to
/// correlate lines with the same call while reading a log, which is all the log needs it
/// for.
/// </summary>
public static class LogRedaction
{
    private const int VisiblePrefix = 4;
    private const int VisibleSuffix = 2;

    /// <summary>
    /// At least this many characters must end up masked, or the value is masked entirely.
    ///
    /// Without a floor, a short internal extension or a 7-digit local number came out with
    /// one or two stars and everything else in the clear — the helper would have been
    /// applied, and would have redacted essentially nothing.
    /// </summary>
    private const int MinimumMasked = 4;

    /// <summary>
    /// Masks the middle of a phone number: <c>+992901234567</c> reads as
    /// <c>+992*******67</c>. Values too short to keep a meaningful prefix, a meaningful
    /// suffix and <see cref="MinimumMasked"/> hidden characters are masked entirely.
    /// </summary>
    public static string Phone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "<none>";

        var trimmed = value.Trim();
        if (trimmed.Length < VisiblePrefix + VisibleSuffix + MinimumMasked)
            return new string('*', trimmed.Length);

        return string.Concat(
            trimmed.AsSpan(0, VisiblePrefix),
            new string('*', trimmed.Length - VisiblePrefix - VisibleSuffix),
            trimmed.AsSpan(trimmed.Length - VisibleSuffix));
    }

    /// <summary>
    /// Masks digit runs in a URL, so logging the request that failed does not log the
    /// caller.
    ///
    /// Several backend routes carry the subscriber's number in the path
    /// (<c>/api/integrations/call-info/992901234567</c>) or the query
    /// (<c>?phone=…</c>, <c>?callerNumber=…</c>), and the error path logs the URL — which
    /// put back on disk exactly what redacting the call sites took off it. Short runs are
    /// left alone so ids, page numbers and API versions stay readable.
    /// </summary>
    public static string Url(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "<no url>";

        var builder = new System.Text.StringBuilder(url.Length);
        var run = 0;

        void FlushRun(int digitsSoFar)
        {
            if (digitsSoFar == 0) return;

            // Anything long enough to be a phone number, and nothing shorter.
            if (digitsSoFar >= 6)
            {
                var start = builder.Length - digitsSoFar;
                var keep  = Math.Min(3, digitsSoFar);
                builder.Length = start + keep;
                builder.Append('*', digitsSoFar - keep);
            }
        }

        foreach (var c in url)
        {
            if (char.IsDigit(c)) { run++; builder.Append(c); continue; }

            FlushRun(run);
            run = 0;
            builder.Append(c);
        }

        FlushRun(run);
        return builder.ToString();
    }
}
