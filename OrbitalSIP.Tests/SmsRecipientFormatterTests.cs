using OrbitalSIP.Models;
using Xunit;

namespace OrbitalSIP.Tests;

public class SmsRecipientFormatterTests
{
    [Fact]
    public void Format_GroupsNineDigitLocalNumber()
    {
        Assert.Equal("021 88 49 49", SmsRecipientFormatter.Format("021884949"));
    }

    [Fact]
    public void Format_GroupsTajikNumberWithCountryCode()
    {
        Assert.Equal("+992 90 123 45 67", SmsRecipientFormatter.Format("+992901234567"));
    }

    [Fact]
    public void Format_AddsPlusToBareCountryCodeNumber()
    {
        Assert.Equal("+992 90 123 45 67", SmsRecipientFormatter.Format("992901234567"));
    }

    [Fact]
    public void Format_LeavesAlreadySpacedValueUntouched()
    {
        Assert.Equal("+992 ** *** 12 34", SmsRecipientFormatter.Format("+992 ** *** 12 34"));
    }

    [Fact]
    public void Format_LeavesUnknownShapeUntouched()
    {
        Assert.Equal("3333", SmsRecipientFormatter.Format("3333"));
    }

    [Fact]
    public void Format_LeavesNonDigitValueUntouched()
    {
        Assert.Equal("anonymous", SmsRecipientFormatter.Format("anonymous"));
    }

    [Fact]
    public void Format_TrimsSurroundingWhitespace()
    {
        Assert.Equal("021 88 49 49", SmsRecipientFormatter.Format("  021884949  "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Format_ReturnsEmptyForBlankInput(string? raw)
    {
        Assert.Equal(string.Empty, SmsRecipientFormatter.Format(raw));
    }
}
