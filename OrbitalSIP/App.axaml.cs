using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.Controls.ApplicationLifetimes;
using OrbitalSIP.Services;

namespace OrbitalSIP
{
    public class App : Application
    {
        /// <summary>Application-wide SIP stack singleton.</summary>
        public static readonly SipService SipService = new SipService();

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow();
                desktop.Exit += (_, __) => SipService.Dispose();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}

