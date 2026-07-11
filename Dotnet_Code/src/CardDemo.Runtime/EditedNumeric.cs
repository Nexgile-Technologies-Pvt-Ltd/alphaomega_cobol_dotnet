using System.Text;

namespace CardDemo.Runtime;

/// <summary>
/// Formats a numeric value into a COBOL numeric-edited PICTURE (the kind used by report and screen
/// fields). Supports:
/// <list type="bullet">
///   <item><b>Fixed leading sign</b> (a single <c>+</c>/<c>-</c>), e.g. <c>-ZZZ,ZZZ,ZZZ.ZZ</c>;</item>
///   <item><b>Floating leading sign</b> (a run of two or more <c>-</c> or <c>+</c>), e.g. <c>----9</c>:
///         the sign floats to immediately left of the first significant digit;</item>
///   <item><b>Trailing sign</b> (<c>-</c>/<c>+</c> after the digits) and <b>CR/DB</b> credit-debit symbols;</item>
///   <item><c>Z</c> zero-suppression, <c>9</c> digit positions, comma insertion, and the decimal point.</item>
/// </list>
/// </summary>
/// <remarks>
/// Behaviour of the fixed-leading-sign / Z / comma family is validated byte-for-byte against GnuCOBOL.
/// Zero-suppression blanks leading zeros and any comma in the suppressed region; a leading/trailing
/// <c>-</c> shows '-' for negative and a space for non-negative; a leading/trailing <c>+</c> shows '+'
/// for non-negative and '-' for negative; <c>CR</c>/<c>DB</c> print only for a negative value (spaces
/// otherwise). A floating sign reserves the leftmost character of its run for the sign itself, so the
/// run provides (length-1) zero-suppressible digit positions.
/// </remarks>
public static class EditedNumeric
{
    /// <summary>Formats <paramref name="value"/> into <paramref name="picture"/>, returning a fixed-width string.</summary>
    public static string Format(decimal value, string picture)
    {
        // COBOL PICTURE symbols are case-insensitive (the compiler folds them), so a lowercase
        // edit picture such as "-zzzzzzz9.99" zero-suppresses exactly like "-ZZZZZZZ9.99".
        // Insertion characters in numeric-edited pictures (+ - . , B 0 / CR DB) are unaffected by casing.
        picture = picture.ToUpperInvariant();
        int fullLen = picture.Length;

        // ---- trailing sign / CR / DB editing (e.g. "ZZ9.99-", "9(5)+", "ZZ9CR", "ZZ9DB") ----------------
        // The sign shows on the RIGHT: negative -> '-' / "CR" / "DB"; non-negative -> spaces (for '-',
        // 'CR', 'DB') or '+' (for a trailing '+'). Only treated as a sign when digit positions precede it.
        string trailing = "";
        if (picture.Length >= 3 && (picture.EndsWith("CR") || picture.EndsWith("DB")) && CountDigits(picture[..^2]) > 0)
        {
            trailing = picture[^2..];
            picture = picture[..^2];
        }
        else if (picture.Length >= 2 && (picture[^1] == '-' || picture[^1] == '+') && CountDigits(picture[..^1]) > 0)
        {
            trailing = picture[^1..];
            picture = picture[..^1];
        }

        // ---- leading sign: fixed (single +/-) or floating (a run of two or more identical +/-) ----------
        char leadSign = '\0';
        bool floating = false;
        int start = 0;
        if (trailing.Length == 0 && picture.Length > 0 && (picture[0] == '+' || picture[0] == '-'))
        {
            leadSign = picture[0];
            int run = 0;
            while (run < picture.Length && picture[run] == leadSign) run++;
            if (run >= 2) { floating = true; start = run; } // floating string of `run` sign chars
            else start = 1;                                  // single fixed leading sign
        }

        int dot = picture.IndexOf('.', start);
        string intPic = dot < 0 ? picture[start..] : picture[start..dot];
        string fracPic = dot < 0 ? "" : picture[(dot + 1)..];

        // A floating run of N sign chars reserves its leftmost position for the sign and provides N-1
        // zero-suppressible digit positions; model those as leading 'Z' positions in the integer picture.
        if (floating) intPic = new string('Z', start - 1) + intPic;

        int intDigits = CountDigits(intPic);
        int fracDigits = CountDigits(fracPic);

        bool negative = value < 0m;
        decimal scaled = decimal.Truncate(Math.Abs(value) * Decimals.Pow10(fracDigits));
        scaled %= Decimals.Pow10(intDigits + fracDigits); // silent overflow, matching fixed field width
        long unscaled = (long)scaled;

        // All-Z zero-suppression: a value that truncates to zero blanks the entire field (sign, decimal
        // point, fractional digits, and any trailing sign/CR/DB included), matching GnuCOBOL.
        if (unscaled == 0 && !picture.Contains('9'))
            return new string(' ', fullLen);

        string allDigits = unscaled.ToString().PadLeft(intDigits + fracDigits, '0');
        string intStr = allDigits[..intDigits];
        string fracStr = allDigits[intDigits..];

        var sb = new StringBuilder(fullLen);

        // ---- integer section ----------------------------------------------------------------------------
        if (floating)
        {
            // Render the Z/9/comma positions with zero-suppression, reserving a leading slot for the
            // floating sign, then place the sign immediately to the left of the first significant char.
            string rendered = RenderInt(intPic, intStr);
            char[] ic = (" " + rendered).ToCharArray(); // index 0 is the reserved sign slot
            if (leadSign == '+' || negative)
            {
                int firstNonSpace = 0;
                while (firstNonSpace < ic.Length && ic[firstNonSpace] == ' ') firstNonSpace++;
                int pos = Math.Max(0, firstNonSpace - 1);
                ic[pos] = negative ? '-' : '+';
            }
            sb.Append(ic);
        }
        else
        {
            if (leadSign == '-') sb.Append(negative ? '-' : ' ');
            else if (leadSign == '+') sb.Append(negative ? '-' : '+');
            sb.Append(RenderInt(intPic, intStr));
        }

        // ---- fraction section ---------------------------------------------------------------------------
        if (dot >= 0)
        {
            sb.Append('.');
            int fi = 0;
            foreach (char p in fracPic)
                sb.Append(p is 'Z' or '9' ? fracStr[fi++] : p);
        }

        // ---- trailing sign / CR / DB --------------------------------------------------------------------
        if (trailing == "-") sb.Append(negative ? '-' : ' ');
        else if (trailing == "+") sb.Append(negative ? '-' : '+');
        else if (trailing == "CR") sb.Append(negative ? "CR" : "  ");
        else if (trailing == "DB") sb.Append(negative ? "DB" : "  ");

        return sb.ToString();
    }

    /// <summary>
    /// Renders the integer picture positions (Z/9/comma/other insertion) with zero-suppression, returning
    /// a string the same length as <paramref name="intPic"/>: a leading <c>Z</c> zero becomes a space, a
    /// comma in the still-suppressed region becomes a space, and a <c>9</c> always shows its digit.
    /// </summary>
    private static string RenderInt(string intPic, string intStr)
    {
        var sb = new StringBuilder(intPic.Length);
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
        return sb.ToString();
    }

    private static int CountDigits(string pic)
    {
        int n = 0;
        foreach (char c in pic) if (c is 'Z' or '9') n++;
        return n;
    }
}
