using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CardDemo.Infrastructure.Persistence.Converters;

/// <summary>
/// Shared EF Core value converters implementing the safe-target persistence rules
/// (09-DotNet-Target-Architecture.md#persistence-design): money persists as signed
/// 64-bit integer minor units (cents) and rates as signed integer hundredths of a
/// percent. All rounding uses <see cref="MidpointRounding.AwayFromZero"/> to match
/// the COBOL half-up convention.
/// </summary>
public static class ValueConverters
{
    /// <summary>
    /// Money <c>decimal</c> (major units, 2 implied decimals) &lt;-&gt; signed 64-bit
    /// integer minor units (cents).
    /// </summary>
    public static readonly ValueConverter<decimal, long> MoneyToCents = new(
        v => (long)Math.Round(v * 100m, MidpointRounding.AwayFromZero),
        v => v / 100m);

    /// <summary>
    /// Rate <c>decimal</c> (percent, 2 implied decimals) &lt;-&gt; signed 32-bit
    /// integer hundredths of a percent.
    /// </summary>
    public static readonly ValueConverter<decimal, int> RateToHundredths = new(
        v => (int)Math.Round(v * 100m, MidpointRounding.AwayFromZero),
        v => v / 100m);
}
