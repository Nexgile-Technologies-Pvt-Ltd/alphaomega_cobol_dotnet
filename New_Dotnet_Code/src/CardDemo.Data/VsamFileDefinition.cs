namespace CardDemo.Data;

/// <summary>A key as a contiguous byte range within a record image (VSAM keys are byte ranges).</summary>
public readonly record struct KeyDef(int Offset, int Length)
{
    /// <summary>Extracts the key bytes from a record image.</summary>
    public byte[] Extract(ReadOnlySpan<byte> image) => image.Slice(Offset, Length).ToArray();
}

/// <summary>
/// Describes a VSAM KSDS file backed by a SQLite table: its name, fixed record length, primary key,
/// and optional non-unique alternate index. Keys are raw byte ranges so SQLite's BLOB ordering
/// (memcmp) reproduces VSAM key collation for the digit/uppercase keys CardDemo uses.
/// </summary>
/// <param name="Name">Logical file name (also the SQLite table name).</param>
/// <param name="RecordLength">Fixed record length in bytes.</param>
/// <param name="PrimaryKey">Primary key byte range.</param>
/// <param name="AlternateKey">Optional alternate-index key byte range (non-unique).</param>
public sealed record VsamFileDefinition(
    string Name,
    int RecordLength,
    KeyDef PrimaryKey,
    KeyDef? AlternateKey = null);
