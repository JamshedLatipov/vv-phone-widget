using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

public class SipSettingsSessionTests
{
    /// <summary>
    /// The [JsonIgnore] properties are exactly the ones Save() never writes and Load()
    /// therefore cannot restore, so they are exactly the ones a settings rebuild has to
    /// carry across by hand.
    /// </summary>
    private static PropertyInfo[] SessionScopedProperties() =>
        typeof(SipSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>() != null)
            .ToArray();

    /// <summary>
    /// Guards the failure that actually happened: RefreshToken was added as a
    /// session-scoped field, the save handler's hand-written copy list was not updated,
    /// and saving settings silently dropped the token — disarming session renewal for the
    /// rest of the shift with nothing on screen and nothing in the log.
    ///
    /// Reflection rather than five explicit asserts, so this fails when the NEXT such
    /// field is added and CopySessionFrom is not updated.
    /// </summary>
    [Fact]
    public void CopySessionFromCarriesEverySessionScopedProperty()
    {
        var properties = SessionScopedProperties();
        Assert.NotEmpty(properties);

        var source = new SipSettings
        {
            Username     = "operator-1",
            Password     = "sip-secret",
            AccessToken  = "access-token-value",
            RefreshToken = "refresh-token-value",
            DecodedToken = new JwtPayload { Sub = "42", Username = "operator-1" },
        };

        // Every session-scoped property must have been given a distinct, non-default value
        // above, or this test would pass vacuously for it.
        foreach (var property in properties)
        {
            var value = property.GetValue(source);
            Assert.True(value != null && !Equals(value, ""),
                $"Test setup is missing a value for the session-scoped property '{property.Name}'.");
        }

        var target = new SipSettings();
        target.CopySessionFrom(source);

        foreach (var property in properties)
            Assert.Equal(property.GetValue(source), property.GetValue(target));
    }

    /// <summary>
    /// The persisted settings must survive the copy untouched: the save path loads them
    /// from disk precisely to keep the operator's edits, and then overlays the session.
    /// </summary>
    [Fact]
    public void CopySessionFromLeavesPersistedSettingsAlone()
    {
        var target = new SipSettings
        {
            Server              = "10.0.0.1",
            Port                = "5061",
            Transport           = "TCP",
            BackendUrl          = "https://crm.internal",
            Language            = "uz",
            AudioInDeviceIndex  = 3,
            MicGainPercent      = 150,
            HotkeyHangup        = "Ctrl+F8",
        };

        target.CopySessionFrom(new SipSettings { Username = "operator-1", AccessToken = "t" });

        Assert.Equal("10.0.0.1", target.Server);
        Assert.Equal("5061", target.Port);
        Assert.Equal("TCP", target.Transport);
        Assert.Equal("https://crm.internal", target.BackendUrl);
        Assert.Equal("uz", target.Language);
        Assert.Equal(3, target.AudioInDeviceIndex);
        Assert.Equal(150, target.MicGainPercent);
        Assert.Equal("Ctrl+F8", target.HotkeyHangup);
    }

    /// <summary>
    /// Nothing session-scoped may reach the settings file: it lives in %APPDATA% in the
    /// clear, and that is why Load() cannot restore these in the first place.
    /// </summary>
    [Fact]
    public void SessionScopedPropertiesAreNotSerialised()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new SipSettings
        {
            Username     = "operator-1",
            Password     = "sip-secret",
            AccessToken  = "access-token-value",
            RefreshToken = "refresh-token-value",
        });

        Assert.DoesNotContain("sip-secret", json);
        Assert.DoesNotContain("access-token-value", json);
        Assert.DoesNotContain("refresh-token-value", json);
        Assert.DoesNotContain("operator-1", json);
    }
}
