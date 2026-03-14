using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using System.Threading.Tasks;
using OrbitalSIP.Services;

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
            var topBar = this.FindControl<TopBarControl>("TopBar");
            if (topBar != null) topBar.OnMinimizeRequested += (_, __) => OnCloseRequested?.Invoke(this, EventArgs.Empty);
            var bottomNav = this.FindControl<BottomNavControl>("BottomNav");
            if (bottomNav != null) bottomNav.OnSettingsRequested += (_, __) => OnSettingsRequested?.Invoke(this, EventArgs.Empty);
            bottomNav?.SetActiveTab("Dialer");
            BindAsync("CopyBtn", CopyDisplayedNumberAsync);

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
                    var digit = btn.Tag?.ToString() ?? btn.Content?.ToString() ?? "";
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

        private void BindAsync(string name, Func<Task> action)
        {
            var btn = this.FindControl<Button>(name);
            if (btn != null) btn.Click += async (_, __) => await action();
        }

        private async Task CopyDisplayedNumberAsync()
        {
            var text = this.FindControl<TextBlock>("DisplayText")?.Text?.Trim() ?? string.Empty;
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

            var button = this.FindControl<Button>("CopyBtn");
            if (button == null)
            {
                return;
            }

            var original = button.Content;
            button.Content = "Copied";
            await Task.Delay(1200);
            button.Content = original;
        }

        // ── Events ────────────────────────────────────────────────────
        public event System.EventHandler?        OnCloseRequested;
        public event System.EventHandler?        OnSettingsRequested;
        /// <summary>Fired when the user presses the call button. Arg = dialled number.</summary>
        public event EventHandler<string>? OutgoingCallRequested;
    }
}
