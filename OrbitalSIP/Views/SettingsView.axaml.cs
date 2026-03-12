using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using OrbitalSIP.Services;

namespace OrbitalSIP.Views
{
    public partial class SettingsView : UserControl
    {
        private readonly SipSettings _settings;

        public SettingsView()
        {
            InitializeComponent();
            _settings = SipSettings.Load();
            PopulateFields();
            WireButtons();

            // Show registration errors inline while this view is visible
            App.SipService.RegistrationError += OnRegistrationError;
        }

        private void OnRegistrationError(string reason)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var lbl = this.FindControl<TextBlock>("StatusLabel");
                if (lbl == null) return;
                lbl.Text      = reason;
                lbl.IsVisible = true;
            });
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void PopulateFields()
        {
            SetText("ServerBox",      _settings.Server);
            SetText("PortBox",        _settings.Port);
            SetText("UsernameBox",    _settings.Username);
            SetText("DisplayNameBox", _settings.DisplayName);
            SetText("PasswordBox",    _settings.Password);

            var transport = this.FindControl<ComboBox>("TransportBox");
            if (transport != null)
            {
                transport.SelectedIndex = _settings.Transport switch
                {
                    "TCP" => 1,
                    "TLS" => 2,
                    _     => 0
                };
            }
        }

        private void SetText(string name, string value)
        {
            var box = this.FindControl<TextBox>(name);
            if (box != null) box.Text = value;
        }

        private string GetText(string name) =>
            this.FindControl<TextBox>(name)?.Text?.Trim() ?? "";

        private void WireButtons()
        {
            var save = this.FindControl<Button>("SaveBtn");
            if (save != null)
                save.Click += (_, __) => SaveAndClose();

            var back = this.FindControl<Button>("BackBtn");
            if (back != null)
                back.Click += (_, __) => OnBackRequested?.Invoke(this, System.EventArgs.Empty);
        }

        private void SaveAndClose()
        {
            _settings.Server      = GetText("ServerBox");
            _settings.Port        = GetText("PortBox");
            _settings.Username    = GetText("UsernameBox");
            _settings.DisplayName = GetText("DisplayNameBox");
            _settings.Password    = GetText("PasswordBox");

            var transport = this.FindControl<ComboBox>("TransportBox");
            _settings.Transport = transport?.SelectedIndex switch
            {
                1 => "TCP",
                2 => "TLS",
                _ => "UDP"
            };

            _settings.Save();
            OnBackRequested?.Invoke(this, System.EventArgs.Empty);
        }

        public event System.EventHandler? OnBackRequested;
    }
}
