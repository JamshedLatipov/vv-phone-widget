using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using OrbitalSIP.Services;

namespace OrbitalSIP.Views
{
    public partial class ActiveCallView
    {
        // ── DTMF pad ──────────────────────────────────────────────────────────

        /// <summary>
        /// Opens or closes the in-call DTMF pad.
        ///
        /// KeypadBtn used to raise OnKeypadRequested, which MainWindow wired to
        /// ShowDialer() — and ShowDialer() redirects back to the call screen whenever a
        /// call is active or on hold. So the only thing pressing «Клавиши» ever did was
        /// rebuild the screen it was already on, and the operator had no way to send a
        /// single DTMF tone into an IVR from this widget.
        /// </summary>
        private void ToggleDtmfPanel()
        {
            var panel = this.FindControl<Border>("DtmfPanel");
            if (panel == null) return;

            if (!panel.IsVisible)
            {
                BuildDtmfPad();
                UpdateDtmfPadEnabled();
            }
            panel.IsVisible = !panel.IsVisible;

            if (!panel.IsVisible)
            {
                var echo = this.FindControl<TextBlock>("DtmfEchoLabel");
                if (echo != null) echo.Text = string.Empty;
            }
        }

        /// <summary>Fills the pad once; reopening reuses the buttons already there.</summary>
        private void BuildDtmfPad()
        {
            var grid = this.FindControl<UniformGrid>("DtmfGrid");
            if (grid == null || grid.Children.Count > 0) return;

            foreach (var digit in "123456789*0#")
            {
                var key = digit;
                var button = new Button
                {
                    Content = key.ToString(),
                    FontSize = 16,
                    FontWeight = Avalonia.Media.FontWeight.SemiBold,
                    Foreground = Avalonia.Media.Brushes.White,
                    Background = Avalonia.Media.Brush.Parse("#152132"),
                    BorderThickness = new Avalonia.Thickness(0),
                    CornerRadius = new Avalonia.CornerRadius(10),
                    Margin = new Avalonia.Thickness(3),
                    Height = 34,
                    HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                };
                button.Click += SafeHandler.Click("ActiveCallDtmf", () => SendDtmfAsync(key));
                grid.Children.Add(button);
            }
        }

        private async Task SendDtmfAsync(char digit)
        {
            // Mirrors TransferToLeadOwner's defensive re-check: IsEnabled already keeps a
            // held call from reaching this (see UpdateDtmfPadEnabled), but the handler
            // asks DtmfPadPresenter again rather than trusting a button never fires stale.
            if (!DtmfPadPresenter.CanSend(_onHold)) return;

            var echo = this.FindControl<TextBlock>("DtmfEchoLabel");
            if (echo != null) echo.Text += digit;

            await App.SipService.SendDtmfAsync(digit);
        }

        /// <summary>
        /// Paints the pad from <see cref="_onHold"/>. Called everywhere UpdateHoldUI is
        /// — SetStatus (the server confirming a hold transition) and ToggleHold (the
        /// operator's own Hold/Resume button) — so the pad reflects hold however it
        /// changed, including while it is already open.
        ///
        /// SipService.SendDtmfAsync already refuses to send outside CallState.Active —
        /// hold's re-INVITE takes the media path down, so a tone would reach no one —
        /// but it does so silently. Before this, the pad kept every key live regardless,
        /// so a press taken while the caller was parked just vanished: no tone, no
        /// error, nothing to tell the operator why the IVR never responded. Graying the
        /// keys out and naming the reason turns that silence into something the operator
        /// can act on — take the call off hold, then dial.
        /// </summary>
        private void UpdateDtmfPadEnabled()
        {
            var grid = this.FindControl<UniformGrid>("DtmfGrid");
            var hint = this.FindControl<TextBlock>("DtmfHoldHintLabel");
            var canSend = DtmfPadPresenter.CanSend(_onHold);

            if (hint != null) hint.IsVisible = !canSend;
            if (grid == null) return;

            foreach (var child in grid.Children)
            {
                if (child is Button button)
                {
                    button.IsEnabled = canSend;
                    button.Opacity = canSend ? 1.0 : 0.5;
                }
            }
        }
    }
}
