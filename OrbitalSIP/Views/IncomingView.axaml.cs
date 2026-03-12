using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace OrbitalSIP.Views
{
    public partial class IncomingView : UserControl
    {
        public IncomingView()
        {
            InitializeComponent();
            var ans = this.FindControl<Button>("AnswerBtn");
            if (ans != null) ans.Click += (_, __) => OnAnswer?.Invoke(this, System.EventArgs.Empty);
            var dec = this.FindControl<Button>("DeclineBtn");
            if (dec != null) dec.Click += (_, __) => OnDecline?.Invoke(this, System.EventArgs.Empty);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public event System.EventHandler? OnAnswer;
        public event System.EventHandler? OnDecline;
    }
}
