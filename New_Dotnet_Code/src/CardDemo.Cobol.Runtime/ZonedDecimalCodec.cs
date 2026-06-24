namespace CardDemo.Cobol.Runtime;

/// <summary>
/// Encodes and decodes COBOL zoned-decimal fields (USAGE DISPLAY numeric), where each digit occupies
/// one byte and the sign of a signed (PIC S9) field is carried on the <em>last</em> byte.
/// </summary>
/// <remarks>
/// <para>EBCDIC carries the sign in the zone (high) nibble of the last byte: <c>C</c> = positive,
/// <c>D</c> = negative, <c>F</c> = unsigned/positive. ASCII carries it as an overpunch character on
/// the last byte (e.g. <c>'{'</c> = +0 .. <c>'I'</c> = +9, <c>'}'</c> = -0 .. <c>'R'</c> = -9).</para>
/// <para>Verified against the shipped data: EBCDIC <c>ACCT-CURR-BAL</c> <c>F0..F1 F9 F4 F0 C0</c>
/// and its ASCII twin <c>...01940{</c> both decode to +194.00.</para>
/// <para>No field in CardDemo uses <c>SIGN IS SEPARATE</c> or <c>SIGN IS LEADING</c>, so this
/// trailing-embedded-sign model is complete for the codebase.</para>
/// </remarks>
public static class ZonedDecimalCodec
{
    // ASCII overpunch: index = digit (0..9); value = the byte written on the last position.
    // Positive: { A B C D E F G H I    Negative: } J K L M N O P Q R
    private static readonly byte[] AsciiPositiveOverpunch =
        { (byte)'{', (byte)'A', (byte)'B', (byte)'C', (byte)'D', (byte)'E', (byte)'F', (byte)'G', (byte)'H', (byte)'I' };
    private static readonly byte[] AsciiNegativeOverpunch =
        { (byte)'}', (byte)'J', (byte)'K', (byte)'L', (byte)'M', (byte)'N', (byte)'O', (byte)'P', (byte)'Q', (byte)'R' };

    /// <summary>
    /// Decodes a zoned-decimal field into a <see cref="decimal"/> with the given
    /// <paramref name="scale"/>. <paramref name="signed"/> indicates a PIC S9 field whose last byte
    /// carries the sign.
    /// </summary>
    public static decimal Decode(ReadOnlySpan<byte> bytes, int scale, bool signed, HostKind host)
    {
        int len = bytes.Length;
        if (len == 0) return 0m;

        long magnitude = 0;
        bool negative = false;

        for (int i = 0; i < len; i++)
        {
            bool last = i == len - 1;
            int digit;
            if (host == HostKind.Ebcdic)
            {
                // Digit is always the low nibble; sign (if any) is the zone nibble of the last byte.
                digit = bytes[i] & 0x0F;
                if (last && signed && (bytes[i] & 0xF0) == 0xD0) negative = true;
            }
            else // ASCII
            {
                if (last && signed) digit = DecodeAsciiSign(bytes[i], out negative);
                else digit = bytes[i] - (byte)'0';
            }

            if ((uint)digit > 9)
                throw new FormatException($"Invalid zoned-decimal digit 0x{bytes[i]:X2} at position {i}.");

            magnitude = magnitude * 10 + digit;
        }

        decimal value = magnitude / Decimals.Pow10(scale);
        return negative ? -value : value;
    }

    /// <summary>
    /// Encodes <paramref name="value"/> into a zoned-decimal field of <paramref name="totalDigits"/>
    /// bytes with the given <paramref name="scale"/>, writing into <paramref name="dest"/> (whose
    /// length must equal <paramref name="totalDigits"/>). Reproduces COBOL truncate-toward-zero and
    /// silent high-order overflow.
    /// </summary>
    public static void Encode(decimal value, Span<byte> dest, int totalDigits, int scale, bool signed, HostKind host)
    {
        if (dest.Length != totalDigits)
            throw new ArgumentException($"Destination length {dest.Length} != totalDigits {totalDigits}.", nameof(dest));

        bool negative = signed && value < 0m;
        decimal magnitude = Math.Abs(value);
        decimal scaled = decimal.Truncate(magnitude * Decimals.Pow10(scale));
        // Drop digits beyond capacity (silent overflow).
        scaled %= Decimals.Pow10(totalDigits);
        long unscaled = (long)scaled;

        for (int i = totalDigits - 1; i >= 0; i--)
        {
            int digit = (int)(unscaled % 10);
            unscaled /= 10;
            bool last = i == totalDigits - 1;

            if (host == HostKind.Ebcdic)
            {
                int zone = 0xF0;
                if (last && signed) zone = negative ? 0xD0 : 0xC0;
                dest[i] = (byte)(zone | digit);
            }
            else // ASCII
            {
                if (last && signed) dest[i] = negative ? AsciiNegativeOverpunch[digit] : AsciiPositiveOverpunch[digit];
                else dest[i] = (byte)('0' + digit);
            }
        }
    }

    private static int DecodeAsciiSign(byte b, out bool negative)
    {
        negative = false;
        if (b >= (byte)'0' && b <= (byte)'9') return b - (byte)'0';
        for (int d = 0; d < 10; d++)
        {
            if (b == AsciiPositiveOverpunch[d]) { negative = false; return d; }
            if (b == AsciiNegativeOverpunch[d]) { negative = true; return d; }
        }
        throw new FormatException($"Invalid ASCII overpunch byte 0x{b:X2}.");
    }
}
