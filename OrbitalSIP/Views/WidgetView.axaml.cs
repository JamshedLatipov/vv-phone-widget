using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Markup.Xaml;
using OrbitalSIP.Services;
using System.Diagnostics;

namespace OrbitalSIP.Views
{
    public partial class WidgetView : UserControl
    {
        private DispatcherTimer? _pulseTimer;
        private Stopwatch?       _stopwatch;
        private Ellipse?         _strokeRing;
        private Ellipse?         _statusDot;

        public WidgetView()
        {
            InitializeComponent();
            _strokeRing = this.FindControl<Ellipse>("StrokeRing");
            _statusDot  = this.FindControl<Ellipse>("StatusDot");

            _stopwatch  = Stopwatch.StartNew();
            _pulseTimer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(30), DispatcherPriority.Normal, OnPulseTick);
            _pulseTimer.Start();

            // Subscribe to registration state
            var sip = App.SipService;
            sip.RegistrationStateChanged += reg =>
                Dispatcher.UIThread.InvokeAsync(() => UpdateStatus(reg));
            sip.RegistrationError += reason =>
                Dispatcher.UIThread.InvokeAsync(() => UpdateStatusTip(false, reason));
            UpdateStatus(sip.IsRegistered);
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void OnPulseTick(object? sender, EventArgs e)
        {
            if (_stopwatch == null || _strokeRing == null) return;

            var pulse = (Math.Sin(_stopwatch.Elapsed.TotalSeconds * 2.2) + 1.0) / 2.0;
            _strokeRing.Opacity        = 0.35 + pulse * 0.65;
            _strokeRing.StrokeThickness = 4.0 + pulse * 4.0;
        }

        private void UpdateStatus(bool registered)
        {
            if (_statusDot == null) return;
            _statusDot.Fill = new SolidColorBrush(
                registered ? Color.Parse("#1ED760") : Color.Parse("#FF4444"));
            UpdateStatusTip(registered, registered ? "Registered" : App.SipService.LastRegistrationError);
        }

        private void UpdateStatusTip(bool registered, string message)
        {
            var tip = this.FindControl<Avalonia.Controls.TextBlock>("StatusTip");
            if (tip == null) return;
            tip.Text = string.IsNullOrWhiteSpace(message)
                ? (registered ? "Registered" : "Not registered")
                : message;
        }
    }
}

