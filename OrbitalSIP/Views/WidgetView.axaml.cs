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
            if (_glow == null || _ringGroup == null || _stopwatch == null) return;

            // Use a smooth sine-based value for pulsing
            var t = _stopwatch.Elapsed.TotalSeconds;
            var pulse = (Math.Sin(t * 2.0) + 1.0) / 2.0; // 0..1

            // Glow opacity (soft)
            _glow.Opacity = 0.08 + pulse * 0.22; // range ~0.08..0.3

            // Slight scale of inner group
            var scale = 1.0 + pulse * 0.02; // 1.00 .. 1.02
            var st = _ringGroup.RenderTransform as ScaleTransform;
            if (st != null)
            {
                st.ScaleX = scale;
                st.ScaleY = scale;
            }

            // Animate stroke dash offset to create wave movement along the ring
            if (_strokeRing != null)
            {
                // StrokeDashArray is "4,200" so pattern length ~=204
                var tNow = _stopwatch.Elapsed.TotalSeconds;
                var speed = 60.0; // units per second
                var offset = (tNow * speed) % 204.0;
                _strokeRing.StrokeDashOffset = offset;
            }
        }
    }
}
