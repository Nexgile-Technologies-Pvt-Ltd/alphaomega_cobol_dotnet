using System.Globalization;

namespace CardDemo.Infrastructure.Fixtures;

/// <summary>
/// Decoder for COBOL zoned-decimal (overpunch) signed DISPLAY fields as stored in
/// the ASCII fixtures. Signed numerics (e.g. <c>S9(10)V99</c>) carry the sign on the
/// LAST byte via the standard overpunch alphabet:
/// <c>{</c> = +0, <c>A..I</c> = +1..+9 ; <c>}</c> = -0, <c>J..R</c> = -1..-9.
/// The remaining bytes are plain digits. Unsigned <c>9(n)</c> fields have no overpunch
/// and are handled by the plain-digit path.
/// </summary>
public static class ZonedDecimal
{
    /// <summary>
    /// Decode a raw zoned-decimal string to a <see cref="decimal"/> applying
    /// <paramref name="decimalPlaces"/> implied decimal digits. Handles overpunch on
    /// the final byte; a trailing plain digit (unsigned field) yields a positive value.
    /// Leading/trailing surrounding spaces are ignored.
    /// </summary>
    public static decimal Decode(string raw, int decimalPlaces)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var s = raw.Trim();
        if (s.Length == 0)
            return 0m;

        // A leading explicit sign is tolerated (not used by these fixtures, but safe).
        var negative = false;
        if (s[0] is '+' or '-')
        {
            negative = s[0] == '-';
            s = s[1..];
            if (s.Length == 0)
                return 0m;
        }

        var digits = new char[s.Length];
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c is >= '0' and <= '9')
            {
                digits[i] = c;
                continue;
            }

            // Overpunch only ever appears on the final byte of a signed field.
            if (i == s.Length - 1 && TryDecodeOverpunch(c, out var digit, out var sign))
            {
                digits[i] = digit;
                if (sign < 0)
                    negative = true;
                continue;
            }

            throw new FormatException(
                $"Invalid zoned-decimal character '{c}' (0x{(int)c:X2}) at position {i} in \"{raw}\".");
        }

        var text = new string(digits);
        if (decimalPlaces > 0)
        {
            if (text.Length <= decimalPlaces)
                text = text.PadLeft(decimalPlaces + 1, '0');
            text = text.Insert(text.Length - decimalPlaces, ".");
        }

        var value = decimal.Parse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        return negative ? -value : value;
    }

    /// <summary>
    /// Map an overpunch byte to its underlying digit and sign.
    /// Positive: <c>{</c>=0, <c>A..I</c>=1..9. Negative: <c>}</c>=0, <c>J..R</c>=1..9.
    /// </summary>
    private static bool TryDecodeOverpunch(char c, out char digit, out int sign)
    {
        switch (c)
        {
            case '{':
                digit = '0'; sign = 1; return true;
            case '}':
                digit = '0'; sign = -1; return true;
            case >= 'A' and <= 'I':
                digit = (char)('1' + (c - 'A')); sign = 1; return true;
            case >= 'J' and <= 'R':
                digit = (char)('1' + (c - 'J')); sign = -1; return true;
            default:
                digit = '0'; sign = 0; return false;
        }
    }
}
