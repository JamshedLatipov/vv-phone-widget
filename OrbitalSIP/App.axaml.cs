using System;
using System.Threading.Tasks;
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
        public static readonly TaskService TaskService = new TaskService();
        public static readonly CallInfoService CallInfoService = new CallInfoService();
        public static readonly FlowsService FlowsService = new FlowsService();
        public static readonly SmsService SmsService = new SmsService();
        public static readonly GlobalHotkeyService GlobalHotkeys = new GlobalHotkeyService();
        /// <summary>Keeps the two survey entry points — the active-call button and the
        /// campaign auto-open — from stacking two windows over the same call.</summary>
        public static readonly Models.SingleWindowGuard SurveySessions = new Models.SingleWindowGuard();
        /// <summary>One task window at a time; the button no longer blocks itself by
        /// being modal.</summary>
        public static readonly Models.SingleWindowGuard TaskWindows = new Models.SingleWindowGuard();
        /// <summary>One script list at a time — it opens from the active call and from
        /// the call history, and neither knows about the other.</summary>
        public static readonly Models.SingleWindowGuard ScriptWindows = new Models.SingleWindowGuard();
        public static readonly UpdateService        Updater       = new UpdateService();

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            var startupSettings = SipSettings.Load();
            var initI18n = Services.I18nService.Instance;
            initI18n.LoadLanguage(startupSettings.Language);
            BackendHttp.WarnIfInsecure(startupSettings.BackendUrl);

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Forward call-state changes to the sound service.
                SipService.CallStateChanged += SoundService.OnStateChanged;

                // The lead panel's per-call cache has to survive a collapse/expand but not
                // the call itself — see ActiveCallView.ForgetCachedCall.
                SipService.CallStateChanged += state =>
                {
                    if (state == CallState.Idle) Views.ActiveCallView.ForgetCachedCall();
                };
                BackendAuth.SessionExpired += Views.ActiveCallView.ForgetCachedCall;

                desktop.MainWindow = new MainWindow();
                App.GlobalHotkeys.ApplySettings(startupSettings);
                App.GlobalHotkeys.Start(startupSettings);

                // Silent one-shot update check — shows a dot on the Settings button if
                // a newer release is available. Fire-and-forget; errors are swallowed inside.
                _ = Task.Run(() => App.Updater.SilentCheckAsync());

                // Audio self-test: verify mic + speakers can actually open in the SIP
                // format. Warns via the banner if the microphone/speakers are missing or
                // can't be opened (the waveInOpen failure that caused one-way audio).
                AudioDeviceCheck.RunInBackground();

                desktop.Exit += (_, __) =>
                {
                    App.Updater.Dispose();
                    App.GlobalHotkeys.Stop();
                    SoundService.Dispose();
                    SipService.Dispose();
                    ScriptService.Dispose();
                    LeadService.Dispose();
                    TaskService.Dispose();
                    CallInfoService.Dispose();
                    FlowsService.Dispose();
                    SmsService.Dispose();
                    AppLogger.Shutdown();   // last, so everything above still gets logged
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
