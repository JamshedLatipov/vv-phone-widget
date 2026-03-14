using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System;
using Avalonia.Media;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;

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

            var dialerIcon = this.FindControl<AvaloniaPath>("DialerIcon");
            var recentsIcon = this.FindControl<AvaloniaPath>("RecentsIcon");
            var contactsIcon = this.FindControl<AvaloniaPath>("ContactsIcon");
            var settingsIcon = this.FindControl<AvaloniaPath>("SettingsIcon");

            if (dialerBtn != null) dialerBtn.Opacity = tabName == "Dialer" ? 1.0 : 0.65;
            if (dialerIcon != null) dialerIcon.Fill = new SolidColorBrush(tabName == "Dialer" ? Color.Parse("#60A5FA") : Color.Parse("#8AA0B8"));

            if (recentsBtn != null) recentsBtn.Opacity = tabName == "Recents" ? 1.0 : 0.65;
            if (recentsIcon != null) recentsIcon.Fill = new SolidColorBrush(tabName == "Recents" ? Color.Parse("#60A5FA") : Color.Parse("#8AA0B8"));

            if (contactsBtn != null) contactsBtn.Opacity = tabName == "Contacts" ? 1.0 : 0.65;
            if (contactsIcon != null) contactsIcon.Fill = new SolidColorBrush(tabName == "Contacts" ? Color.Parse("#60A5FA") : Color.Parse("#8AA0B8"));

            if (settingsBtn != null) settingsBtn.Opacity = tabName == "Settings" ? 1.0 : 0.65;
            if (settingsIcon != null) settingsIcon.Fill = new SolidColorBrush(tabName == "Settings" ? Color.Parse("#60A5FA") : Color.Parse("#8AA0B8"));
        }
    }
}
