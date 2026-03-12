using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OrbitalSIP.ViewModels
{
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        private string _status = "Offline";

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
