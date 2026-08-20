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

        /// <summary>
        /// The call clock. A stopwatch rather than a per-tick accumulator: MainWindow
        /// always routes this widget through StartAnimation, which parents it into
        /// OverlayHost and then moves it to Host — a detach/attach pair that stops the
        /// timer. A counting clock would lose that time; this one only loses the repaint,
        /// which OnAttachedToVisualTree restores.
        /// </summary>
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

        /// <summary>Time this call had already run when the widget was built.</summary>
        private readonly TimeSpan _initialElapsed;

        public TimeSpan Elapsed => _initialElapsed + _clock.Elapsed;
        private bool _muted;
        private bool _onHold;

        public ActiveCallWidgetView() : this("Unknown", TimeSpan.Zero) { }

        public ActiveCallWidgetView(string callerId, TimeSpan initialElapsed, bool isMuted = false, bool isOnHold = false)
        {
            InitializeComponent();
            var caller = this.FindControl<TextBlock>("CallerText");
            if (caller != null) caller.Text = callerId;

            _initialElapsed = initialElapsed;
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
            if (statusText != null) statusText.Text = isOnHold ? Services.I18nService.Instance.Get("OnHold") : Services.I18nService.Instance.Get("ActiveCall");
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        /// <summary>
        /// MainWindow builds a NEW widget on every hangup and every expand back out of
        /// the mini view, and a running DispatcherTimer keeps the old one rooted in the
        /// dispatcher queue: it goes on ticking against a detached tree for the rest of
        /// the session, one more leak per cycle.
        ///
        /// Only the repaint stops here — <see cref="Elapsed"/> comes from the stopwatch —
        /// and <see cref="OnAttachedToVisualTree"/> starts it again for the half of these
        /// detaches that are really the animation reparenting this widget.
        /// </summary>
        protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            _timer?.Stop();
            _timer = null;
            base.OnDetachedFromVisualTree(e);
        }

        protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (_timer == null) StartTimer();
        }

        private void StartTimer()
        {
            _timer = new DispatcherTimer(
                TimeSpan.FromSeconds(1),
                DispatcherPriority.Render,
                OnTick);
            _timer.Start();
        }

        private void OnTick(object? sender, EventArgs e) => UpdateTimeUI();

        private void UpdateTimeUI()
        {
            var timerText = this.FindControl<TextBlock>("TimerText");
            if (timerText != null)
            {
                var elapsed = Elapsed;
                timerText.Text = elapsed.TotalHours >= 1
                    ? elapsed.ToString(@"h\:mm\:ss")
                    : elapsed.ToString(@"mm\:ss");
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
