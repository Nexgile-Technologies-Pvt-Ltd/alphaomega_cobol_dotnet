namespace CardDemo.Runtime;

/// <summary>
/// Identifies the byte representation of a fixed-width record image.
/// </summary>
/// <remarks>
/// <para><see cref="Ebcdic"/> is the IBM CP037 mainframe encoding used by the authoritative
/// <c>app/data/EBCDIC</c> dataset images.</para>
/// <para><see cref="Ascii"/> is the ISO-8859-1/ASCII representation used by the convenience
/// <c>app/data/ASCII</c> twins. The two differ in text bytes <em>and</em> in how the sign of a
/// signed zoned-decimal field is carried (EBCDIC zone nibble vs. ASCII overpunch character).</para>
/// </remarks>
public enum HostKind
{
    /// <summary>IBM EBCDIC, code page 037.</summary>
    Ebcdic,

    /// <summary>ISO-8859-1 (Latin-1), a complete 8-bit superset of US-ASCII.</summary>
    Ascii,
}
