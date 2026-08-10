using System;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using OrbitalSIP.Services;

namespace OrbitalSIP.Views;

/// <summary>
/// Wraps an async click handler so a throw reaches the log instead of the AppDomain.
///
/// <c>button.Click += async (_, _) =&gt; await DoAsync();</c> compiles to an async void:
/// the state machine has no caller to hand the exception back to, so anything the body
/// does not catch itself goes straight to AppDomain.UnhandledException. In this app that
/// means a crash report and — for the handlers that sit on the active-call panel — the
/// possibility of the widget disappearing while the operator is still on a call.
///
/// The services behind these buttons all catch internally today, so this is a guard
/// against the next one that does not, not a fix for a live crash.
/// </summary>
internal static class SafeHandler
{
    public static EventHandler<RoutedEventArgs> Click(string source, Func<Task> action) =>
        async (_, _) =>
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                AppLogger.Log(source, $"Click handler threw: {ex.GetType().Name}: {ex.Message}");
            }
        };
}
