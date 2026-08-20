using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

public class GlobalHotkeyServiceTests
{
    [Theory]
    [InlineData("Ctrl+M")]
    [InlineData("alt+h")]
    [InlineData("Alt+Escape")]
    [InlineData("Alt+Enter")]
    [InlineData("Alt+Space")]
    [InlineData("Escape")]
    [InlineData("Esc")]
    [InlineData("Return")]
    [InlineData("F12")]
    [InlineData("A")]
    [InlineData("  Ctrl+F5  ")]
    public void RecognisedCombinationsParse(string hotkey)
    {
        Assert.True(GlobalHotkeyService.IsValidHotkey(hotkey));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+")]
    [InlineData("Shift+M")]     // Shift is not a modifier this parser handles
    [InlineData("F13")]
    [InlineData("Ctrl+Alt+M")]  // only one modifier prefix is understood
    [InlineData("MM")]
    [InlineData("1")]
    public void UnrecognisedCombinationsDoNotParse(string? hotkey)
    {
        Assert.False(GlobalHotkeyService.IsValidHotkey(hotkey));
    }

    // ── RegisterHotKey eligibility ──────────────────────────────────────────

    /// <summary>
    /// The four defaults and the deployed config all carry Alt, which is what makes the
    /// registration path usable at all for this app.
    /// </summary>
    [Theory]
    [InlineData("Ctrl+M")]
    [InlineData("Alt+H")]
    [InlineData("Alt+Escape")]
    [InlineData("Alt+Enter")]
    [InlineData("Alt+Space")]
    public void CombinationWithAModifierMayBeRegistered(string hotkey)
    {
        Assert.True(GlobalHotkeyService.IsSafeToRegister(hotkey));
    }

    /// <summary>
    /// The case that must never reach RegisterHotKey: it consumes what it claims, so a
    /// bare key would become untypeable in every application on the machine.
    /// </summary>
    [Theory]
    [InlineData("M")]
    [InlineData("Escape")]
    [InlineData("Enter")]
    [InlineData("Space")]
    [InlineData("F5")]
    public void BareKeyMayNotBeRegistered(string hotkey)
    {
        Assert.False(GlobalHotkeyService.IsSafeToRegister(hotkey));
    }

    /// <summary>
    /// An unset or unparseable binding registers nothing, so it cannot disqualify the
    /// registration path — otherwise an operator who cleared one hotkey would silently
    /// drop the other three back onto the hook.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Shift+M")]
    [InlineData("F13")]
    public void UnsetOrUnparseableBindingIsNotADisqualifier(string? hotkey)
    {
        Assert.True(GlobalHotkeyService.IsSafeToRegister(hotkey));
    }
}
