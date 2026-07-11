using CardDemo.Infrastructure.Fixtures;
using Xunit;

namespace CardDemo.Tests;

public sealed class ZonedDecimalTests
{
    // The three canonical contract examples for S9(n)V99 money fields.
    [Theory]
    [InlineData("00000001940{", 2, 194.00)]   // +194.00
    [InlineData("0000005047G", 2, 504.77)]     // +504.77 (G => digit 7, positive)
    [InlineData("00000000000{", 2, 0.00)]      // +0.00
    public void Decode_ContractExamples(string raw, int places, double expected)
    {
        Assert.Equal((decimal)expected, ZonedDecimal.Decode(raw, places));
    }

    [Fact]
    public void Decode_DisclosureRatePercent()
    {
        // DisclosureGroup DIS-INT-RATE S9(04)V99: 00150{ => 15.00 percent.
        Assert.Equal(15.00m, ZonedDecimal.Decode("00150{", 2));
    }

    [Fact]
    public void Decode_NegativeOverpunch()
    {
        // '}' on the last byte => negative zero digit; leading digits form -...50.
        // "0000000005}" => digits 00000000050 with negative sign => -0.50 at 2 dp.
        Assert.Equal(-0.50m, ZonedDecimal.Decode("0000000005}", 2));

        // 'J' => -1 on the last digit: "0000000019J" => 000000001 9 1 => 191 => -1.91.
        Assert.Equal(-1.91m, ZonedDecimal.Decode("0000000019J", 2));
    }

    [Fact]
    public void Decode_PositiveLetterOverpunch()
    {
        // 'A'..'I' map to +1..+9 on the final digit.
        // "0000000000A" => trailing digit 1 => 0.01 positive.
        Assert.Equal(0.01m, ZonedDecimal.Decode("0000000000A", 2));
        // "0000000000I" => trailing digit 9 => 0.09 positive.
        Assert.Equal(0.09m, ZonedDecimal.Decode("0000000000I", 2));
    }

    [Fact]
    public void Decode_NegativeZeroIsZero()
    {
        // '}' = -0: the value is zero regardless of sign.
        Assert.Equal(0.00m, ZonedDecimal.Decode("00000000000}", 2));
    }

    [Fact]
    public void Decode_EmptyOrBlankIsZero()
    {
        Assert.Equal(0m, ZonedDecimal.Decode("   ", 2));
        Assert.Equal(0m, ZonedDecimal.Decode("", 2));
    }
}
