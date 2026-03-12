using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;

namespace OrbitalSIP
{
    public partial class MainWindow : Window
    {
        private const double WidgetSize     = 96;
        private const double ExpandedWidth  = 300;
        private const double ExpandedHeight = 480;
        private const double AnimDurationMs = 220;

        // Bottom-right corner of the window in screen pixels – stays fixed during resize
        private int _anchorX, _anchorY;
        private bool _isExpanded;

        // Animation state
        private DispatcherTimer? _animTimer;
        private double _animProgress;
        private double _fromW, _fromH, _toW, _toH;
        private Action? _onAnimComplete;

        public MainWindow()
        {
            InitializeComponent();

            // Start near bottom-right of the work area
            var workArea = Screens?.Primary?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
            var left = workArea.Right - (int)WidgetSize - 24;
            var top  = workArea.Bottom - (int)WidgetSize - 48;
            Position = new PixelPoint(left, top);

            _anchorX = left + (int)WidgetSize;
            _anchorY = top  + (int)WidgetSize;

            this.SystemDecorations = SystemDecorations.None;
            this.PointerPressed   += MainWindow_PointerPressed;
            this.PointerReleased  += MainWindow_PointerReleased;
            this.DoubleTapped     += (_, __) => ToggleExpanded();
        }

        private void MainWindow_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        }

        // After drag ends, refresh the anchor from the new position
        private void MainWindow_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_animTimer == null)
            {
                _anchorX = Position.X + (int)Width;
                _anchorY = Position.Y + (int)Height;
            }
        }

        private void ToggleExpanded()
        {
            if (_isExpanded) CollapseWidget();
            else             ExpandWidget();
        }

        private void ExpandWidget()
        {
            _isExpanded = true;

            var host = this.FindControl<ContentControl>("Host");
            if (host == null) return;

            var expanded = new Views.ExpandedView();
            expanded.OnCloseRequested += (_, __) => CollapseWidget();
            host.Content = expanded;

            // Refresh anchor from current window corner before animating
            _anchorX = Position.X + (int)Width;
            _anchorY = Position.Y + (int)Height;

            StartAnimation(Width, Height, ExpandedWidth, ExpandedHeight);
        }

        private void CollapseWidget()
        {
            _isExpanded = false;

            StartAnimation(Width, Height, WidgetSize, WidgetSize, onComplete: () =>
            {
                var host = this.FindControl<ContentControl>("Host");
                if (host != null) host.Content = new Views.WidgetView();
            });
        }

        private void StartAnimation(double fromW, double fromH,
                                    double toW,   double toH,
                                    Action? onComplete = null)
        {
            _animTimer?.Stop();
            _fromW = fromW; _fromH = fromH;
            _toW   = toW;   _toH   = toH;
            _animProgress  = 0;
            _onAnimComplete = onComplete;

            _animTimer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(16),
                DispatcherPriority.Normal,
                OnAnimTick);
            _animTimer.Start();
        }

        private void OnAnimTick(object? sender, EventArgs e)
        {
            _animProgress += 16.0 / AnimDurationMs;
            if (_animProgress >= 1.0)
            {
                _animProgress = 1.0;
                _animTimer!.Stop();
                _animTimer = null;
            }

            // Ease-out cubic
            var t = 1.0 - Math.Pow(1.0 - _animProgress, 3);
            var w = _fromW + (_toW - _fromW) * t;
            var h = _fromH + (_toH - _fromH) * t;

            // Keep bottom-right corner fixed at the anchor
            Position = new PixelPoint(_anchorX - (int)w, _anchorY - (int)h);
            Width  = w;
            Height = h;

            if (_animProgress >= 1.0)
                _onAnimComplete?.Invoke();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}

