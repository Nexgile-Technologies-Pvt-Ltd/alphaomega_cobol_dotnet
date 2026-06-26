using CardDemo.Runtime;

namespace CardDemo.Tooling;

/// <summary>
/// The parsed meaning of a COBOL PICTURE string: its category, sign, and digit counts.
/// </summary>
/// <param name="Category">Alphanumeric (any X/A present) or Numeric.</param>
/// <param name="Signed">True if the picture begins with S.</param>
/// <param name="IntegerDigits">9-positions before the implied decimal point V.</param>
/// <param name="Scale">9-positions after V.</param>
/// <param name="ByteCount">Character-position count for alphanumeric pictures.</param>
public sealed record PicInfo(
    PicCategory Category,
    bool Signed,
    int IntegerDigits,
    int Scale,
    int ByteCount)
{
    /// <summary>Total numeric digit positions (integer + fractional).</summary>
    public int TotalDigits => IntegerDigits + Scale;

    /// <summary>
    /// Parses a picture string such as <c>X(178)</c>, <c>9(11)</c>, or <c>S9(10)V99</c>.
    /// </summary>
    public static PicInfo Parse(string picture)
    {
        bool signed = false;
        bool afterV = false;
        int intDigits = 0;
        int scale = 0;
        int positions = 0; // X/A/9 character positions
        bool alpha = false;

        int i = 0;
        while (i < picture.Length)
        {
            char c = char.ToUpperInvariant(picture[i]);
            i++;

            int repeat = 1;
            if (i < picture.Length && picture[i] == '(')
            {
                int close = picture.IndexOf(')', i);
                if (close < 0) throw new FormatException($"Unbalanced '(' in picture '{picture}'.");
                repeat = int.Parse(picture.AsSpan(i + 1, close - i - 1));
                i = close + 1;
            }

            switch (c)
            {
                case 'S':
                    signed = true;
                    break;
                case 'V':
                    afterV = true;
                    break;
                case '9':
                    positions += repeat;
                    if (afterV) scale += repeat; else intDigits += repeat;
                    break;
                case 'X':
                case 'A':
                    alpha = true;
                    positions += repeat;
                    break;
                default:
                    throw new NotSupportedException($"Picture symbol '{c}' in '{picture}' is not supported.");
            }
        }

        return alpha
            ? new PicInfo(PicCategory.Alphanumeric, false, 0, 0, positions)
            : new PicInfo(PicCategory.Numeric, signed, intDigits, scale, positions);
    }
}
