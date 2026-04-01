using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
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

            var langBox = this.FindControl<ComboBox>("LanguageBox");
            if (langBox != null)
            {
                langBox.SelectedIndex = _settings.Language switch
                {
                    "uz" => 1,
                    "kk" => 2,
                    "tg" => 3,
                    _    => 0
                };
            }

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
            PopulateHotkeyFields();
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

        // ── Hotkey fields ─────────────────────────────────────────────
        private void PopulateHotkeyFields()
        {
            SetText("HotkeyMuteBox",   _settings.HotkeyMute);
            SetText("HotkeyHoldBox",   _settings.HotkeyHold);
            SetText("HotkeyHangupBox", _settings.HotkeyHangup);
            SetText("HotkeyAnswerBox", _settings.HotkeyAnswer);

            WireHotkeyBox("HotkeyMuteBox");
            WireHotkeyBox("HotkeyHoldBox");
            WireHotkeyBox("HotkeyHangupBox");
            WireHotkeyBox("HotkeyAnswerBox");
        }

        private void WireHotkeyBox(string name)
        {
            var box = this.FindControl<TextBox>(name);
            if (box == null) return;

            box.GotFocus += (_, __) =>
            {
                box.Text = Services.I18nService.Instance.Get("HotkeyPressKey");
                box.Foreground = Avalonia.Media.Brushes.Gray;
            };

            box.KeyDown += (_, e) =>
            {
                // Ignore lone modifier keys
                if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
                          or Key.LeftAlt  or Key.RightAlt  or Key.LWin     or Key.RWin)
                    return;

                e.Handled = true;
                var combo = BuildComboString(e.KeyModifiers, e.Key);
                if (combo != null)
                {
                    box.Text       = combo;
                    box.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#17E0A0"));
                    this.FindControl<Button>("SaveBtn")?.Focus();
                }
                else
                {
                    // Unsupported key — restore previous value
                    box.Text       = name switch
                    {
                        "HotkeyMuteBox"   => _settings.HotkeyMute,
                        "HotkeyHoldBox"   => _settings.HotkeyHold,
                        "HotkeyHangupBox" => _settings.HotkeyHangup,
                        "HotkeyAnswerBox" => _settings.HotkeyAnswer,
                        _                 => box.Text
                    };
                    box.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#17E0A0"));
                    this.FindControl<Button>("SaveBtn")?.Focus();
                }
            };
        }

        /// <summary>Converts an Avalonia key + modifiers to a string like "Ctrl+M" or "Escape".</summary>
        private static string? BuildComboString(KeyModifiers mods, Key key)
        {
            bool ctrl = mods.HasFlag(KeyModifiers.Control);
            bool alt  = mods.HasFlag(KeyModifiers.Alt);
            string? keyName = key switch
            {
                Key.Escape               => "Escape",
                Key.Return or Key.Enter  => "Enter",
                Key.Space                => "Space",
                >= Key.F1 and <= Key.F12 => key.ToString(),
                >= Key.A  and <= Key.Z   => key.ToString(),
                _                        => null
            };
            if (keyName == null) return null;
            if (ctrl) return $"Ctrl+{keyName}";
            if (alt)  return $"Alt+{keyName}";
            return keyName;
        }

        private void WireButtons()
        {
            var save = this.FindControl<Button>("SaveBtn");
            if (save != null)
                save.Click += (_, __) => SaveAndClose();

            var updateBtn    = this.FindControl<Button>("CheckUpdateBtn");
            var updateStatus = this.FindControl<TextBlock>("UpdateStatusLabel");
            if (updateBtn != null)
            {
                // Set initial label based on whether a silent-check already found an update.
                RefreshUpdateBtnText(updateBtn);

                // If the silent check fires while Settings is open, update the button live.
                App.Updater.UpdateAvailable += () =>
                    Dispatcher.UIThread.InvokeAsync(() => RefreshUpdateBtnText(updateBtn));

                updateBtn.Click += async (_, __) =>
                {
                    updateBtn.IsEnabled = false;
                    await App.Updater.CheckAndUpdateAsync(text =>
                    {
                        if (updateStatus != null) updateStatus.Text = text;
                    });
                    RefreshUpdateBtnText(updateBtn);
                    updateBtn.IsEnabled = true;
                };
            }

            var topBar = this.FindControl<TopBarControl>("TopBar");
            if (topBar != null)
            {
                topBar.SetTitle("Settings");
                topBar.OnMinimizeRequested += (_, __) => OnMinimizeRequested?.Invoke(this, System.EventArgs.Empty);
                topBar.OnAvatarClicked += (_, __) => OnAvatarClicked?.Invoke(this, System.EventArgs.Empty);
                topBar.OnCloseRequested += (_, __) => OnExitAppRequested?.Invoke(this, System.EventArgs.Empty);
            }
            var bottomNav = this.FindControl<BottomNavControl>("BottomNav");
            if (bottomNav != null)
            {
                bottomNav.OnDialerRequested += (_, __) => OnBackRequested?.Invoke(this, System.EventArgs.Empty);
                bottomNav.SetActiveTab("Settings");
            }
        }

        private static void RefreshUpdateBtnText(Button btn)
        {
            var i18n = I18nService.Instance;
            btn.Content = App.Updater.HasUpdate
                ? i18n.Get("InstallUpdate")
                : i18n.Get("CheckForUpdates");
        }

        private void SaveAndClose()
        {
            _settings.BackendUrl  = GetText("BackendUrlBox");
            _settings.Server      = GetText("ServerBox");
            _settings.Port        = GetText("PortBox");

            var langBox = this.FindControl<ComboBox>("LanguageBox");
            _settings.Language = langBox?.SelectedIndex switch
            {
                1 => "uz",
                2 => "kk",
                3 => "tg",
                _ => "ru"
            };
            Services.I18nService.Instance.LoadLanguage(_settings.Language);

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

            // Hotkeys – only persist if the text is a valid combo
            SaveHotkey("HotkeyMuteBox",   v => _settings.HotkeyMute   = v);
            SaveHotkey("HotkeyHoldBox",   v => _settings.HotkeyHold   = v);
            SaveHotkey("HotkeyHangupBox", v => _settings.HotkeyHangup = v);
            SaveHotkey("HotkeyAnswerBox", v => _settings.HotkeyAnswer = v);

            _settings.Save();
            App.GlobalHotkeys.ApplySettings(_settings);
            OnSaveRequested?.Invoke(this, System.EventArgs.Empty);
        }

        private void SaveHotkey(string boxName, System.Action<string> apply)
        {
            var text = GetText(boxName);
            if (GlobalHotkeyService.IsValidHotkey(text))
                apply(text);
        }

        public event System.EventHandler? OnBackRequested;
        public event System.EventHandler? OnMinimizeRequested;
        public event System.EventHandler? OnSaveRequested;
        public event System.EventHandler? OnAvatarClicked;
        public event System.EventHandler? OnExitAppRequested;
    }
}
