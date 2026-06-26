using System.Text;

namespace CardDemo.Runtime;

/// <summary>
/// Formats a numeric value into a COBOL numeric-edited PICTURE (the kind used by report and screen
/// fields), supporting a leading fixed sign (<c>+</c>/<c>-</c>), <c>Z</c> zero-suppression, <c>9</c>
/// digit positions, comma insertion, and the decimal point — e.g. <c>-ZZZ,ZZZ,ZZZ.ZZ</c>.
/// </summary>
/// <remarks>
/// Behaviour is validated byte-for-byte against GnuCOBOL (see the parity test-suite). Zero-suppression
/// blanks leading zeros and any comma in the suppressed region; a leading <c>-</c> shows '-' for
/// negative and a space for non-negative; a leading <c>+</c> shows '+' for non-negative and '-' for
/// negative.
/// </remarks>
public static class EditedNumeric
{
    /// <summary>Formats <paramref name="value"/> into <paramref name="picture"/>, returning a fixed-width string.</summary>
    public static string Format(decimal value, string picture)
    {
        // COBOL PICTURE symbols are case-insensitive (the compiler folds them), so a lowercase
        // edit picture such as "-zzzzzzz9.99" zero-suppresses exactly like "-ZZZZZZZ9.99".
        // Normalise to upper case so the 'Z'/'9'/'CR'/'DB' handling below is case-agnostic.
        // Insertion characters in numeric-edited pictures (+ - . , B 0 /) are unaffected by casing.
        picture = picture.ToUpperInvariant();

        char sign = '\0';
        int start = 0;
        if (picture.Length > 0 && (picture[0] == '+' || picture[0] == '-')) { sign = picture[0]; start = 1; }

        int dot = picture.IndexOf('.', start);
        string intPic = dot < 0 ? picture[start..] : picture[start..dot];
        string fracPic = dot < 0 ? "" : picture[(dot + 1)..];

        int intDigits = CountDigits(intPic);
        int fracDigits = CountDigits(fracPic);

        bool negative = value < 0m;
        decimal scaled = decimal.Truncate(Math.Abs(value) * Decimals.Pow10(fracDigits));
        scaled %= Decimals.Pow10(intDigits + fracDigits); // silent overflow, matching fixed field width
        long unscaled = (long)scaled;

        // All-Z zero-suppression: a value that truncates to zero blanks the entire field (the sign,
        // decimal point and fractional digits included), matching GnuCOBOL.
        if (unscaled == 0 && !picture.Contains('9'))
            return new string(' ', picture.Length);

        string allDigits = unscaled.ToString().PadLeft(intDigits + fracDigits, '0');
        string intStr = allDigits[..intDigits];
        string fracStr = allDigits[intDigits..];

        var sb = new StringBuilder(picture.Length);

        if (sign == '-') sb.Append(negative ? '-' : ' ');
        else if (sign == '+') sb.Append(negative ? '-' : '+');

        bool significant = false;
        int di = 0;
        foreach (char p in intPic)
        {
            if (p is 'Z' or '9')
            {
                char d = intStr[di++];
                if (p == 'Z' && !significant && d == '0') sb.Append(' ');
                else { sb.Append(d); if (d != '0') significant = true; }
            }
            else if (p == ',') sb.Append(significant ? ',' : ' ');
            else sb.Append(p);
        }

        if (dot >= 0)
        {
            sb.Append('.');
            int fi = 0;
            foreach (char p in fracPic)
                sb.Append(p is 'Z' or '9' ? fracStr[fi++] : p);
        }

        return sb.ToString();
    }

    private static int CountDigits(string pic)
    {
        int n = 0;
        foreach (char c in pic) if (c is 'Z' or '9') n++;
        return n;
    }
}
