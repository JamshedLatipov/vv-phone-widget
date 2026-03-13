using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Markup.Xaml;
using System.Diagnostics;
using OrbitalSIP.Services;

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
            sip.RegistrationStatusChanged += state =>
                Dispatcher.UIThread.InvokeAsync(() => UpdateStatus(state));
            sip.RegistrationError += reason =>
                Dispatcher.UIThread.InvokeAsync(() => UpdateStatusTip(sip.RegistrationStatus, reason));

            UpdateStatus(sip.RegistrationStatus);
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void OnPulseTick(object? sender, EventArgs e)
        {
            if (_stopwatch == null || _strokeRing == null) return;

            var pulse = (Math.Sin(_stopwatch.Elapsed.TotalSeconds * 2.2) + 1.0) / 2.0;
            _strokeRing.Opacity        = 0.35 + pulse * 0.65;
            _strokeRing.StrokeThickness = 4.0 + pulse * 4.0;
        }

        private void UpdateStatus(RegistrationState state)
        {
            if (_statusDot == null) return;

            Color color;
            string label;

            switch (state)
            {
                case RegistrationState.Registered:
                    color = Color.Parse("#10B981"); // Emerald
                    label = "Registered";
                    break;
                case RegistrationState.Failed:
                    color = Color.Parse("#EF4444"); // Red
                    label = App.SipService.LastRegistrationError;
                    break;
                case RegistrationState.Paused:
                    color = Color.Parse("#F59E0B"); // Amber
                    label = "Paused";
                    break;
                case RegistrationState.Unregistered:
                default:
                    color = Color.Parse("#89A0B8"); // Slate/Gray
                    label = "Offline";
                    break;
            }

            _statusDot.Fill = new SolidColorBrush(color);
            UpdateStatusTip(state, label);
        }

        private void UpdateStatusTip(RegistrationState state, string message)
        {
            var tip = this.FindControl<Avalonia.Controls.TextBlock>("StatusTip");
            if (tip == null) return;

            if (string.IsNullOrWhiteSpace(message))
            {
                tip.Text = state switch
                {
                    RegistrationState.Registered => "Registered",
                    RegistrationState.Failed => "Registration Failed",
                    RegistrationState.Paused => "Paused",
                    _ => "Offline"
                };
            }
            else
            {
                tip.Text = message;
            }
        }
    }
}
