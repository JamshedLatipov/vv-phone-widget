using System;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;

namespace OrbitalSIP.Views
{
    public partial class ActiveCallView : UserControl
    {
        private DispatcherTimer? _timer;
        private TimeSpan _elapsed = TimeSpan.Zero;
        private bool _muted;

        public ActiveCallView()
            : this("Unknown", false)
        {
        }

        public ActiveCallView(string callerId, bool isOutgoing = false)
        {
            InitializeComponent();

            var callerLabel  = this.FindControl<TextBlock>("CallerLabel");
            var statusLabel  = this.FindControl<TextBlock>("StatusLabel");
            if (callerLabel != null) callerLabel.Text = callerId;
            if (statusLabel != null) statusLabel.Text = isOutgoing ? "CALLING…" : "CONNECTED";

            WireButtons();
            StartTimer();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        // ── Timer ─────────────────────────────────────────────────────
        private void StartTimer()
        {
            _timer = new DispatcherTimer(
                TimeSpan.FromSeconds(1),
                DispatcherPriority.Normal,
                OnTick);
            _timer.Start();
        }

        private void OnTick(object? sender, EventArgs e)
        {
            _elapsed = _elapsed.Add(TimeSpan.FromSeconds(1));
            var label = this.FindControl<TextBlock>("TimerLabel");
            if (label != null)
                label.Text = _elapsed.TotalHours >= 1
                    ? _elapsed.ToString(@"h\:mm\:ss")
                    : _elapsed.ToString(@"mm\:ss");
        }

        public void MarkConnected()
        {
            var label = this.FindControl<TextBlock>("StatusLabel");
            if (label != null) label.Text = "CONNECTED";
        }

        // ── Buttons ───────────────────────────────────────────────────
        private void WireButtons()
        {
            var hangup = this.FindControl<Button>("HangupBtn");
            if (hangup != null)
                hangup.Click += (_, __) =>
                {
                    _timer?.Stop();
                    OnHangup?.Invoke(this, EventArgs.Empty);
                };

            var mute = this.FindControl<Button>("MuteBtn");
            if (mute != null)
                mute.Click += (_, __) => ToggleMute();

            var keypad = this.FindControl<Button>("KeypadBtn");
            if (keypad != null)
                keypad.Click += (_, __) => OnKeypadRequested?.Invoke(this, EventArgs.Empty);
        }

        private void ToggleMute()
        {
            _muted = !_muted;
            OnMuteToggled?.Invoke(this, _muted);

            // Update icon colour to signal muted state
            var icon  = this.FindControl<AvaloniaPath>("MuteIcon");
            var label = this.FindControl<TextBlock>("MuteLabel");
            var btn   = this.FindControl<Button>("MuteBtn");

            if (icon  != null) icon.Fill  = new SolidColorBrush(_muted ? Color.Parse("#FF4444") : Color.Parse("#8FA6BE"));
            if (label != null) label.Text  = _muted ? "Unmute" : "Mute";
            if (btn   != null) btn.Background = new SolidColorBrush(_muted ? Color.Parse("#3A1010") : Color.Parse("#1A2D42"));
        }

        // ── Events ────────────────────────────────────────────────────
        public event EventHandler?       OnHangup;
        public event EventHandler<bool>? OnMuteToggled;   // arg = isMuted
        public event EventHandler?       OnKeypadRequested;
    }
}
