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
        public static readonly StatusService StatusService = new StatusService();
        public static readonly ScriptService ScriptService = new ScriptService();
        public static readonly LoggedCallService LoggedCallService = new LoggedCallService();
        public static readonly LeadService LeadService = new LeadService();
        public static readonly CallInfoService CallInfoService = new CallInfoService();

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            var initI18n = Services.I18nService.Instance;
            initI18n.LoadLanguage(SipSettings.Load().Language);

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Forward call-state changes to the sound service.
                SipService.CallStateChanged += SoundService.OnStateChanged;

                desktop.MainWindow = new MainWindow();
                desktop.Exit += (_, __) =>
                {
                    SoundService.Dispose();
                    SipService.Dispose();
                    ScriptService.Dispose();
                    LeadService.Dispose();
                    CallInfoService.Dispose();
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
