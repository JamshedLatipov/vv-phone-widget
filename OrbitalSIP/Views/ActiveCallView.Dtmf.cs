using System.Threading;
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
        /// Serializes the actual SendDtmf calls below. SafeHandler.Click gives each press
        /// its own fire-and-forget async operation, so nothing else stops a second digit
        /// typed quickly from starting its RTP send before the first one's has gone out.
        /// The cost of waiting here is nothing at human typing speed.
        /// </summary>
        private readonly SemaphoreSlim _dtmfSendGate = new(1, 1);

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

        /// <summary>
        /// Echoes the digit and forwards it to SipService — but only for the operator to
        /// see once it is actually true. The echo used to be unconditional, which is what
        /// let a press taken mid-ring (see DtmfPadPresenter) look sent when it never left
        /// this view.
        /// </summary>
        private async Task SendDtmfAsync(char digit)
        {
            // Mirrors TransferToLeadOwner's defensive re-check: IsEnabled already keeps a
            // non-Active call from reaching this (see UpdateDtmfPadEnabled), but the
            // handler asks DtmfPadPresenter again — against the live state, not a cached
            // one — rather than trusting a button never fires stale.
            if (!DtmfPadPresenter.CanSend(App.SipService.State)) return;

            var echo = this.FindControl<TextBlock>("DtmfEchoLabel");
            if (echo != null) echo.Text += digit;

            await _dtmfSendGate.WaitAsync();
            try
            {
                await App.SipService.SendDtmfAsync(digit);
            }
            finally
            {
                _dtmfSendGate.Release();
            }
        }

        /// <summary>
        /// Paints the pad from the SIP service's live CallState. Called everywhere
        /// UpdateHoldUI is — SetStatus (the server confirming an Active/OnHold
        /// transition) and ToggleHold (the operator's own Hold/Resume button) — so the
        /// pad repaints on both of the transitions that change what it shows, including
        /// while it is already open. It reads state fresh each time rather than from
        /// this view's own _onHold mirror, which only ever distinguishes Active from
        /// OnHold and has nothing to say about Idle/Ringing/IncomingRinging — the gap
        /// that let a dial-out echo tones nobody sent (see DtmfPadPresenter).
        ///
        /// A press already echoed here proves nothing once the gate closes — Idle/
        /// Ringing/IncomingRinging never sent it, and OnHold cannot send it now — so the
        /// echo is cleared here too, not only when the panel is closed. Otherwise a
        /// digit typed a moment ago can sit under a hint saying it cannot be sent.
        /// </summary>
        private void UpdateDtmfPadEnabled()
        {
            var state = App.SipService.State;
            var canSend = DtmfPadPresenter.CanSend(state);

            var hint = this.FindControl<TextBlock>("DtmfHintLabel");
            if (hint != null)
            {
                var key = DtmfPadPresenter.HintKey(state);
                hint.Text = key == null ? string.Empty : Services.I18nService.Instance.Get(key);
                hint.IsVisible = key != null;
            }

            if (!canSend)
            {
                var echo = this.FindControl<TextBlock>("DtmfEchoLabel");
                if (echo != null) echo.Text = string.Empty;
            }

            var grid = this.FindControl<UniformGrid>("DtmfGrid");
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
