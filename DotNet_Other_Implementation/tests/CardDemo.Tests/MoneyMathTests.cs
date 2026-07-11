using CardDemo.Domain.Services;
using Xunit;

namespace CardDemo.Tests;

public sealed class MoneyMathTests
{
    [Theory]
    [InlineData("1.239", "1.23")]      // truncates toward zero, no rounding up
    [InlineData("1.231", "1.23")]
    [InlineData("1.999", "1.99")]
    [InlineData("100.00", "100.00")]
    [InlineData("0", "0.00")]
    [InlineData("0.005", "0.00")]      // COBOL COMPUTE without ROUNDED truncates
    public void Truncate2_TruncatesTowardZero(string input, string expected)
    {
        var value = decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture);
        var want = decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(want, MoneyMath.Truncate2(value));
    }

    [Fact]
    public void Truncate2_NegativeTruncatesTowardZero()
    {
        Assert.Equal(-1.23m, MoneyMath.Truncate2(-1.239m));
        Assert.Equal(-0.00m, MoneyMath.Truncate2(-0.009m));
    }
}
