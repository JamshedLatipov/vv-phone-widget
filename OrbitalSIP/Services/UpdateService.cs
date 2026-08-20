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

                var installer = FindInstaller(root);
                if (installer == null)
                {
                    AppLogger.Log("update", "No usable .exe asset found in release.");
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
                await DownloadAndInstallAsync(remoteVer, installer.Value, onStatus, ct);
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

        /// <summary>The release asset this app will download and run.</summary>
        private readonly record struct InstallerAsset(string Url, long Size);

        /// <summary>
        /// Picks the installer out of the release, rejecting any asset whose download URL
        /// does not point at GitHub. The release JSON already arrives over a validated TLS
        /// connection to api.github.com, so this is belt-and-braces — but the one field
        /// here that becomes an executable on the operator's machine is worth not taking
        /// on trust from a parsed document.
        /// </summary>
        private static InstallerAsset? FindInstaller(JsonElement releaseRoot)
        {
            if (!releaseRoot.TryGetProperty("assets", out var assets)) return null;

            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (name == null || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;

                if (!asset.TryGetProperty("browser_download_url", out var urlElement)) continue;
                var url = urlElement.GetString();
                if (!IsGitHubDownload(url))
                {
                    AppLogger.Log("update", $"Ignoring release asset '{name}': download URL is not on GitHub.");
                    continue;
                }

                var size = asset.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number
                    ? s.GetInt64()
                    : 0;

                return new InstallerAsset(url!, size);
            }

            return null;
        }

        private static bool IsGitHubDownload(string? url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
             uri.Host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase) ||
             uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase));

        private static async Task DownloadAndInstallAsync(
            Version remoteVer, InstallerAsset installer, Action<string> onStatus, CancellationToken ct)
        {
            // A fresh directory with an unguessable name. The old path was
            // %TEMP%\OrbitalSIP-Setup-{version}.exe — entirely predictable, so anything
            // running as this user could sit on that name and swap the file between the
            // write and Process.Start, and the app would launch it through an installer
            // manifest that asks for elevation.
            var stagingDir = Path.Combine(Path.GetTempPath(), StagingPrefix + Path.GetRandomFileName());
            var tempPath   = Path.Combine(stagingDir, $"OrbitalSIP-Setup-{remoteVer}.exe");

            // The successful path cannot delete its own staging directory — the installer
            // is running out of it when this process is closed — so previous ones are
            // swept here instead. Without this every completed update left ~90 MB in %TEMP%
            // forever.
            SweepOldStagingDirectories(stagingDir);

            try
            {
                Directory.CreateDirectory(stagingDir);
                AppLogger.Log("update", $"Downloading installer → {tempPath}");

                // Streamed, not GetByteArrayAsync: the installer is around 90 MB and
                // buffering it whole put every byte on the large object heap first.
                using (var response = await _httpDownload
                           .GetAsync(installer.Url, HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    response.EnsureSuccessStatusCode();

                    await using var source = await response.Content.ReadAsStreamAsync(ct);
                    // CreateNew, FileShare.None: fails outright rather than writing into a
                    // file something else got there first, or that anything can open while
                    // it is being written.
                    await using var target = new FileStream(
                        tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);

                    await source.CopyToAsync(target, ct);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("update", $"Download failed: {ex.Message}");
                onStatus(I18nService.Instance.Get("UpdateError"));
                TryCleanup(stagingDir);
                return;
            }

            // The release told us how big the asset is; a file that is not that size is
            // truncated or is not the file GitHub is serving.
            var actualSize = new FileInfo(tempPath).Length;
            if (installer.Size > 0 && actualSize != installer.Size)
            {
                AppLogger.Log("update", $"Refusing to run the installer: expected {installer.Size} bytes, got {actualSize}.");
                onStatus(I18nService.Instance.Get("UpdateError"));
                TryCleanup(stagingDir);
                return;
            }

            LogAuthenticode(tempPath);

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
                TryCleanup(stagingDir);
            }
        }

        /// <summary>
        /// Records who signed the installer, or that nobody did.
        ///
        /// Deliberately does NOT refuse to run an unsigned build: this project does not
        /// code-sign its installer today (there is no signtool step in build.ps1 or the
        /// Inno script), so enforcing a signature would simply break every update. The
        /// real integrity control here is a signing step in the build pipeline; until
        /// that exists, this line is what makes its absence visible in the log rather
        /// than only in a review document.
        /// </summary>
        private static void LogAuthenticode(string path)
        {
            try
            {
                var certificate = System.Security.Cryptography.X509Certificates
                    .X509Certificate.CreateFromSignedFile(path);
                AppLogger.Log("update", $"Installer is Authenticode-signed by: {certificate.Subject}");
            }
            catch (Exception ex)
            {
                AppLogger.Log("update",
                    $"Installer carries NO usable Authenticode signature ({ex.GetType().Name}). " +
                    "Running it anyway — this build is not signed. Add a signing step to the build to fix this properly.");
            }
        }

        private const string StagingPrefix = "OrbitalSIP-update-";

        /// <summary>
        /// Deletes staging directories left by earlier updates. Best effort throughout: one
        /// still holding a running installer simply refuses to go and is tried again next
        /// time.
        /// </summary>
        private static void SweepOldStagingDirectories(string keep)
        {
            try
            {
                foreach (var directory in Directory.EnumerateDirectories(Path.GetTempPath(), StagingPrefix + "*"))
                {
                    if (string.Equals(directory, keep, StringComparison.OrdinalIgnoreCase)) continue;
                    try { Directory.Delete(directory, recursive: true); }
                    catch { /* in use, or not ours to delete */ }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("update", $"Could not sweep old staging directories: {ex.Message}");
            }
        }

        private static void TryCleanup(string directory)
        {
            try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
            catch (Exception ex) { AppLogger.Log("update", $"Could not clean up '{directory}': {ex.Message}"); }
        }
    }
}
