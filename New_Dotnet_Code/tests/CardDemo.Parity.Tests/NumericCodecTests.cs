using CardDemo.Cobol.Runtime;
using Xunit;

namespace CardDemo.Parity.Tests;

/// <summary>
/// Targeted tests for the numeric codecs and COBOL decimal semantics, including the values observed
/// directly in the sample data and synthetic negative-sign cases (the shipped account data is
/// all-positive, so the negative overpunch/zone paths have no natural coverage).
/// </summary>
public class NumericCodecTests
{
    [Fact]
    public void Zoned_decode_matches_observed_account_balance_both_hosts()
    {
        // GnuCOBOL ASCII positive: all plain digits -> +194.00 (S9(10)V99 = 12 digits)
        byte[] ascii = System.Text.Encoding.ASCII.GetBytes("000000019400");
        Assert.Equal(194.00m, ZonedDecimalCodec.Decode(ascii, scale: 2, signed: true, HostKind.Ascii));

        // EBCDIC image: F0*7 F1 F9 F4 F0 C0  -> +194.00
        byte[] ebcdic = { 0xF0, 0xF0, 0xF0, 0xF0, 0xF0, 0xF0, 0xF0, 0xF1, 0xF9, 0xF4, 0xF0, 0xC0 };
        Assert.Equal(194.00m, ZonedDecimalCodec.Decode(ebcdic, scale: 2, signed: true, HostKind.Ebcdic));
    }

    [Theory]
    [InlineData(HostKind.Ebcdic)]
    [InlineData(HostKind.Ascii)]
    public void Zoned_encode_decode_round_trips_signed_values(HostKind host)
    {
        decimal[] values = { 0m, 194.00m, -194.00m, 0.01m, -0.01m, 9999999999.99m, -9999999999.99m };
        foreach (decimal v in values)
        {
            var buf = new byte[12]; // S9(10)V99
            ZonedDecimalCodec.Encode(v, buf, totalDigits: 12, scale: 2, signed: true, host);
            decimal back = ZonedDecimalCodec.Decode(buf, scale: 2, signed: true, host);
            Assert.Equal(v, back);
        }
    }

    [Fact]
    public void Zoned_negative_sets_expected_sign_byte()
    {
        var ebcdic = new byte[3];
        ZonedDecimalCodec.Encode(-5m, ebcdic, totalDigits: 3, scale: 0, signed: true, HostKind.Ebcdic);
        Assert.Equal(0xD0 | 5, ebcdic[2]); // negative zone D on the last byte

        var ascii = new byte[3];
        ZonedDecimalCodec.Encode(-5m, ascii, totalDigits: 3, scale: 0, signed: true, HostKind.Ascii);
        Assert.Equal((byte)'u', ascii[2]); // GnuCOBOL ASCII negative: '0' + 5 + 0x40
    }

    [Fact]
    public void Compute_truncates_toward_zero_no_rounding()
    {
        // Interest-style: (TRAN-CAT-BAL * DIS-INT-RATE) / 1200 stored into S9(9)V99 (scale 2), no ROUNDED.
        decimal balance = 1000.00m;
        decimal rate = 18.50m;
        decimal monthly = balance * rate / 1200m;          // 15.4166666...
        decimal stored = Decimals.Store(monthly, integerDigits: 9, scale: 2, signed: true);
        Assert.Equal(15.41m, stored);                       // truncated, NOT 15.42
    }

    [Fact]
    public void Store_drops_high_order_digits_on_overflow_silently()
    {
        // S9(9)V99 holds 9 integer digits; 12-digit integer overflows and wraps (no ON SIZE ERROR).
        decimal stored = Decimals.Store(123456789012.00m, integerDigits: 9, scale: 2, signed: true);
        Assert.Equal(456789012.00m, stored);
    }

    [Fact]
    public void Unsigned_store_keeps_magnitude()
    {
        Assert.Equal(7.00m, Decimals.Store(-7.00m, integerDigits: 9, scale: 2, signed: false));
    }

    [Fact]
    public void Packed_decimal_round_trips()
    {
        foreach (decimal v in new[] { 0m, 194.00m, -194.00m, 9999999.99m, -9999999.99m })
        {
            int total = 9; // e.g. S9(7)V99
            var buf = new byte[PackedDecimalCodec.ByteLength(total)];
            PackedDecimalCodec.Encode(v, buf, total, scale: 2, signed: true);
            Assert.Equal(v, PackedDecimalCodec.Decode(buf, scale: 2));
        }
    }

    [Fact]
    public void Binary_comp_round_trips_big_endian()
    {
        // EXPORT-SEQUENCE-NUM PIC 9(9) COMP occupies 4 bytes; value 1 -> 00 00 00 01.
        var buf = new byte[4];
        BinaryCodec.Encode(1m, buf, totalDigits: 9, scale: 0, signed: false);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x01 }, buf);
        Assert.Equal(1m, BinaryCodec.Decode(buf, scale: 0, signed: false));
    }
}
