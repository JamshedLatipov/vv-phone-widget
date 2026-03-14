using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace OrbitalSIP.Views
{
    public partial class StatusPopupControl : UserControl
    {
        public StatusPopupControl()
        {
            InitializeComponent();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
    }
}
