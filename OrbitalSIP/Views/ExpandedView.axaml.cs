using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace OrbitalSIP.Views
{
    public partial class ExpandedView : UserControl
    {
        public ExpandedView()
        {
            InitializeComponent();
            var close = this.FindControl<Button>("CloseBtn");
            if (close != null)
            {
                close.Click += (_, __) => OnCloseRequested?.Invoke(this, System.EventArgs.Empty);
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public event System.EventHandler? OnCloseRequested;
    }
}
