using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System;
using Avalonia.Media;
using Material.Icons.Avalonia;

namespace OrbitalSIP.Views
{
    public partial class BottomNavControl : UserControl
    {
        public event EventHandler? OnSettingsRequested;
        public event EventHandler? OnDialerRequested;
        public event EventHandler? OnRecentsRequested;
        public event EventHandler? OnContactsRequested;

        public BottomNavControl()
        {
            InitializeComponent();
            WireButtons();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void WireButtons()
        {
            var settingsBtn = this.FindControl<Button>("SettingsBtn");
            if (settingsBtn != null)
            {
                settingsBtn.Click += (_, __) => OnSettingsRequested?.Invoke(this, EventArgs.Empty);
            }

            var dialerBtn = this.FindControl<Button>("DialerBtn");
            if (dialerBtn != null)
            {
                dialerBtn.Click += (_, __) => OnDialerRequested?.Invoke(this, EventArgs.Empty);
            }

            var recentsBtn = this.FindControl<Button>("RecentsBtn");
            if (recentsBtn != null)
            {
                recentsBtn.Click += (_, __) => OnRecentsRequested?.Invoke(this, EventArgs.Empty);
            }

            var contactsBtn = this.FindControl<Button>("ContactsBtn");
            if (contactsBtn != null)
            {
                contactsBtn.Click += (_, __) => OnContactsRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        public void SetActiveTab(string tabName)
        {
            var dialerBtn = this.FindControl<Button>("DialerBtn");
            var recentsBtn = this.FindControl<Button>("RecentsBtn");
            var contactsBtn = this.FindControl<Button>("ContactsBtn");
            var settingsBtn = this.FindControl<Button>("SettingsBtn");

            var dialerIcon = this.FindControl<MaterialIcon>("DialerIcon");
            var recentsIcon = this.FindControl<MaterialIcon>("RecentsIcon");
            var contactsIcon = this.FindControl<MaterialIcon>("ContactsIcon");
            var settingsIcon = this.FindControl<MaterialIcon>("SettingsIcon");

            if (dialerBtn != null) dialerBtn.Opacity = tabName == "Dialer" ? 1.0 : 0.65;
            if (dialerIcon != null) dialerIcon.Foreground = new SolidColorBrush(tabName == "Dialer" ? Color.Parse("#60A5FA") : Color.Parse("#8AA0B8"));

            if (recentsBtn != null) recentsBtn.Opacity = tabName == "Recents" ? 1.0 : 0.65;
            if (recentsIcon != null) recentsIcon.Foreground = new SolidColorBrush(tabName == "Recents" ? Color.Parse("#60A5FA") : Color.Parse("#8AA0B8"));

            if (contactsBtn != null) contactsBtn.Opacity = tabName == "Contacts" ? 1.0 : 0.65;
            if (contactsIcon != null) contactsIcon.Foreground = new SolidColorBrush(tabName == "Contacts" ? Color.Parse("#60A5FA") : Color.Parse("#8AA0B8"));

            if (settingsBtn != null) settingsBtn.Opacity = tabName == "Settings" ? 1.0 : 0.65;
            if (settingsIcon != null) settingsIcon.Foreground = new SolidColorBrush(tabName == "Settings" ? Color.Parse("#60A5FA") : Color.Parse("#8AA0B8"));
        }
    }
}
