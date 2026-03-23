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
            if (topBar != null) { topBar.OnMinimizeRequested += (_, __) => OnCloseRequested?.Invoke(this, EventArgs.Empty); topBar.OnAvatarClicked += (_, __) => OnAvatarClicked?.Invoke(this, EventArgs.Empty); topBar.OnCloseRequested += (_, __) => OnExitAppRequested?.Invoke(this, EventArgs.Empty); }
            var bottomNav = this.FindControl<BottomNavControl>("BottomNav");
            if (bottomNav != null) if (bottomNav != null) bottomNav.OnSettingsRequested += (_, __) => OnSettingsRequested?.Invoke(this, EventArgs.Empty);
            if (bottomNav != null) bottomNav.OnRecentsRequested += (_, __) => OnRecentsRequested?.Invoke(this, EventArgs.Empty);
            bottomNav?.SetActiveTab("Dialer");
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
            });

            // Call button
            Bind("CallBtn", () =>
            {
                var d = this.FindControl<TextBox>("DisplayText");
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
                        var d = this.FindControl<TextBox>("DisplayText");
                        if (d != null)
                        {
                            d.Text = (d.Text ?? "") + digit;
                            d.CaretIndex = d.Text.Length;
                        }
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

            var button = this.FindControl<Button>("CopyBtn");
            if (button == null)
            {
                return;
            }

            if (button.Content is MaterialIcon icon)
            {
                var originalKind = icon.Kind;
                icon.Kind = MaterialIconKind.Check;
                await Task.Delay(1200);
                icon.Kind = originalKind;
            }
        }

        // ── Events ────────────────────────────────────────────────────
        public event System.EventHandler?        OnCloseRequested;
        public event EventHandler? OnAvatarClicked;
        public event System.EventHandler?        OnSettingsRequested;
        public event System.EventHandler?        OnRecentsRequested;
        public event System.EventHandler?        OnExitAppRequested;
        /// <summary>Fired when the user presses the call button. Arg = dialled number.</summary>
        public event EventHandler<string>? OutgoingCallRequested;
    }
}
