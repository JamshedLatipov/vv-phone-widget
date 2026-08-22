using Avalonia.Controls;
using OrbitalSIP.Services;

namespace OrbitalSIP.Views
{
    /// <summary>
    /// Opens the survey as an ordinary owned window instead of a modal dialog.
    ///
    /// ShowDialog disables its owner for as long as the dialog lives, and the survey
    /// lives for the whole call — so while it was up the operator could not hang up,
    /// mute, or answer the next call, and every backend round-trip inside it read as
    /// a frozen softphone. Worse, hiding the softphone to the tray in that state
    /// brought it back still disabled, with nothing left to do but kill the process.
    ///
    /// An owned window keeps the softphone live next to the questionnaire, so a slow
    /// or wedged survey can never take the phone down with it.
    /// </summary>
    public static class SurveyWindowLauncher
    {
        private static SurveyDialog? _current;

        /// <summary>Closes the open window, if there is one. Called when the session expires.</summary>
        public static void CloseIfOpen() => _current?.Close();

        public static void Open(Window owner, string callerNumber, string? autoFlowId = null)
        {
            // Both entry points funnel through here, so a campaign auto-open and a
            // button press can no longer stack two windows over one call.
            if (!App.SurveySessions.TryBegin())
            {
                _current?.Activate();
                return;
            }

            try
            {
                var window = new SurveyDialog(callerNumber, autoFlowId);
                _current = window;
                window.Closed += (_, __) =>
                {
                    if (ReferenceEquals(_current, window)) _current = null;
                    App.SurveySessions.Complete();
                };
                window.Show(owner);
            }
            catch (System.Exception ex)
            {
                _current = null;
                App.SurveySessions.Complete();
                AppLogger.Log("SurveyWindow", $"Failed to open survey window: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
