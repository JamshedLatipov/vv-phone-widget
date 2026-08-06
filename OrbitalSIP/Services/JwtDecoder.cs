using System;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrbitalSIP.Services
{
    public class JwtPayload
    {
        /// <summary>
        /// The CRM user id. The local HS256 login signs it as a JSON number
        /// (user.id is a numeric primary key) while the Zitadel ID token carries an
        /// opaque string, hence the converter — see <see cref="NumberOrStringConverter"/>.
        /// </summary>
        [JsonPropertyName("sub")]
        [JsonConverter(typeof(NumberOrStringConverter))]
        public string? Sub { get; set; }

        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("roles")]
        public string[]? Roles { get; set; }

        [JsonPropertyName("fullName")]
        public string? FullName { get; set; }
        [JsonPropertyName("operator")]
        public OperatorTokenInfo? Operator { get; set; }
    }

    public class OperatorTokenInfo
    {
        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("password")]
        public string? Password { get; set; }

    }

    /// <summary>
    /// Reads a claim that may arrive as either a JSON string or a JSON number.
    ///
    /// Without this a numeric claim threw mid-deserialisation and JwtDecoder's
    /// catch-all turned the whole payload into null — one claim of the wrong shape
    /// silently cost us the user id, the roles and the operator credentials.
    /// Ids are read as Int64 so no digits are lost on the long Zitadel-style values.
    /// </summary>
    internal sealed class NumberOrStringConverter : JsonConverter<string?>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Number => reader.TryGetInt64(out var id)
                    ? id.ToString(CultureInfo.InvariantCulture)
                    : reader.GetDecimal().ToString(CultureInfo.InvariantCulture),
                _ => null,
            };

        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        {
            if (value == null) writer.WriteNullValue();
            else writer.WriteStringValue(value);
        }
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
