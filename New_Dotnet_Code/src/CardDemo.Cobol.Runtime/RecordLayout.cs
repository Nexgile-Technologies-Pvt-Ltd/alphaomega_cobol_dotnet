namespace CardDemo.Cobol.Runtime;

/// <summary>COBOL data category of an elementary item.</summary>
public enum CobolCategory
{
    /// <summary>PIC X — alphanumeric text.</summary>
    Alphanumeric,

    /// <summary>PIC 9 / S9 — numeric.</summary>
    Numeric,
}

/// <summary>COBOL <c>USAGE</c> of a numeric item (alphanumeric items are always <see cref="Display"/>).</summary>
public enum CobolUsage
{
    /// <summary>Zoned decimal — one byte per digit.</summary>
    Display,

    /// <summary>COMP-3 — packed decimal, two digits per byte plus a sign nibble.</summary>
    Comp3,

    /// <summary>COMP / COMP-4 / BINARY — big-endian two's-complement integer.</summary>
    Comp,
}

/// <summary>
/// A single elementary field within a fixed-width record, carrying everything needed to decode and
/// re-encode its bytes exactly. Produced by the copybook parser; never hand-transcribed.
/// </summary>
/// <param name="Name">Field name (a COBOL FILLER keeps the synthetic name "FILLER").</param>
/// <param name="Offset">Zero-based byte offset within the record.</param>
/// <param name="Length">Byte length of the field's stored image.</param>
/// <param name="Category">Alphanumeric or numeric.</param>
/// <param name="Usage">Numeric usage (ignored for alphanumeric).</param>
/// <param name="Signed">True for PIC S9 (trailing embedded sign).</param>
/// <param name="IntegerDigits">Count of integer digit positions (numeric only).</param>
/// <param name="Scale">Count of fractional digit positions implied by V (numeric only).</param>
/// <param name="IsFiller">True if this came from a FILLER item.</param>
public sealed record FieldDef(
    string Name,
    int Offset,
    int Length,
    CobolCategory Category,
    CobolUsage Usage,
    bool Signed,
    int IntegerDigits,
    int Scale,
    bool IsFiller)
{
    /// <summary>Total digit positions (integer + fractional) for a numeric field.</summary>
    public int TotalDigits => IntegerDigits + Scale;
}

/// <summary>
/// The flattened elementary-field layout of a single record (the COBOL 01-level), in byte order.
/// Group items contribute no bytes of their own; only elementary leaves appear here.
/// </summary>
/// <param name="Name">The 01-level record name.</param>
/// <param name="Fields">Elementary fields in ascending offset order.</param>
/// <param name="Length">Total record length in bytes.</param>
public sealed record RecordLayout(string Name, IReadOnlyList<FieldDef> Fields, int Length)
{
    /// <summary>Finds an elementary field by name.</summary>
    public FieldDef Field(string name) =>
        Fields.FirstOrDefault(f => f.Name == name)
        ?? throw new KeyNotFoundException($"Field '{name}' not found in record '{Name}'.");

    /// <summary>
    /// Returns the contiguous byte range spanning the named fields (first..last), used to derive a
    /// VSAM key directly from the copybook rather than hand-coding offsets.
    /// </summary>
    public (int Offset, int Length) KeyRange(params string[] fieldNames)
    {
        if (fieldNames.Length == 0) throw new ArgumentException("At least one field name is required.", nameof(fieldNames));
        FieldDef first = Field(fieldNames[0]);
        FieldDef last = Field(fieldNames[^1]);
        return (first.Offset, last.Offset + last.Length - first.Offset);
    }
}
