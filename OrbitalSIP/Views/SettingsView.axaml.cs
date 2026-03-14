using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using NAudio.Wave;
using OrbitalSIP.Services;

namespace OrbitalSIP.Views
{
    public partial class SettingsView : UserControl
    {
        private readonly SipSettings _settings;

        public SettingsView()
        {
            InitializeComponent();

            // Load persistent settings
            _settings = SipSettings.Load();

            // Re-apply in-memory credentials from the active session (if any)
            var current = App.SipService.CurrentSettings;
            if (!string.IsNullOrEmpty(current.Username))
            {
                _settings.Username = current.Username;
                _settings.Password = current.Password;
            }

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
            SetText("BackendUrlBox",   _settings.BackendUrl);
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

            PopulateAudioDevices();
        }

        private void PopulateAudioDevices()
        {
            var speakerBox = this.FindControl<ComboBox>("SpeakerBox");
            var micBox     = this.FindControl<ComboBox>("MicBox");
            if (speakerBox == null || micBox == null) return;

            // Build output device list  (-1 = system default)
            var outItems = new List<string> { "System Default" };
            for (int i = 0; i < WaveOut.DeviceCount; i++)
                outItems.Add(WaveOut.GetCapabilities(i).ProductName);
            speakerBox.ItemsSource  = outItems;
            // Saved index -1 → list position 0; index N → list position N+1
            speakerBox.SelectedIndex = _settings.AudioOutDeviceIndex + 1;
            if (speakerBox.SelectedIndex < 0) speakerBox.SelectedIndex = 0;

            // Build input device list  (-1 = system default)
            var inItems = new List<string> { "System Default" };
            for (int i = 0; i < WaveIn.DeviceCount; i++)
                inItems.Add(WaveIn.GetCapabilities(i).ProductName);
            micBox.ItemsSource   = inItems;
            micBox.SelectedIndex = _settings.AudioInDeviceIndex + 1;
            if (micBox.SelectedIndex < 0) micBox.SelectedIndex = 0;
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
            var bottomNav = this.FindControl<BottomNavControl>("BottomNav");
            if (bottomNav != null) { bottomNav.OnSettingsRequested += (_, __) => OnBackRequested?.Invoke(this, System.EventArgs.Empty); bottomNav.SetActiveTab("Settings"); }
            if (back != null)
                back.Click += (_, __) => OnBackRequested?.Invoke(this, System.EventArgs.Empty);
        }

        private void SaveAndClose()
        {
            _settings.BackendUrl  = GetText("BackendUrlBox");
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

            // Audio device indices: list position 0 → device -1 (default), N+1 → device N
            var speakerBox = this.FindControl<ComboBox>("SpeakerBox");
            if (speakerBox != null)
                _settings.AudioOutDeviceIndex = (speakerBox.SelectedIndex <= 0 ? -1 : speakerBox.SelectedIndex - 1);

            var micBox = this.FindControl<ComboBox>("MicBox");
            if (micBox != null)
                _settings.AudioInDeviceIndex = (micBox.SelectedIndex <= 0 ? -1 : micBox.SelectedIndex - 1);

            _settings.Save();
            OnBackRequested?.Invoke(this, System.EventArgs.Empty);
        }

        public event System.EventHandler? OnBackRequested;
    }
}
