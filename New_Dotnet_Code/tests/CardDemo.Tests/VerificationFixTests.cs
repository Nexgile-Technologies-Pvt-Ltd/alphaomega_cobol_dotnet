using CardDemo.Runtime;

namespace CardDemo.Tests;

/// <summary>
/// Pinning tests for the runtime-codec fidelity fixes surfaced by the independent 5-workflow audit:
/// <list type="bullet">
///   <item><see cref="EditedNumeric"/> floating-sign (<c>----9</c>), trailing-sign and CR/DB editing
///         (the COTRTLIC SQLCODE field <c>WS-DISP-SQLCODE PIC ----9</c> previously rendered garbage);</item>
///   <item><see cref="BinaryCodec"/> COMP encode now applies the IBM TRUNC(STD) decimal-digit modulo so an
///         over-capacity value drops high-order DIGITS (mod 10^n), matching the zoned/packed codecs.</item>
/// </list>
/// </summary>
public sealed class VerificationFixTests
{
    // ---- EditedNumeric: floating leading sign (PIC ----9), the COTRTLIC WS-DISP-SQLCODE picture ----------
    [Theory]
    [InlineData(-911, " -911")]
    [InlineData(-532, " -532")]
    [InlineData(-1, "   -1")]
    [InlineData(0, "    0")]
    [InlineData(100, "  100")]
    [InlineData(-9111, "-9111")] // sign occupies the reserved leftmost slot when all digit positions fill
    public void EditedNumeric_FloatingMinus_Pic____9_MatchesIbm(int value, string expected)
    {
        Assert.Equal(expected, EditedNumeric.Format(value, "----9"));
    }

    [Theory]
    [InlineData(100, " +100")]  // floating '+' shows '+' for non-negative...
    [InlineData(-100, " -100")] // ...and '-' for negative.
    [InlineData(0, "   +0")]    // zero is non-negative, so the floating '+' still prints (left of the forced 0).
    public void EditedNumeric_FloatingPlus_Pic____9(int value, string expected)
    {
        Assert.Equal(expected, EditedNumeric.Format(value, "++++9"));
    }

    [Fact]
    public void EditedNumeric_TrailingSign_And_CrDb()
    {
        // Trailing '-' : '-' when negative, space when not.
        Assert.Equal(" 12-", EditedNumeric.Format(-12, "ZZ9-"));
        Assert.Equal(" 12 ", EditedNumeric.Format(12, "ZZ9-"));
        // Trailing '+' : '-' when negative, '+' when not.
        Assert.Equal(" 12+", EditedNumeric.Format(12, "ZZ9+"));
        Assert.Equal(" 12-", EditedNumeric.Format(-12, "ZZ9+"));
        // CR/DB : the two letters when negative, two spaces when not.
        Assert.Equal("  5CR", EditedNumeric.Format(-5, "ZZ9CR"));
        Assert.Equal("  5  ", EditedNumeric.Format(5, "ZZ9CR"));
        Assert.Equal("  5DB", EditedNumeric.Format(-5, "ZZ9DB"));
        Assert.Equal("  5  ", EditedNumeric.Format(5, "ZZ9DB"));
    }

    [Fact]
    public void EditedNumeric_ExistingFixedSignFamily_Unchanged()
    {
        // Regression guard: the GnuCOBOL-validated fixed-leading-sign / Z-suppress family is untouched.
        Assert.Equal(new string(' ', 15), EditedNumeric.Format(0m, "-ZZZ,ZZZ,ZZZ.ZZ"));
        Assert.Equal("  5", EditedNumeric.Format(5m, "ZZ9"));
        Assert.Equal("-  5", EditedNumeric.Format(-5m, "-ZZ9")); // single leading '-' is FIXED (does not float)
        // Lowercase picture folds identically (the COPAUS1C fix).
        Assert.Equal(EditedNumeric.Format(1234.5m, "-ZZZZZZZ9.99"), EditedNumeric.Format(1234.5m, "-zzzzzzz9.99"));
    }

    // ---- BinaryCodec: IBM TRUNC(STD) decimal-digit overflow on COMP encode --------------------------------
    [Fact]
    public void BinaryCodec_Encode_AppliesDecimalDigitModulo_TruncStd()
    {
        // 12345 into PIC 9(4) COMP (capacity 9999, 2 bytes): TRUNC(STD) keeps 12345 mod 10^4 = 2345,
        // NOT the byte-width value 12345. (Previously the codec masked only to the halfword.)
        byte[] dest = new byte[2];
        BinaryCodec.Encode(12345m, dest, totalDigits: 4, scale: 0, signed: false);
        Assert.Equal(2345m, BinaryCodec.Decode(dest, 0, false));

        // In-capacity values are unaffected.
        BinaryCodec.Encode(1234m, dest, totalDigits: 4, scale: 0, signed: false);
        Assert.Equal(1234m, BinaryCodec.Decode(dest, 0, false));

        // Signed over-capacity keeps the sign on the digit-truncated magnitude: -12345 -> -2345.
        BinaryCodec.Encode(-12345m, dest, totalDigits: 4, scale: 0, signed: true);
        Assert.Equal(-2345m, BinaryCodec.Decode(dest, 0, true));
    }
}
