namespace CardDemo.Cobol.Runtime;

/// <summary>
/// Encodes and decodes COBOL zoned-decimal fields (USAGE DISPLAY numeric), where each digit occupies
/// one byte and the sign of a signed (PIC S9) field is carried on the <em>last</em> byte.
/// </summary>
/// <remarks>
/// <para><b>EBCDIC</b> carries the sign in the zone (high) nibble of the last byte: <c>C</c> = positive,
/// <c>D</c> = negative, <c>F</c> = unsigned/positive. Verified against the shipped data: EBCDIC
/// <c>ACCT-CURR-BAL</c> <c>F0..F1 F9 F4 F0 C0</c> decodes to +194.00.</para>
/// <para><b>ASCII</b> uses the convention of the GnuCOBOL reference runtime (the only consumer of the
/// ASCII form): positive values are plain digits <c>0x30..0x39</c>; a negative value's last byte is the
/// digit plus <c>0x40</c> (<c>'p'..'y'</c> for 0..9). Verified: <c>-1.94</c> as S9(3)V99 encodes
/// <c>30 30 31 39 74</c>. (This differs from the IBM overpunch <c>'{'</c>/<c>'}'</c> used in the
/// shipped data files, which are not the source of truth here.)</para>
/// <para>No field in CardDemo uses <c>SIGN IS SEPARATE</c> or <c>SIGN IS LEADING</c>, so this
/// trailing-embedded-sign model is complete for the codebase.</para>
/// </remarks>
public static class ZonedDecimalCodec
{
    private const byte AsciiNegativeBias = 0x40; // negative last byte = '0'+digit+0x40 -> 'p'..'y'

    /// <summary>
    /// Decodes a zoned-decimal field into a <see cref="decimal"/> with the given <paramref name="scale"/>.
    /// <paramref name="signed"/> indicates a PIC S9 field whose last byte carries the sign.
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
                digit = bytes[i] & 0x0F; // digit is the low nibble; sign is the last byte's zone nibble
                if (last && signed && (bytes[i] & 0xF0) == 0xD0) negative = true;
            }
            else // ASCII (GnuCOBOL convention)
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
    /// bytes with the given <paramref name="scale"/>, writing into <paramref name="dest"/>. Reproduces
    /// COBOL truncate-toward-zero and silent high-order overflow.
    /// </summary>
    public static void Encode(decimal value, Span<byte> dest, int totalDigits, int scale, bool signed, HostKind host)
    {
        if (dest.Length != totalDigits)
            throw new ArgumentException($"Destination length {dest.Length} != totalDigits {totalDigits}.", nameof(dest));

        bool negative = signed && value < 0m;
        decimal magnitude = Math.Abs(value);
        decimal scaled = decimal.Truncate(magnitude * Decimals.Pow10(scale));
        scaled %= Decimals.Pow10(totalDigits); // silent overflow
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
            else // ASCII (GnuCOBOL convention)
            {
                dest[i] = (byte)('0' + digit + (last && negative ? AsciiNegativeBias : 0));
            }
        }
    }

    private static int DecodeAsciiSign(byte b, out bool negative)
    {
        negative = false;
        if (b >= (byte)'0' && b <= (byte)'9') return b - (byte)'0';          // positive
        if (b >= 0x70 && b <= 0x79) { negative = true; return b - 0x70; }    // negative 'p'..'y'
        throw new FormatException($"Invalid GnuCOBOL ASCII sign byte 0x{b:X2}.");
    }
}
