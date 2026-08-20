using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using NAudio.Wave;
using OrbitalSIP.Models;
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

        /// <summary>
        /// Releases the subscriptions this screen takes out on process-lifetime singletons.
        /// MainWindow builds a new SettingsView on every visit, so without this each visit
        /// left a dead one attached: the next registration failure walked N of them, each
        /// posting to the dispatcher to write into its own detached StatusLabel.
        /// </summary>
        protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            App.SipService.RegistrationError -= OnRegistrationError;
            if (_updateAvailableHandler != null)
            {
                App.Updater.UpdateAvailable -= _updateAvailableHandler;
                _updateAvailableHandler = null;
            }

            base.OnDetachedFromVisualTree(e);
        }

        /// <summary>Kept in a field so the closure below can be unsubscribed by identity.</summary>
        private System.Action? _updateAvailableHandler;

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

            var scaleBox = this.FindControl<ComboBox>("WidgetScaleBox");
            if (scaleBox != null)
                scaleBox.SelectedIndex = WidgetScale.ListPosition(_settings.WidgetScalePercent);

            PopulateAudioDevices();
            PopulateHotkeyFields();
        }

        private void PopulateAudioDevices()
        {
            var speakerBox = this.FindControl<ComboBox>("SpeakerBox");
            var micBox     = this.FindControl<ComboBox>("MicBox");
            if (speakerBox == null || micBox == null) return;

            // Build output device list  (-1 = system default)
            // WaveOutDevices rather than NAudio's WaveOut: the same winmm enumeration in
            // the same order, but without pulling in NAudio.WinForms.
            var outCount = Services.Audio.WaveOutDevices.Count;
            var outItems = new List<string> { "System Default" };
            for (int i = 0; i < outCount; i++)
                outItems.Add(Services.Audio.WaveOutDevices.ProductName(i));
            AppendMissingDeviceRow(outItems, _settings.AudioOutDeviceIndex, outCount);
            speakerBox.ItemsSource   = outItems;
            speakerBox.SelectedIndex = AudioDeviceChoice.ListPosition(_settings.AudioOutDeviceIndex, outCount);

            // Build input device list  (-1 = system default)
            var inCount = WaveInEvent.DeviceCount;
            var inItems = new List<string> { "System Default" };
            for (int i = 0; i < inCount; i++)
                inItems.Add(WaveInEvent.GetCapabilities(i).ProductName);
            AppendMissingDeviceRow(inItems, _settings.AudioInDeviceIndex, inCount);
            micBox.ItemsSource   = inItems;
            micBox.SelectedIndex = AudioDeviceChoice.ListPosition(_settings.AudioInDeviceIndex, inCount);

            // Gain sliders — snap to 50% ticks; the value label tracks the slider live.
            InitGainSlider("MicGainSlider", "MicGainValue", _settings.MicGainPercent);
            InitGainSlider("SpeakerGainSlider", "SpeakerGainValue", _settings.SpeakerGainPercent);
        }

        /// <summary>
        /// Gives a saved-but-absent device a row of its own at the end of the list, so the
        /// operator can see what is configured and saving the screen does not quietly reset
        /// it. Without the row the stale index addressed nothing, the combo fell back to
        /// System Default, and the next save made that permanent.
        /// </summary>
        private static void AppendMissingDeviceRow(List<string> items, int savedIndex, int deviceCount)
        {
            if (!AudioDeviceChoice.IsMissing(savedIndex, deviceCount)) return;

            items.Add(string.Format(
                I18nService.Instance.Get("AudioDeviceUnavailable", "Устройство {0} — сейчас недоступно"),
                savedIndex));
        }

        private void InitGainSlider(string sliderName, string valueName, int percent)
        {
            var slider = this.FindControl<Slider>(sliderName);
            var label  = this.FindControl<TextBlock>(valueName);
            if (slider == null) return;
            slider.Value = percent;
            if (label != null)
            {
                label.Text = $"{percent}%";
                slider.PropertyChanged += (_, e) =>
                {
                    if (e.Property == RangeBase.ValueProperty)
                        label.Text = $"{(int)slider.Value}%";
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
                // Held in a field so OnDetachedFromVisualTree can take it off again — a
                // bare lambda cannot be unsubscribed, so every visit to this screen used to
                // leave one more of them attached to App.Updater for good.
                _updateAvailableHandler = () =>
                    Dispatcher.UIThread.InvokeAsync(() => RefreshUpdateBtnText(updateBtn));
                App.Updater.UpdateAvailable += _updateAvailableHandler;

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

            var audioBtn    = this.FindControl<Button>("CheckAudioBtn");
            var audioStatus = this.FindControl<TextBlock>("AudioCheckStatus");
            if (audioBtn != null)
            {
                audioBtn.Click += async (_, __) =>
                {
                    var i18n = Services.I18nService.Instance;
                    audioBtn.IsEnabled = false;
                    if (audioStatus != null)
                    {
                        audioStatus.Foreground = Avalonia.Media.Brush.Parse("#5F7A96");
                        audioStatus.Text = i18n.Get("audio.checking", "Проверка…");
                    }

                    // Probe opens the real devices — run off the UI thread.
                    var problems = await System.Threading.Tasks.Task.Run(
                        () => Services.AudioDeviceCheck.Probe());

                    if (audioStatus != null)
                    {
                        if (problems.Count == 0)
                        {
                            audioStatus.Foreground = Avalonia.Media.Brush.Parse("#17E0A0");
                            audioStatus.Text = i18n.Get("audio.ok", "Микрофон и динамики в порядке.");
                        }
                        else
                        {
                            audioStatus.Foreground = Avalonia.Media.Brush.Parse("#FF6B6B");
                            audioStatus.Text = string.Join(" ", problems);
                        }
                    }
                    audioBtn.IsEnabled = true;
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

            var scaleBox = this.FindControl<ComboBox>("WidgetScaleBox");
            if (scaleBox != null)
                _settings.WidgetScalePercent = WidgetScale.FromListPosition(scaleBox.SelectedIndex);

            // Audio device indices. The "device is absent right now" row resolves back to
            // whatever is already stored, so saving the screen for an unrelated reason
            // cannot drop the operator's headset.
            var speakerBox = this.FindControl<ComboBox>("SpeakerBox");
            if (speakerBox != null)
                _settings.AudioOutDeviceIndex = AudioDeviceChoice.SavedIndex(
                    speakerBox.SelectedIndex, Services.Audio.WaveOutDevices.Count, _settings.AudioOutDeviceIndex);

            var micBox = this.FindControl<ComboBox>("MicBox");
            if (micBox != null)
                _settings.AudioInDeviceIndex = AudioDeviceChoice.SavedIndex(
                    micBox.SelectedIndex, WaveInEvent.DeviceCount, _settings.AudioInDeviceIndex);

            var micGainSlider = this.FindControl<Slider>("MicGainSlider");
            if (micGainSlider != null)
                _settings.MicGainPercent = (int)micGainSlider.Value;

            var speakerGainSlider = this.FindControl<Slider>("SpeakerGainSlider");
            if (speakerGainSlider != null)
                _settings.SpeakerGainPercent = (int)speakerGainSlider.Value;

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

        public event System.EventHandler? OnMinimizeRequested;
        public event System.EventHandler? OnSaveRequested;
        public event System.EventHandler? OnAvatarClicked;
        public event System.EventHandler? OnExitAppRequested;
    }
}
