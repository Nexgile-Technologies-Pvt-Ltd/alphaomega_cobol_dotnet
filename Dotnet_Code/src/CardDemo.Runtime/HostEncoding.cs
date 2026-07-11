using System.Text;

namespace CardDemo.Runtime;

/// <summary>
/// Provides the single-byte encodings used to translate the text (PIC X) portions of fixed-width
/// record images.
/// </summary>
/// <remarks>
/// Both encodings are complete single-byte code pages, so the byte -> char -> byte transformation is
/// an identity for all 256 byte values (verified by the runtime test-suite). That property is what
/// makes byte-exact round-tripping of PIC X fields possible: a record decoded from EBCDIC and
/// re-encoded to EBCDIC reproduces the original bytes exactly.
/// </remarks>
public static class HostEncoding
{
    private static readonly object Gate = new();
    private static volatile bool _registered;
    private static Encoding? _ebcdic;
    private static Encoding? _ascii;

    /// <summary>IBM EBCDIC US/Canada, code page 037 — the mainframe record encoding.</summary>
    public static Encoding Ebcdic
    {
        get { EnsureRegistered(); return _ebcdic!; }
    }

    /// <summary>ISO-8859-1 (Latin-1), the complete 8-bit code page used for the ASCII twins.</summary>
    public static Encoding Ascii
    {
        get { EnsureRegistered(); return _ascii!; }
    }

    /// <summary>Returns the <see cref="Encoding"/> for the requested <paramref name="host"/>.</summary>
    public static Encoding For(HostKind host) => host == HostKind.Ebcdic ? Ebcdic : Ascii;

    private static void EnsureRegistered()
    {
        if (_registered) return;
        lock (Gate)
        {
            if (_registered) return;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _ebcdic = Encoding.GetEncoding(37);
            _ascii = Encoding.GetEncoding(28591); // ISO-8859-1 (Latin-1)
            _registered = true;
        }
    }
}
