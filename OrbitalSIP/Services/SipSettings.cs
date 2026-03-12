using System.IO;
using System.Text.Json;

namespace OrbitalSIP.Services
{
    public class SipSettings
    {
        public string Server   { get; set; } = "";
        public string Port     { get; set; } = "5060";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Transport { get; set; } = "UDP";  // UDP | TCP | TLS

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

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
    }
}
