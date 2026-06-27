using CardDemo.Runtime;

namespace CardDemo.Utilities;

/// <summary>
/// Re-implementation of the assembler routine <c>COBDATFT</c> (date format conversion). It operates on
/// the 80-byte <c>CODATECN-REC</c> in place: type '1' converts <c>YYYYMMDD</c> -&gt; <c>YYYY-MM-DD</c>,
/// type '2' converts <c>YYYY-MM-DD</c> -&gt; <c>YYYYMMDD</c>; mismatched output type, a separator in a
/// type-1 input, or an unknown type set the error message. It never fails (the original always returns
/// RC=0) and only the specific output bytes are written, leaving the rest of the field unchanged.
/// </summary>
/// <remarks>
/// Re-implemented from <c>app/asm/COBDATFT.asm</c> + <c>app/cpy/CODATECN.cpy</c>. No HLASM toolchain is
/// available, so this is verified against a COBOL transcription of the same routine under GnuCOBOL and
/// documented as a specified emulation boundary.
/// CODATECN-REC layout: TYPE(0,1) INP-DATE(1,20) OUTTYPE(21,1) OUT-DATE(22,20) ERROR-MSG(42,38).
/// </remarks>
public static class DateFormatConverter
{
    private const int Inp = 1;
    private const int OutType = 21;
    private const int Outp = 22;
    private const int ErrMsg = 42;

    /// <summary>Applies the COBDATFT conversion in place to an 80-byte CODATECN-REC in the given host encoding.</summary>
    public static void Convert(byte[] rec, HostKind host)
    {
        if (rec.Length != 80)
            throw new ArgumentException($"CODATECN-REC must be 80 bytes, was {rec.Length}.", nameof(rec));

        var enc = HostEncoding.For(host);
        byte one = enc.GetBytes("1")[0], two = enc.GetBytes("2")[0], dash = enc.GetBytes("-")[0];
        byte type = rec[0], outType = rec[OutType];

        if (type == one)
        {
            if (rec[Inp + 4] == dash || outType == two) { SetError(rec, host); return; }
            Array.Copy(rec, Inp, rec, Outp, 4);          // YYYY
            rec[Outp + 4] = dash;
            Array.Copy(rec, Inp + 4, rec, Outp + 5, 2);  // MM
            rec[Outp + 7] = dash;
            Array.Copy(rec, Inp + 6, rec, Outp + 8, 2);  // DD
        }
        else if (type == two)
        {
            if (outType == one) { SetError(rec, host); return; }
            Array.Copy(rec, Inp, rec, Outp, 4);          // YYYY
            Array.Copy(rec, Inp + 5, rec, Outp + 4, 2);  // MM (skip the '-')
            Array.Copy(rec, Inp + 8, rec, Outp + 6, 2);  // DD (skip the '-')
        }
        else
        {
            SetError(rec, host);
        }
    }

    private static void SetError(byte[] rec, HostKind host) =>
        HostEncoding.For(host).GetBytes("INVALID INPUT".PadRight(38)).CopyTo(rec, ErrMsg);
}
