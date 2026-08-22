using System;
using Avalonia.Controls;
using OrbitalSIP.Models;
using OrbitalSIP.Services;

namespace OrbitalSIP.Views
{
    /// <summary>
    /// Opens the transfer target picker as an ordinary owned window instead of the
    /// inline panel it used to be.
    ///
    /// Measured, not guessed: the call panel body is 496px (600 from
    /// ShellGeometry.PanelHeight, minus TopBarControl's 58px, minus
    /// BottomNavControl's 46px), and with the inline panel open at zero rows the
    /// content through HangupBtn already ran 508-525px — the hang-up button was
    /// below the fold before a single operator loaded. Hiding the quick-actions
    /// row recovered ~90px and still left it negative, and a ScrollViewer
    /// MaxHeight cannot help either: a cap only bounds a non-empty list, and at
    /// zero rows the scroller is zero anyway.
    ///
    /// ShowDialog disables its owner for as long as the picker is open, and a
    /// transfer picker sits over a live call — while it was up the operator could
    /// not hang up, mute, or answer the next call, exactly the incident
    /// ScriptsWindowLauncher's own docblock records. So this is an ordinary owned
    /// window, opened with Show, never ShowDialog.
    ///
    /// The awaited TransferRequest is gone with the modality: the window raises
    /// <see cref="TransferDialog.TransferSelected"/> instead, and
    /// <paramref name="onSelected"/> picks up where the old direct
    /// OnTransferRequested hookup did.
    /// </summary>
    public static class TransferWindowLauncher
    {
        private static TransferDialog? _current;

        /// <summary>Closes the open window, if there is one. Called when the session expires.</summary>
        public static void CloseIfOpen() => _current?.Close();

        public static void Open(Window owner, string callerNumber, Action<TransferRequest> onSelected)
        {
            if (!App.TransferWindows.TryBegin())
            {
                _current?.Activate();
                return;
            }

            try
            {
                var window = new TransferDialog(callerNumber);
                _current = window;
                window.TransferSelected += (_, request) => onSelected(request);
                window.Closed += (_, __) =>
                {
                    if (ReferenceEquals(_current, window)) _current = null;
                    App.TransferWindows.Complete();
                };
                window.Show(owner);
            }
            catch (Exception ex)
            {
                _current = null;
                App.TransferWindows.Complete();
                AppLogger.Log("TransferWindow", $"Failed to open transfer window: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
