using System;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;

namespace OrbitalSIP.Views
{
    public partial class ActiveCallWidgetView : UserControl
    {
        private DispatcherTimer? _timer;
        private TimeSpan _elapsed = TimeSpan.Zero;
        public TimeSpan Elapsed => _elapsed;
        private bool _muted;
        private bool _onHold;

        public ActiveCallWidgetView() : this("Unknown", TimeSpan.Zero) { }

        public ActiveCallWidgetView(string callerId, TimeSpan initialElapsed, bool isMuted = false, bool isOnHold = false)
        {
            InitializeComponent();
            var caller = this.FindControl<TextBlock>("CallerText");
            if (caller != null) caller.Text = callerId;

            _elapsed = initialElapsed;
            _muted = isMuted;
            _onHold = isOnHold;

            UpdateMuteUI();
            UpdateHoldUI();
            SetStatus(_onHold);
            UpdateTimeUI();
            WireButtons();
            StartTimer();

            this.DoubleTapped += (_, __) => OnExpandRequested?.Invoke(this, EventArgs.Empty);
        }

        public void SetStatus(bool isOnHold)
        {
            var statusText = this.FindControl<TextBlock>("StatusText");
            if (statusText != null) statusText.Text = isOnHold ? "ON HOLD" : "ACTIVE CALL";
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void StartTimer()
        {
            _timer = new DispatcherTimer(
                TimeSpan.FromSeconds(1),
                DispatcherPriority.Render,
                OnTick);
            _timer.Start();
        }

        private void OnTick(object? sender, EventArgs e)
        {
            _elapsed = _elapsed.Add(TimeSpan.FromSeconds(1));
            UpdateTimeUI();
        }

        private void UpdateTimeUI()
        {
            var timerText = this.FindControl<TextBlock>("TimerText");
            if (timerText != null)
            {
                timerText.Text = _elapsed.TotalHours >= 1
                    ? _elapsed.ToString(@"h\:mm\:ss")
                    : _elapsed.ToString(@"mm\:ss");
            }
        }

        private void WireButtons()
        {
            var mute = this.FindControl<Button>("MuteBtn");
            if (mute != null) mute.Click += (_, __) => ToggleMute();

            var hold = this.FindControl<Button>("HoldBtn");
            if (hold != null) hold.Click += (_, __) => ToggleHold();

            var transfer = this.FindControl<Button>("TransferBtn");
            if (transfer != null) transfer.Click += (_, __) => OnTransferRequested?.Invoke(this, EventArgs.Empty);

            var hangup = this.FindControl<Button>("HangupBtn");
            if (hangup != null) hangup.Click += (_, __) => OnHangup?.Invoke(this, EventArgs.Empty);
        }

        private void ToggleMute()
        {
            _muted = !_muted;
            UpdateMuteUI();
            OnMuteToggled?.Invoke(this, _muted);
        }

        private void UpdateMuteUI()
        {
            var icon = this.FindControl<AvaloniaPath>("MuteIcon");
            var btn = this.FindControl<Button>("MuteBtn");
            if (icon != null) icon.Fill = new SolidColorBrush(_muted ? Color.Parse("#FFFFFF") : Color.Parse("#DDE7F3"));
            if (btn != null) btn.Background = new SolidColorBrush(_muted ? Color.Parse("#B91C1C") : Color.Parse("#1A2D42"));
        }

        private void ToggleHold()
        {
            _onHold = !_onHold;
            UpdateHoldUI();
            SetStatus(_onHold);
            OnHoldToggled?.Invoke(this, _onHold);
        }

        private void UpdateHoldUI()
        {
            var btn = this.FindControl<Button>("HoldBtn");
            if (btn != null) btn.Background = new SolidColorBrush(_onHold ? Color.Parse("#B91C1C") : Color.Parse("#1E4270"));
        }

        public event EventHandler? OnHangup;
        public event EventHandler<bool>? OnMuteToggled;
        public event EventHandler<bool>? OnHoldToggled;
        public event EventHandler? OnTransferRequested;
        public event EventHandler? OnExpandRequested;
    }
}
