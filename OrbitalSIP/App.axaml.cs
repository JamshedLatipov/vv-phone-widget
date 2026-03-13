using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.Controls.ApplicationLifetimes;
using OrbitalSIP.Services;

namespace OrbitalSIP
{
    public class App : Application
    {
        /// <summary>Application-wide SIP stack singleton.</summary>
        public static readonly SipService   SipService   = new SipService();
        /// <summary>Application-wide sound player singleton.</summary>
        public static readonly SoundService SoundService = new SoundService();

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Forward call-state changes to the sound service.
                // SipService fires this event on SIPSorcery background threads,
                // but MediaPlayer is agile so no dispatcher hop is required.
                SipService.CallStateChanged += SoundService.OnStateChanged;

                desktop.MainWindow = new MainWindow();
                desktop.Exit += (_, __) =>
                {
                    SoundService.Dispose();
                    SipService.Dispose();
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}

