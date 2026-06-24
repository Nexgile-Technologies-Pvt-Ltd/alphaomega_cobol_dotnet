using CardDemo.Cobol.Runtime;
using CardDemo.Data;

namespace CardDemo.Batch;

/// <summary>
/// Faithful port of <c>CBACT02C</c> — reads the card file (KSDS) in key order and DISPLAYs each
/// CARD-RECORD once. Output is the SYSOUT (DISPLAY) stream.
/// </summary>
/// <remarks>Ported from <c>app/cbl/CBACT02C.cbl</c>. The get-next paragraph's DISPLAY is commented out,
/// so each record is displayed exactly once (in the main loop, line 78).</remarks>
public sealed class Cbact02cCardPrinter(VsamFile cardFile, RecordLayout cardLayout, HostKind host)
{
    public IReadOnlyList<string> Run()
    {
        _ = cardLayout; // record is displayed as raw bytes; layout retained for symmetry/future use
        var sysout = new List<string> { "START OF EXECUTION OF PROGRAM CBACT02C" };
        cardFile.StartBrowse();
        while (cardFile.ReadNext(out byte[]? image) == FileStatus.Ok)
            sysout.Add(HostEncoding.For(host).GetString(image!)); // DISPLAY CARD-RECORD
        cardFile.EndBrowse();
        sysout.Add("END OF EXECUTION OF PROGRAM CBACT02C");
        return sysout;
    }
}

/// <summary>
/// Faithful port of <c>CBACT03C</c> — reads the card cross-reference file (KSDS) in key order and
/// DISPLAYs each CARD-XREF-RECORD. Output is the SYSOUT (DISPLAY) stream.
/// </summary>
/// <remarks>Ported from <c>app/cbl/CBACT03C.cbl</c>. Faithful quirk: each record is displayed TWICE —
/// once in 1000-XREFFILE-GET-NEXT (line 96) and again in the main loop (line 78). Reproduced, not fixed.</remarks>
public sealed class Cbact03cXrefPrinter(VsamFile xrefFile, RecordLayout xrefLayout, HostKind host)
{
    public IReadOnlyList<string> Run()
    {
        _ = xrefLayout;
        var sysout = new List<string> { "START OF EXECUTION OF PROGRAM CBACT03C" };
        xrefFile.StartBrowse();
        while (xrefFile.ReadNext(out byte[]? image) == FileStatus.Ok)
        {
            string line = HostEncoding.For(host).GetString(image!);
            sysout.Add(line); // DISPLAY in 1000-XREFFILE-GET-NEXT (line 96)
            sysout.Add(line); // DISPLAY in the main loop (line 78)
        }
        xrefFile.EndBrowse();
        sysout.Add("END OF EXECUTION OF PROGRAM CBACT03C");
        return sysout;
    }
}
