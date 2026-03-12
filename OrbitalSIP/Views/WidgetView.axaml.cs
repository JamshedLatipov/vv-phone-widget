using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Controls.Shapes;
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia;
using System.Diagnostics;

namespace OrbitalSIP.Views
{
    public partial class WidgetView : UserControl
    {
        private DispatcherTimer? _pulseTimer;
        private Stopwatch? _stopwatch;
        private Ellipse? _glow;
        private Grid? _ringGroup;
        private Ellipse? _strokeRing;

        public WidgetView()
        {
            InitializeComponent();
            _glow = this.FindControl<Ellipse>("GlowEllipse");
            _ringGroup = this.FindControl<Grid>("RingGroup");
            _strokeRing = this.FindControl<Ellipse>("StrokeRing");

            _stopwatch = Stopwatch.StartNew();
            _pulseTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(30), DispatcherPriority.Normal, OnPulseTick);
            _pulseTimer.Start();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OnPulseTick(object? sender, EventArgs e)
        {
            if (_stopwatch == null || _strokeRing == null) return;

            // Smooth sine pulse: 0..1
            var pulse = (Math.Sin(_stopwatch.Elapsed.TotalSeconds * 2.2) + 1.0) / 2.0;

            // Ring opacity: breathes between 0.35 and 1.0
            _strokeRing.Opacity = 0.35 + pulse * 0.65;

            // Ring thickness: grows from 4 to 8
            _strokeRing.StrokeThickness = 4.0 + pulse * 4.0;
        }
    }
}
