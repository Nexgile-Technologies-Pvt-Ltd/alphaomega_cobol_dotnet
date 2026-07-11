namespace CardDemo.Domain.Services;

/// <summary>
/// Fixed-point helpers that reproduce COBOL <c>COMPUTE</c> semantics.
/// COBOL stores results in packed/display fields with a fixed number of decimal
/// places; a <c>COMPUTE</c> without <c>ROUNDED</c> truncates toward zero to the
/// receiving field's scale. CardDemo money and interest fields are V99 (2 dp).
/// </summary>
public static class MoneyMath
{
    /// <summary>Truncate toward zero to two decimal places (COBOL V99, no ROUNDED).</summary>
    public static decimal Truncate2(decimal value) => Math.Truncate(value * 100m) / 100m;
}
