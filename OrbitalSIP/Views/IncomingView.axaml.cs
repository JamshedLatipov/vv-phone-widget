using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System.Threading.Tasks;

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
            var copy = this.FindControl<Button>("CopyCallerBtn");
            if (copy != null) copy.Click += async (_, __) => await CopyCallerAsync();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public void SetCaller(string callerId)
        {
            var caller = this.FindControl<TextBlock>("CallerText");
            if (caller != null)
            {
                caller.Text = string.IsNullOrWhiteSpace(callerId) ? Services.I18nService.Instance.Get("UnknownCaller") : callerId;
            }
        }

        private async Task CopyCallerAsync()
        {
            var caller = this.FindControl<TextBlock>("CallerText")?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(caller))
            {
                return;
            }

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard == null)
            {
                return;
            }

            await topLevel.Clipboard.SetTextAsync(caller);
            await FlashCopyButtonAsync("CopyCallerBtn");
        }

        private async Task FlashCopyButtonAsync(string buttonName)
        {
            var button = this.FindControl<Button>(buttonName);
            if (button == null)
            {
                return;
            }

            var original = button.Content;
            button.Content = Services.I18nService.Instance.Get("Copied");
            await Task.Delay(1200);
            button.Content = original;
        }

        public event System.EventHandler? OnAnswer;
        public event System.EventHandler? OnDecline;
    }
}
