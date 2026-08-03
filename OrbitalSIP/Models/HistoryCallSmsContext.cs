using System;

namespace OrbitalSIP.Models;

/// <summary>Immutable UI context for composing an SMS from a call-history CDR.</summary>
public sealed record HistoryCallSmsContext(SmsCallSource Source, string LockedDisplayNumber)
{
    public static bool TryCreate(CdrEntry? entry, string lockedDisplayNumber, out HistoryCallSmsContext? context)
    {
        if (entry?.Id is not { Length: 36 } cdrId || !Guid.TryParseExact(cdrId, "D", out _))
        {
            context = null;
            return false;
        }

        context = new HistoryCallSmsContext(
            new SmsCallSource("history", cdrId),
            lockedDisplayNumber);
        return true;
    }
}
