using System;
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

        private void TrayIcon_Clicked(object? sender, EventArgs e)
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                if (desktop.MainWindow.IsVisible)
                    desktop.MainWindow.Hide();
                else
                    desktop.MainWindow.Show();
            }
        }

        private void MenuShow_Click(object? sender, EventArgs e)
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                desktop.MainWindow.Show();
            }
        }

        private void MenuHide_Click(object? sender, EventArgs e)
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                desktop.MainWindow.Hide();
            }
        }

        private void MenuExit_Click(object? sender, EventArgs e)
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }
    }
}
