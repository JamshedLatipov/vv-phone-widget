using System.Linq;

namespace OrbitalSIP.Models;

/// <summary>
/// Cosmetic grouping for the locked recipient. Display only — the raw value
/// stays untouched in <see cref="SmsComposeState.Recipient"/> and never reaches
/// the request, which is built server-side from the call anchor.
/// </summary>
public static class SmsRecipientFormatter
{
    public static string Format(string? raw)
    {
        var value = raw?.Trim() ?? string.Empty;
        if (value.Length == 0)
            return string.Empty;

        // Anything already carrying separators arrived formatted (or masked) from
        // upstream. Regrouping it would corrupt masks like "+992 ** *** 12 34".
        var digits = value.StartsWith('+') ? value[1..] : value;
        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit))
            return value;

        if (digits.Length == 12 && digits.StartsWith("992"))
            return $"+992 {digits[3..5]} {digits[5..8]} {digits[8..10]} {digits[10..12]}";

        if (digits.Length == 9 && !value.StartsWith('+'))
            return $"{digits[0..3]} {digits[3..5]} {digits[5..7]} {digits[7..9]}";

        return value;
    }
}
