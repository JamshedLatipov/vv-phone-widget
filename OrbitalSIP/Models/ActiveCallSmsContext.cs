namespace OrbitalSIP.Models;

/// <summary>Immutable UI context for composing an SMS from the current call.</summary>
public sealed record ActiveCallSmsContext(SmsCallSource Source, string LockedDisplayNumber)
{
    public static bool TryCreate(string? primaryLinkedId, string lockedDisplayNumber, out ActiveCallSmsContext? context)
    {
        if (string.IsNullOrWhiteSpace(primaryLinkedId))
        {
            context = null;
            return false;
        }

        context = new ActiveCallSmsContext(
            new SmsCallSource("active", primaryLinkedId),
            lockedDisplayNumber);
        return true;
    }
}
