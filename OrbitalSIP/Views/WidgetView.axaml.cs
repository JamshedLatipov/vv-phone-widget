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
            if (_statusDot == null || _strokeRing == null) return;

            Color color;
            Color pulseColorStart;
            Color pulseColorEnd;
            string label;

            switch (state)
            {
                case RegistrationState.Registered:
                    color = Color.Parse("#10B981"); // Emerald
                    pulseColorStart = Color.Parse("#17E0A0");
                    pulseColorEnd   = Color.Parse("#00BFA5");
                    label = "Registered";
                    break;
                case RegistrationState.Failed:
                    color = Color.Parse("#EF4444"); // Red
                    pulseColorStart = Color.Parse("#F87171");
                    pulseColorEnd   = Color.Parse("#DC2626");
                    label = App.SipService.LastRegistrationError;
                    break;
                case RegistrationState.Paused:
                    color = Color.Parse("#F59E0B"); // Amber
                    pulseColorStart = Color.Parse("#FBBF24");
                    pulseColorEnd   = Color.Parse("#D97706");
                    label = "Paused";
                    break;
                case RegistrationState.Unregistered:
                default:
                    color = Color.Parse("#89A0B8"); // Slate/Gray
                    pulseColorStart = Color.Parse("#94A3B8");
                    pulseColorEnd   = Color.Parse("#64748B");
                    label = "Offline";
                    break;
            }

            _statusDot.Fill = new SolidColorBrush(color);

            _strokeRing.Stroke = new LinearGradientBrush
            {
                StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
                EndPoint = new Avalonia.RelativePoint(1, 1, Avalonia.RelativeUnit.Relative),
                GradientStops = new GradientStops
                {
                    new GradientStop(pulseColorStart, 0),
                    new GradientStop(pulseColorEnd, 1)
                }
            };

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
