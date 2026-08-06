using System;
using Avalonia.Controls;
using OrbitalSIP.Models;
using OrbitalSIP.Services;

namespace OrbitalSIP.Views
{
    /// <summary>
    /// Opens the task form as an ordinary owned window instead of a modal dialog.
    ///
    /// ShowDialog disables its owner for as long as the dialog lives, so while the
    /// form was up the operator could not hang up, mute, or answer the next call —
    /// and hiding the softphone to the tray in that state brought it back still
    /// disabled, with nothing left to do but kill the process.
    ///
    /// The awaited CreateTaskRequest is gone with the modality: the window raises
    /// <see cref="TaskDialog.TaskConfirmed"/> instead, and <paramref name="onConfirmed"/>
    /// picks up where the old await did.
    /// </summary>
    public static class TaskWindowLauncher
    {
        private static TaskDialog? _current;

        public static void Open(Window owner, string callerNumber, Action<CreateTaskRequest> onConfirmed)
        {
            if (!App.TaskWindows.TryBegin())
            {
                _current?.Activate();
                return;
            }

            try
            {
                var window = new TaskDialog(callerNumber);
                _current = window;
                window.TaskConfirmed += (_, request) => onConfirmed(request);
                window.Closed += (_, __) =>
                {
                    if (ReferenceEquals(_current, window)) _current = null;
                    App.TaskWindows.Complete();
                };
                window.Show(owner);
            }
            catch (Exception ex)
            {
                _current = null;
                App.TaskWindows.Complete();
                AppLogger.Log("TaskWindow", $"Failed to open task window: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
