using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using System.Threading.Tasks;
using Material.Icons;
using Material.Icons.Avalonia;
using OrbitalSIP.Services;

namespace OrbitalSIP.Views
{
    public partial class ExpandedView : UserControl
    {
        /// <summary>
        /// True while a call is up, so the dialer refuses to start a second one.
        ///
        /// SipService has always refused the second call, and MainWindow.StartOutgoingCall
        /// guards it too — but both refuse in silence, and the operator pressing a green,
        /// enabled button and getting nothing has no way to tell a blocked action from a
        /// broken one. Until a second line means something, saying no out loud is the
        /// honest answer.
        ///
        /// Pushed in by MainWindow rather than read from SipService here: this view is
        /// rebuilt on every navigation, and a subscription per rebuild is the leak the
        /// window-wide state exists to avoid.
        /// </summary>
        private bool _dialingBlocked;

        public ExpandedView()
        {
            InitializeComponent();
            WireButtons();
        }

        /// <summary>Tells the dialer whether a call is already up. Idempotent.</summary>
        public void SetDialingBlocked(bool blocked)
        {
            _dialingBlocked = blocked;
            RefreshCallButton();
        }

        /// <summary>
        /// The one place the call button's state is decided, from the two things that decide
        /// it: something to dial, and no call already running. Three separate assignments
        /// used to set IsEnabled from the text alone, so whichever ran last won.
        /// </summary>
        private void RefreshCallButton()
        {
            var callBtn = this.FindControl<Button>("CallBtn");
            if (callBtn == null) return;

            var typed = this.FindControl<TextBox>("DisplayText")?.Text;
            callBtn.IsEnabled = !string.IsNullOrWhiteSpace(typed) && !_dialingBlocked;

            ToolTip.SetTip(callBtn, _dialingBlocked
                ? I18nService.Instance.Get("DialBlockedDuringCall")
                : null);
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void WireButtons()
        {
            BindAsync("CopyBtn", CopyDisplayedNumberAsync);

            // Backspace
            Bind("BackspaceBtn", () =>
            {
                var d = this.FindControl<TextBox>("DisplayText");
                if (d != null && d.Text?.Length > 0)
                {
                    d.Text = d.Text[..^1];
                    d.CaretIndex = d.Text.Length;
                }
                d?.Focus();
            });

            // Call button
            Bind("CallBtn", () =>
            {
                if (_dialingBlocked) return;

                var d = this.FindControl<TextBox>("DisplayText");
                var num = d?.Text?.Trim() ?? "";
                if (num.Length > 0)
                    OutgoingCallRequested?.Invoke(this, num);
            });

            // Enter key on display field triggers call; the button's state follows the text
            var display = this.FindControl<TextBox>("DisplayText");
            if (display != null)
            {
                RefreshCallButton();

                display.TextChanged += (_, __) => RefreshCallButton();

                display.KeyDown += (_, e) =>
                {
                    if (e.Key == Avalonia.Input.Key.Enter)
                    {
                        e.Handled = true;

                        // Enter goes around the button, so the button being disabled does not
                        // stop it. Holding Enter autorepeats, which is how this path found its
                        // first guard in the first place.
                        if (_dialingBlocked) return;

                        var num = display.Text?.Trim() ?? "";
                        if (num.Length > 0)
                            OutgoingCallRequested?.Invoke(this, num);
                    }
                };
            }

            // Dial pad digits
            var pad = this.FindControl<UniformGrid>("DialPad");
            if (pad == null) return;
            foreach (var child in pad.Children)
            {
                if (child is Button btn)
                {
                    var digit = btn.Tag?.ToString() ?? btn.Content?.ToString() ?? "";
                    btn.Click += (_, __) =>
                    {
                        var d = this.FindControl<TextBox>("DisplayText");
                        if (d != null)
                        {
                            d.Text = (d.Text ?? "") + digit;
                            d.CaretIndex = d.Text.Length;
                        }
                        d?.Focus();
                    };
                }
            }
        }

        private void Bind(string name, Action action)
        {
            var btn = this.FindControl<Button>(name);
            if (btn != null) btn.Click += (_, __) => action();
        }

        private void BindAsync(string name, Func<Task> action)
        {
            var btn = this.FindControl<Button>(name);
            if (btn != null) btn.Click += async (_, __) => await action();
        }

        private async Task CopyDisplayedNumberAsync()
        {
            var text = this.FindControl<TextBox>("DisplayText")?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard == null)
            {
                return;
            }

            await topLevel.Clipboard.SetTextAsync(text);

            await IconFlash.ConfirmAsync(this.FindControl<Button>("CopyBtn")?.Content);
        }

        // ── Events ────────────────────────────────────────────────────
        /// <summary>Fired when the user presses the call button. Arg = dialled number.</summary>
        public event EventHandler<string>? OutgoingCallRequested;
    }
}
