using System;
using Avalonia.Controls;
using OrbitalSIP.Models;
using OrbitalSIP.Services;

namespace OrbitalSIP.Views
{
    /// <summary>
    /// Opens the script list as an ordinary owned window instead of a modal dialog.
    ///
    /// ShowDialog disables its owner for as long as the dialog lives, so while the
    /// list was up the operator could not hang up, mute, or answer the next call —
    /// and hiding the softphone to the tray in that state brought it back still
    /// disabled, with nothing left to do but kill the process. Reading a script to a
    /// live caller is exactly when the phone must stay usable.
    ///
    /// Both entry points — the active call and the call history — funnel through
    /// here, so they can no longer stack two lists over one another. The awaited
    /// ScriptSelection is gone with the modality: the window raises
    /// <see cref="ScriptsDialog.ScriptSelected"/> instead, and <paramref name="onSelected"/>
    /// picks up where the old await did.
    /// </summary>
    public static class ScriptsWindowLauncher
    {
        private static ScriptsDialog? _current;

        public static void Open(Window owner, Action<ScriptSelection> onSelected)
        {
            if (!App.ScriptWindows.TryBegin())
            {
                _current?.Activate();
                return;
            }

            try
            {
                var window = new ScriptsDialog();
                _current = window;
                window.ScriptSelected += (_, selection) => onSelected(selection);
                window.Closed += (_, __) =>
                {
                    if (ReferenceEquals(_current, window)) _current = null;
                    App.ScriptWindows.Complete();
                };
                window.Show(owner);
            }
            catch (Exception ex)
            {
                _current = null;
                App.ScriptWindows.Complete();
                AppLogger.Log("ScriptsWindow", $"Failed to open scripts window: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
