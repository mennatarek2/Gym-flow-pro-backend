namespace GMS.Tests;

using GMS.Application.Utilities;

public class PhoneNormalizerTests
{
    [Theory]
    [InlineData("01001234567", "+201001234567")]
    [InlineData("01101234567", "+201101234567")]
    [InlineData("01201234567", "+201201234567")]
    [InlineData("01501234567", "+201501234567")]
    [InlineData("+201001234567", "+201001234567")]
    [InlineData("0020 100 123 4567", "+201001234567")]
    [InlineData("0020-100-123-4567", "+201001234567")]
    [InlineData("201001234567", "+201001234567")]
    [InlineData("010 0123 4567", "+201001234567")]
    [InlineData("010-0123-4567", "+201001234567")]
    [InlineData("+20 100 123 4567", "+201001234567")]
    [InlineData("00201001234567", "+201001234567")]
    public void Normalize_ValidEgyptianFormats_ReturnsCanonicalPlusTwentyForm(string input, string expected)
    {
        Assert.Equal(expected, PhoneNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("01301234567")]   // 013 is not a valid Egyptian mobile network prefix
    [InlineData("0100123456")]    // too short (10 digits)
    [InlineData("010012345678")]  // too long (12 digits)
    [InlineData("abc")]
    [InlineData("")]
    [InlineData(null)]
    public void Normalize_InvalidFormats_ReturnsNull(string? input)
    {
        Assert.Null(PhoneNormalizer.Normalize(input));
    }
}
