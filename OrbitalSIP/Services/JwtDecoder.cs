using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrbitalSIP.Services
{
    public class JwtPayload
    {
        [JsonPropertyName("sub")]
        public string? Sub { get; set; }

        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("roles")]
        public string[]? Roles { get; set; }

        [JsonPropertyName("fullName")]
        public string? FullName { get; set; }
    }

    public static class JwtDecoder
    {
        public static JwtPayload? Decode(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return null;

            var parts = token.Split('.');
            if (parts.Length != 3) return null;

            var payload = parts[1];
            payload = payload.Replace('-', '+').Replace('_', '/');

            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "="; break;
            }

            try
            {
                var bytes = Convert.FromBase64String(payload);
                var json = Encoding.UTF8.GetString(bytes);
                return JsonSerializer.Deserialize<JwtPayload>(json);
            }
            catch
            {
                return null;
            }
        }
    }
}
