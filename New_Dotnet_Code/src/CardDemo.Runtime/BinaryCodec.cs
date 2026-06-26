namespace CardDemo.Runtime;

/// <summary>
/// Encodes and decodes COBOL binary (USAGE COMP / COMP-4 / BINARY) fields as big-endian two's
/// complement integers. The byte length follows IBM's digit-count rule: 1-4 digits = 2 bytes,
/// 5-9 = 4 bytes, 10-18 = 8 bytes. Used only by the EXPORT path (<c>CVEXPORT</c>).
/// </summary>
public static class BinaryCodec
{
    /// <summary>Byte length of a COMP field holding <paramref name="totalDigits"/> digits.</summary>
    public static int ByteLength(int totalDigits) => totalDigits switch
    {
        <= 4 => 2,
        <= 9 => 4,
        <= 18 => 8,
        _ => throw new ArgumentOutOfRangeException(nameof(totalDigits), totalDigits, "COMP digit count out of range."),
    };

    /// <summary>Decodes a big-endian COMP field into a <see cref="decimal"/> with the given scale.</summary>
    public static decimal Decode(ReadOnlySpan<byte> bytes, int scale, bool signed)
    {
        long raw;
        if (signed)
        {
            raw = (sbyte)bytes[0]; // sign-extend the most significant byte
            for (int i = 1; i < bytes.Length; i++) raw = (raw << 8) | bytes[i];
        }
        else
        {
            raw = 0;
            for (int i = 0; i < bytes.Length; i++) raw = (raw << 8) | bytes[i];
        }

        return raw / Decimals.Pow10(scale);
    }

    /// <summary>
    /// Encodes <paramref name="value"/> into a big-endian COMP field of <paramref name="totalDigits"/>
    /// digits, writing <see cref="ByteLength(int)"/> bytes into <paramref name="dest"/>.
    /// </summary>
    public static void Encode(decimal value, Span<byte> dest, int totalDigits, int scale, bool signed)
    {
        int bytes = ByteLength(totalDigits);
        if (dest.Length != bytes)
            throw new ArgumentException($"Destination length {dest.Length} != COMP length {bytes}.", nameof(dest));

        decimal scaled = decimal.Truncate(value * Decimals.Pow10(scale));
        long u = (long)scaled;
        if (!signed) u = Math.Abs(u);

        for (int i = dest.Length - 1; i >= 0; i--)
        {
            dest[i] = (byte)(u & 0xFF);
            u >>= 8;
        }
    }
}
