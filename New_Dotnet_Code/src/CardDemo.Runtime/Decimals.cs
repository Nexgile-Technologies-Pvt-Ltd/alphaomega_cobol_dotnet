namespace CardDemo.Runtime;

/// <summary>
/// Implements COBOL fixed-point decimal semantics on top of <see cref="decimal"/>.
/// </summary>
/// <remarks>
/// <para>CardDemo uses <c>System.Decimal</c>-compatible base-10 fixed-point arithmetic, but COBOL
/// differs from C# in two ways that this type pins down:</para>
/// <list type="number">
/// <item><description><b>Truncation, not rounding.</b> The entire CardDemo codebase contains
/// <em>zero</em> <c>ROUNDED</c> clauses, so every result is truncated toward zero to the receiving
/// field's scale.</description></item>
/// <item><description><b>Silent overflow.</b> There is <em>zero</em> <c>ON SIZE ERROR</c>, so a
/// value that exceeds the receiving field's integer-digit capacity has its high-order digits
/// dropped silently (modulo 10^digits) rather than throwing.</description></item>
/// </list>
/// </remarks>
public static class Decimals
{
    private static readonly decimal[] Pow = BuildPow();

    private static decimal[] BuildPow()
    {
        var p = new decimal[29];
        p[0] = 1m;
        for (int i = 1; i < p.Length; i++) p[i] = p[i - 1] * 10m;
        return p;
    }

    /// <summary>Returns 10^<paramref name="n"/> as a <see cref="decimal"/> (0 &lt;= n &lt;= 28).</summary>
    public static decimal Pow10(int n)
    {
        if ((uint)n >= (uint)Pow.Length)
            throw new ArgumentOutOfRangeException(nameof(n), n, "Power of ten out of decimal range.");
        return Pow[n];
    }

    /// <summary>
    /// Truncates <paramref name="value"/> toward zero to <paramref name="scale"/> fractional digits,
    /// matching a COBOL move/compute into a field with that scale and no <c>ROUNDED</c>.
    /// </summary>
    public static decimal Truncate(decimal value, int scale)
    {
        decimal p = Pow10(scale);
        return decimal.Truncate(value * p) / p;
    }

    /// <summary>
    /// Stores <paramref name="value"/> into a field described by (<paramref name="integerDigits"/>,
    /// <paramref name="scale"/>, <paramref name="signed"/>), reproducing COBOL's truncate-toward-zero
    /// and silent high-order overflow. An unsigned field stores the absolute value.
    /// </summary>
    public static decimal Store(decimal value, int integerDigits, int scale, bool signed)
    {
        if (!signed) value = Math.Abs(value);
        bool negative = value < 0m;
        decimal p = Pow10(scale);
        // Truncate toward zero to the field scale (work on the magnitude to keep truncation symmetric).
        decimal unscaled = decimal.Truncate(Math.Abs(value) * p);
        // Drop high-order digits beyond the field capacity (silent overflow, no ON SIZE ERROR).
        decimal modulus = Pow10(integerDigits + scale);
        unscaled %= modulus;
        decimal result = unscaled / p;
        return negative ? -result : result;
    }
}
