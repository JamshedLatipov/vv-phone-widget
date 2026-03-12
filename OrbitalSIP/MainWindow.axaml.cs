using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;

namespace OrbitalSIP
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // Start positioned near bottom-right
            var workArea = Screens?.Primary?.WorkingArea ?? new PixelRect(0, 0, (int)Width, (int)Height);
            var left = workArea.Right - (int)Width - 24;
            var top = workArea.Bottom - (int)Height - 48;
            Position = new PixelPoint(left, top);

            // Ensure window is borderless (set via SystemDecorations)
            this.SystemDecorations = SystemDecorations.None;

            // Pointer pressed for drag
            this.PointerPressed += MainWindow_PointerPressed;

            // Double-click to expand widget
            this.DoubleTapped += (_, __) => ShowExpanded();
        }

        private void MainWindow_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        }

        private void ShowExpanded()
        {
            var host = this.FindControl<ContentControl>("Host");
            if (host != null)
            {
                var expanded = new Views.ExpandedView();
                expanded.OnCloseRequested += (_, __) => ShowWidget();
                host.Content = expanded;
            }
        }

        private void ShowWidget()
        {
            var host = this.FindControl<ContentControl>("Host");
            if (host != null)
            {
                host.Content = new Views.WidgetView();
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
