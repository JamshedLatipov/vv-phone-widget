using System.Threading;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace OrbitalSIP
{
    internal static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            using var mutex = new Mutex(true, "OrbitalSIP_SingleInstance", out bool createdNew);
            if (!createdNew)
                return;

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
    }
}
