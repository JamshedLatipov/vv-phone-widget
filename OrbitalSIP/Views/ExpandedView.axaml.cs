using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;

namespace OrbitalSIP.Views
{
    public partial class ExpandedView : UserControl
    {
        public ExpandedView()
        {
            InitializeComponent();
            WireButtons();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void WireButtons()
        {
            // Header buttons
            Bind("CloseBtn",    () => OnCloseRequested?.Invoke(this, EventArgs.Empty));
            Bind("SettingsBtn", () => OnSettingsRequested?.Invoke(this, EventArgs.Empty));

            // Backspace
            Bind("BackspaceBtn", () =>
            {
                var d = this.FindControl<TextBlock>("DisplayText");
                if (d != null && d.Text?.Length > 0)
                    d.Text = d.Text[..^1];
            });

            // Call button
            Bind("CallBtn", () =>
            {
                var d = this.FindControl<TextBlock>("DisplayText");
                var num = d?.Text?.Trim() ?? "";
                if (num.Length > 0)
                    OutgoingCallRequested?.Invoke(this, num);
            });

            // Dial pad digits
            var pad = this.FindControl<UniformGrid>("DialPad");
            if (pad == null) return;
            foreach (var child in pad.Children)
            {
                if (child is Button btn)
                {
                    var digit = btn.Content?.ToString() ?? "";
                    btn.Click += (_, __) =>
                    {
                        var d = this.FindControl<TextBlock>("DisplayText");
                        if (d != null) d.Text = (d.Text ?? "") + digit;
                    };
                }
            }
        }

        private void Bind(string name, Action action)
        {
            var btn = this.FindControl<Button>(name);
            if (btn != null) btn.Click += (_, __) => action();
        }

        // ── Events ────────────────────────────────────────────────────
        public event System.EventHandler?        OnCloseRequested;
        public event System.EventHandler?        OnSettingsRequested;
        /// <summary>Fired when the user presses the call button. Arg = dialled number.</summary>
        public event EventHandler<string>? OutgoingCallRequested;
    }
}

