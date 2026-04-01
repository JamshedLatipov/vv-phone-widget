using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OrbitalSIP;

namespace OrbitalSIP.Services
{
    /// <summary>
    /// Manual update checker: call <see cref="CheckAndUpdateAsync"/> when the user presses
    /// the "Check for updates" button in Settings.
    ///
    /// Flow:
    ///  1. Fetches GitHub API: GET /repos/{owner}/{repo}/releases/latest
    ///  2. Compares tag_name with the running assembly version.
    ///  3. If newer and no SIP call is active, downloads the .exe asset and launches it
    ///     with /VERYSILENT — Inno Setup closes this process, installs, then restarts.
    /// </summary>
    public sealed class UpdateService : IDisposable
    {
        // ── Configuration ───────────────────────────────────────────────────────────
        private const string GitHubOwner = "JamshedLatipov";
        private const string GitHubRepo  = "vv-phone-widget";
        // ───────────────────────────────────────────────────────────────────────────

        private static readonly string ApiUrl =
            $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";

        // Short timeout for the lightweight JSON API call.
        private static readonly HttpClient _httpApi = new()
        {
            Timeout = TimeSpan.FromSeconds(20),
            DefaultRequestHeaders =
            {
                { "User-Agent", "OrbitalSIP-Updater" },
                { "Accept",     "application/vnd.github+json" }
            }
        };

        // Long timeout for downloading the installer binary.
        private static readonly HttpClient _httpDownload = new()
        {
            Timeout = TimeSpan.FromMinutes(20),
            DefaultRequestHeaders = { { "User-Agent", "OrbitalSIP-Updater" } }
        };

        // Prevents two simultaneous checks if the user clicks the button rapidly.
        private int _running = 0;
        private CancellationTokenSource? _cts;

        /// <summary>Raised (on the thread-pool) when a silent startup check finds a newer version.</summary>
        public event Action? UpdateAvailable;

        /// <summary>True after <see cref="SilentCheckAsync"/> has confirmed a newer release exists.</summary>
        public bool HasUpdate { get; private set; }

        /// <summary>Version currently running, read from the assembly manifest.</summary>
        public static Version CurrentVersion =>
            Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0, 0);

        /// <summary>
        /// Check GitHub for a newer release and install it if available.
        /// <paramref name="onStatus"/> is called with translated status text to display in the UI.
        /// Returns immediately if another check is already in progress.
        /// </summary>
        public async Task CheckAndUpdateAsync(Action<string> onStatus)
        {
            if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
                return;

            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            try
            {
                var i18n = I18nService.Instance;
                onStatus(i18n.Get("UpdateChecking"));
                AppLogger.Log("update", $"Manual update check. Current: {CurrentVersion}");

                string json;
                try
                {
                    json = await _httpApi.GetStringAsync(ApiUrl, ct);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    AppLogger.Log("update", $"Network error: {ex.Message}");
                    onStatus(i18n.Get("UpdateError"));
                    return;
                }

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var tagName    = root.GetProperty("tag_name").GetString() ?? "";
                var versionStr = tagName.TrimStart('v');

                if (!Version.TryParse(versionStr, out var remoteVer))
                {
                    AppLogger.Log("update", $"Could not parse remote version '{tagName}'.");
                    onStatus(i18n.Get("UpdateError"));
                    return;
                }

                if (remoteVer <= CurrentVersion)
                {
                    AppLogger.Log("update", $"Up to date ({CurrentVersion}).");
                    onStatus($"{i18n.Get("UpdateUpToDate")} ({CurrentVersion})");
                    return;
                }

                AppLogger.Log("update", $"Update available: {CurrentVersion} → {remoteVer}.");

                var installerUrl = FindInstallerUrl(root);
                if (string.IsNullOrEmpty(installerUrl))
                {
                    AppLogger.Log("update", "No .exe asset found in release.");
                    onStatus(i18n.Get("UpdateError"));
                    return;
                }

                if (App.SipService.State != CallState.Idle)
                {
                    AppLogger.Log("update", "Update postponed: call in progress.");
                    onStatus(i18n.Get("UpdatePostponed"));
                    return;
                }

                onStatus($"{i18n.Get("UpdateDownloading")} {remoteVer}...");
                await DownloadAndInstallAsync(remoteVer, installerUrl, onStatus, ct);
            }
            catch (OperationCanceledException)
            {
                AppLogger.Log("update", "Check cancelled by user.");
            }
            catch (Exception ex)
            {
                AppLogger.Log("update", $"Update check failed: {ex}");
                onStatus(I18nService.Instance.Get("UpdateError"));
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
            }
        }

        /// <summary>
        /// Silent one-shot check at startup: does NOT download anything, does NOT show UI.
        /// If a newer release exists, raises <see cref="UpdateAvailable"/>.
        /// Errors are swallowed — this is best-effort.
        /// </summary>
        public async Task SilentCheckAsync()
        {
            try
            {
                var json = await _httpApi.GetStringAsync(ApiUrl);
                using var doc = JsonDocument.Parse(json);
                var tagName    = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
                var versionStr = tagName.TrimStart('v');
                if (Version.TryParse(versionStr, out var remoteVer) && remoteVer > CurrentVersion)
                {
                    AppLogger.Log("update", $"Silent check: update available {CurrentVersion} → {remoteVer}.");
                    HasUpdate = true;
                    UpdateAvailable?.Invoke();
                }
                else
                {
                    AppLogger.Log("update", $"Silent check: up to date ({CurrentVersion}).");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("update", $"Silent check failed (ignored): {ex.Message}");
            }
        }

        /// <summary>Cancel a running check or download.</summary>
        public void Cancel() => _cts?.Cancel();

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }

        // ── Internals ─────────────────────────────────────────────────────────────

        private static string? FindInstallerUrl(JsonElement releaseRoot)
        {
            if (!releaseRoot.TryGetProperty("assets", out var assets)) return null;
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (name != null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    if (asset.TryGetProperty("browser_download_url", out var url))
                        return url.GetString();
            }
            return null;
        }

        private static async Task DownloadAndInstallAsync(
            Version remoteVer, string url, Action<string> onStatus, CancellationToken ct)
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"OrbitalSIP-Setup-{remoteVer}.exe");
            AppLogger.Log("update", $"Downloading installer → {tempPath}");

            try
            {
                var bytes = await _httpDownload.GetByteArrayAsync(url, ct);
                await File.WriteAllBytesAsync(tempPath, bytes, ct);
            }
            catch (Exception ex)
            {
                AppLogger.Log("update", $"Download failed: {ex.Message}");
                onStatus(I18nService.Instance.Get("UpdateError"));
                return;
            }

            AppLogger.Log("update", "Download complete. Launching installer.");
            onStatus(I18nService.Instance.Get("UpdateInstalling"));

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName        = tempPath,
                    Arguments       = "/VERYSILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AppLogger.Log("update", $"Failed to launch installer: {ex.Message}");
                onStatus(I18nService.Instance.Get("UpdateError"));
            }
        }
    }
}
