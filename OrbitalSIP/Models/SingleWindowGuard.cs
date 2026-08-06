namespace OrbitalSIP.Models;

/// <summary>
/// Admits one window of a given kind at a time, application-wide.
///
/// Every dialog the softphone opens has more than one entry point — a survey from
/// the active-call button and from the campaign auto-open that fires on answer, a
/// script list from the active call and from the call history — and none of them
/// knew about the others. Now that these windows are non-modal, nothing else stops
/// a second one from stacking over the first.
///
/// One instance per dialog kind: guards are independent, so an open survey must
/// never block the script list.
/// </summary>
public sealed class SingleWindowGuard
{
    private readonly object _gate = new();
    private bool _open;

    public bool IsOpen
    {
        get { lock (_gate) return _open; }
    }

    /// <returns>false when a window is already on screen — the caller must not open another.</returns>
    public bool TryBegin()
    {
        lock (_gate)
        {
            if (_open) return false;
            _open = true;
            return true;
        }
    }

    /// <summary>Releases the slot. Safe to call more than once for the same window.</summary>
    public void Complete()
    {
        lock (_gate) _open = false;
    }
}
