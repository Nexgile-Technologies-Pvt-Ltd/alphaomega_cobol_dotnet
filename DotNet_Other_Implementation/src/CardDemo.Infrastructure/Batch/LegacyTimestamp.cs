using System.Globalization;

namespace CardDemo.Infrastructure.Batch;

/// <summary>
/// Formats an instant as the canonical 26-character legacy timestamp text
/// (<c>yyyy-MM-dd HH:mm:ss.ffffff</c>) written to TRAN-PROC-TS / TRAN-ORIG-TS.
/// </summary>
public static class LegacyTimestamp
{
    /// <summary>The 26-character format used across the batch programs.</summary>
    public const string Format = "yyyy-MM-dd HH:mm:ss.ffffff";

    /// <summary>Format the supplied instant (its local-clock value) to 26 characters.</summary>
    public static string Format26(DateTimeOffset instant) =>
        instant.ToString(Format, CultureInfo.InvariantCulture);

    /// <summary>Read the current instant from the provider and format it to 26 characters.</summary>
    public static string Now(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        return Format26(timeProvider.GetLocalNow());
    }
}
