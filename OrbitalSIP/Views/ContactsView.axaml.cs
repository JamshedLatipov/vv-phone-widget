using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Interactivity;
using System.Collections.ObjectModel;
using OrbitalSIP.ViewModels;

namespace OrbitalSIP.Views
{
    public partial class ContactsView : UserControl
    {
        public ObservableCollection<ContactItemViewModel> Contacts { get; } = new ObservableCollection<ContactItemViewModel>();

        public event EventHandler? OnCloseRequested;
        public event EventHandler? OnSettingsRequested;
        public event EventHandler? OnDialerRequested;
        public event EventHandler? OnRecentsRequested;
        public event EventHandler<string>? OutgoingCallRequested;

        public ContactsView()
        {
            InitializeComponent();
            WireButtons();
            DataContext = this;

            LoadMockContacts();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void WireButtons()
        {
            var topBar = this.FindControl<TopBarControl>("TopBar");
            if (topBar != null) topBar.OnMinimizeRequested += (_, __) => OnCloseRequested?.Invoke(this, EventArgs.Empty);

            var bottomNav = this.FindControl<BottomNavControl>("BottomNav");
            if (bottomNav != null)
            {
                bottomNav.OnSettingsRequested += (_, __) => OnSettingsRequested?.Invoke(this, EventArgs.Empty);
                bottomNav.OnDialerRequested += (_, __) => OnDialerRequested?.Invoke(this, EventArgs.Empty);
                bottomNav.OnRecentsRequested += (_, __) => OnRecentsRequested?.Invoke(this, EventArgs.Empty);
                bottomNav.SetActiveTab("Contacts");
            }
        }

        private void LoadMockContacts()
        {
            Contacts.Clear();
            Contacts.Add(new ContactItemViewModel { Name = "Alice Smith", Number = "1001" });
            Contacts.Add(new ContactItemViewModel { Name = "Bob Johnson", Number = "1002" });
            Contacts.Add(new ContactItemViewModel { Name = "Charlie Davis", Number = "1003" });
            Contacts.Add(new ContactItemViewModel { Name = "Diana Prince", Number = "1004" });
            Contacts.Add(new ContactItemViewModel { Name = "Evan Wright", Number = "1005" });
            Contacts.Add(new ContactItemViewModel { Name = "Tech Support", Number = "1000" });

            var ic = this.FindControl<ItemsControl>("ContactsItemsControl");
            if (ic != null)
            {
                ic.ItemsSource = Contacts;
            }
        }

        private void OnContactCallClicked(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string num)
            {
                OutgoingCallRequested?.Invoke(this, num);
            }
        }
    }
}
