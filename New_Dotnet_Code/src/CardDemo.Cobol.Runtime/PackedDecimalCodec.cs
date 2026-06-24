namespace CardDemo.Cobol.Runtime;

/// <summary>
/// Encodes and decodes COBOL packed-decimal (USAGE COMP-3) fields: two digits per byte, with the
/// sign held in the low nibble of the last byte (<c>C</c>/<c>A</c>/<c>E</c>/<c>F</c> = positive,
/// <c>D</c>/<c>B</c> = negative). The byte length is <c>floor(totalDigits / 2) + 1</c>; for an even
/// digit count the high nibble of the first byte is an unused zero pad. Used only by the EXPORT path
/// (<c>CVEXPORT</c>).
/// </summary>
public static class PackedDecimalCodec
{
    /// <summary>Byte length of a COMP-3 field holding <paramref name="totalDigits"/> digits.</summary>
    public static int ByteLength(int totalDigits) => totalDigits / 2 + 1;

    /// <summary>Decodes a packed-decimal field into a <see cref="decimal"/> with the given scale.</summary>
    public static decimal Decode(ReadOnlySpan<byte> bytes, int scale)
    {
        int len = bytes.Length;
        if (len == 0) return 0m;

        long magnitude = 0;
        for (int i = 0; i < len; i++)
        {
            int high = (bytes[i] >> 4) & 0x0F;
            int low = bytes[i] & 0x0F;
            if (i < len - 1)
            {
                magnitude = magnitude * 10 + high;
                magnitude = magnitude * 10 + low;
            }
            else
            {
                magnitude = magnitude * 10 + high; // final digit
            }
        }

        int signNibble = bytes[len - 1] & 0x0F;
        bool negative = signNibble == 0x0D || signNibble == 0x0B;

        decimal value = magnitude / Decimals.Pow10(scale);
        return negative ? -value : value;
    }

    /// <summary>
    /// Encodes <paramref name="value"/> into a packed-decimal field of <paramref name="totalDigits"/>
    /// digits, writing <see cref="ByteLength(int)"/> bytes into <paramref name="dest"/>.
    /// </summary>
    public static void Encode(decimal value, Span<byte> dest, int totalDigits, int scale, bool signed)
    {
        int bytes = ByteLength(totalDigits);
        if (dest.Length != bytes)
            throw new ArgumentException($"Destination length {dest.Length} != COMP-3 length {bytes}.", nameof(dest));

        bool negative = signed && value < 0m;
        decimal scaled = decimal.Truncate(Math.Abs(value) * Decimals.Pow10(scale));
        scaled %= Decimals.Pow10(totalDigits);
        long unscaled = (long)scaled;

        int digitNibbles = bytes * 2 - 1;
        Span<int> dn = stackalloc int[digitNibbles];
        for (int i = digitNibbles - 1; i >= 0; i--)
        {
            dn[i] = (int)(unscaled % 10);
            unscaled /= 10;
        }

        int signNibble = signed ? (negative ? 0x0D : 0x0C) : 0x0F;
        for (int j = 0; j < bytes; j++)
        {
            int high = dn[j * 2];
            int low = j < bytes - 1 ? dn[j * 2 + 1] : signNibble;
            dest[j] = (byte)((high << 4) | low);
        }
    }
}
