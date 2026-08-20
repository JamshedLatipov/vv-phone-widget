using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrbitalSIP.Services
{
    public class SipSettings
    {
        public string Server   { get; set; } = "";
        public string Port     { get; set; } = "5060";

        [JsonIgnore]
        public string Username { get; set; } = "";

        [JsonIgnore]
        public string Password { get; set; } = "";
        [JsonIgnore]
        public string AccessToken { get; set; } = "";

        /// <summary>
        /// Spends for a new access token when the current one ages out — see
        /// <see cref="BackendAuth"/>. JsonIgnore for the same reason as the password: a
        /// 30-day credential does not belong in a world-readable file under %APPDATA%,
        /// and a restart takes the operator through the login screen anyway.
        /// </summary>
        [JsonIgnore]
        public string RefreshToken { get; set; } = "";

        [JsonIgnore]
        public JwtPayload? DecodedToken { get; set; }


        public string DisplayName { get; set; } = "";
        public string Language { get; set; } = "ru";
        public string Transport { get; set; } = "UDP";  // UDP | TCP | TLS
        public string BackendUrl { get; set; } = "";

        /// <summary>NAudio WaveOut device index. -1 = system default.</summary>
        public int AudioOutDeviceIndex { get; set; } = -1;
        /// <summary>NAudio WaveIn device index. -1 = system default.</summary>
        public int AudioInDeviceIndex  { get; set; } = -1;
        /// <summary>Outgoing mic gain as a percent. 50..200. 100 = unity.</summary>
        public int MicGainPercent { get; set; } = 100;
        /// <summary>Incoming speaker gain as a percent. 0..200. 100 = unity.</summary>
        public int SpeakerGainPercent { get; set; } = 100;

        /// <summary>
        /// Percentage the widget's layout is scaled by, or <see cref="WidgetScale.Auto"/>
        /// (0) to pick it from the screen. See <see cref="WidgetScale"/> for why the fixed
        /// view sizes need this at all.
        /// </summary>
        public int WidgetScalePercent { get; set; } = WidgetScale.Auto;

        // ── Hotkeys ──────────────────────────────────────────────────
        public string HotkeyMute   { get; set; } = "Alt+M";
        public string HotkeyHold   { get; set; } = "Alt+H";
        public string HotkeyHangup { get; set; } = "Alt+Escape";
        public string HotkeyAnswer { get; set; } = "Alt+Enter";

        /// <summary>
        /// Deliver global hotkeys through Win32 RegisterHotKey instead of a low-level
        /// keyboard hook. RegisterHotKey is the correct primitive — Windows hands over only
        /// the combinations this app asked for, rather than routing every keystroke on the
        /// machine through the process — but it can fail when another application already
        /// owns a combination, and it cannot be exercised without a real desktop session.
        ///
        /// Off by default until it has been verified on a live machine: an operator who
        /// loses the hangup hotkey mid-shift is a worse outcome than an impolite hook.
        /// <see cref="GlobalHotkeyService"/> falls back to the hook by itself if
        /// registration fails, so turning this on cannot leave the hotkeys dead — it can
        /// only be a no-op. Flip the default once a manual pass confirms all four fire
        /// with the window unfocused.
        /// </summary>
        public bool UseHotkeyRegistration { get; set; } = false;

        /// <summary>
        /// Carries the session-scoped credentials across from the live settings.
        ///
        /// These are exactly the <c>[JsonIgnore]</c> properties: they are never written to
        /// disk, so a <see cref="Load"/> cannot restore them and every path that rebuilds
        /// settings from disk has to copy them by hand. That copying used to be an inline
        /// list of four assignments in MainWindow's save handler, and adding
        /// <see cref="RefreshToken"/> made the list wrong without making it fail to
        /// compile: saving settings dropped the refresh token and silently disarmed the
        /// session renewal for the rest of the shift. Keeping the list here, beside the
        /// fields, is what stops the next added field repeating it — and a test asserts
        /// this method covers every JsonIgnore'd property.
        /// </summary>
        public void CopySessionFrom(SipSettings source)
        {
            Username     = source.Username;
            Password     = source.Password;
            AccessToken  = source.AccessToken;
            RefreshToken = source.RefreshToken;
            DecodedToken = source.DecodedToken;
        }

        // ----------------------------------------------------------------
        private static readonly string FilePath = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            "OrbitalSIP", "sip-settings.json");

        public static SipSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    return JsonSerializer.Deserialize<SipSettings>(json) ?? new SipSettings();
                }
            }
            catch { /* return defaults on any error */ }
            return new SipSettings();
        }

        /// <summary>
        /// Writes the settings, replacing the previous file atomically.
        ///
        /// A direct WriteAllText truncates first: a crash, a power cut or a full disk
        /// between the truncate and the write left a zero-length or half-written file, and
        /// <see cref="Load"/> swallows the parse error and hands back defaults — an
        /// operator whose SIP server and audio devices have silently reset to nothing.
        /// Writing beside the target and moving over it means the file is either the old
        /// one or the new one.
        /// </summary>
        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });

            var tempPath = FilePath + ".tmp";

            // Flushed to the device before the rename, not merely handed to the OS cache.
            // The rename is atomic on NTFS, but that is a metadata guarantee: without this
            // a power cut could land the new directory entry while the bytes behind it were
            // still buffered, leaving exactly the truncated file this method exists to
            // prevent — and Load() answers a truncated file with silent defaults, so the
            // operator comes back to a softphone with no SIP server and no audio devices.
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, FilePath, overwrite: true);
        }
    }
}
