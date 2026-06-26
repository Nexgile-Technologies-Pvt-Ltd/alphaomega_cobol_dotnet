using System.Text;
using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;

namespace CardDemo.Batch;

/// <summary>
/// Faithful relational re-port of the batch/util program <c>CBACT03C</c> ("Read and print account cross
/// reference data file"). It opens the card cross-reference master (the VSAM KSDS <c>XREFFILE</c>, now the
/// relational <b>CARD_XREF</b> table) as a sequential, primary-key-ordered read cursor, <c>DISPLAY</c>s each
/// CARD-XREF-RECORD to SYSOUT, then closes the file. It performs no updates, no computations, no filtering,
/// and writes no derived datasets — a pure read-and-print report.
/// </summary>
/// <remarks>
/// <para>
/// Ported paragraph-by-paragraph from <c>app/cbl/CBACT03C.cbl</c> (method names mirror the COBOL paragraph
/// names; each carries a <c>// source: CBACT03C.cbl:NNN</c> citation, and statement order / PERFORM flow is
/// preserved). The VSAM file is read through <see cref="CardXrefRepository"/> (the relational data layer):
/// <c>OPEN INPUT</c> -&gt; <see cref="CardXrefRepository.StartBrowse"/>, a sequential <c>READ</c> -&gt;
/// <see cref="CardXrefRepository.ReadNext"/> (FileStatus '00'/'10'), <c>CLOSE</c> -&gt;
/// <see cref="CardXrefRepository.EndBrowse"/>. The browse is ordered by the PK <c>xref_card_num</c> ASC
/// (ordinal collation), which reproduces the KSDS sequential (READ NEXT) order.
/// </para>
/// <para>
/// SYSOUT (every COBOL <c>DISPLAY</c>) is collected into <see cref="Sysout"/> in order; when an output path
/// is supplied to <see cref="Run(RelationalDb,string?)"/> the same lines are also written to that flat
/// report file via <see cref="BatchSupport.OpenWriter"/>.
/// </para>
/// <para>FAITHFUL BUGS reproduced (see <c>_design/specs/CBACT03C.md</c> §7 / <c>_design/faithful-bugs.md</c>):
/// <list type="number">
/// <item><b>Each record is DISPLAYed TWICE, back-to-back.</b> <c>1000-XREFFILE-GET-NEXT</c> DISPLAYs
/// CARD-XREF-RECORD on status '00' (line 96), and the mainline loop ALSO DISPLAYs it after the PERFORM
/// (line 78). The sibling CBACT02C displays only once; this duplicate output is genuine and preserved.</item>
/// <item>The redundant inner <c>IF END-OF-FILE = 'N'</c> guards in the mainline loop (lines 75, 77)
/// duplicate the <c>PERFORM UNTIL</c> condition; the control structure is reproduced as-is.</item>
/// <item><c>9910-DISPLAY-IO-STATUS</c> second-byte rendering on the non-numeric / <c>IO-STAT1='9'</c>
/// branch reinterprets the status char as the low byte of a big-endian halfword and prints it as a 0..255
/// number (the char's EBCDIC code point, e.g. '0' -&gt; 240). Reproduced exactly, big-endian.</item>
/// </list></para>
/// </remarks>
public sealed class Cbact03c
{
    // --- WORKING-STORAGE (CBACT03C lines 42-67) --------------------------------------------------

    /// <summary>XREFFILE-STATUS — the file-status of the CARD_XREF cursor (2 chars). // source: CBACT03C.cbl:46-48</summary>
    private string _xreffileStatus = "00";

    /// <summary>IO-STATUS — working copy of the status used by the display routine. // source: CBACT03C.cbl:50-52</summary>
    private string _ioStatus = "00";

    /// <summary>APPL-RESULT PIC S9(9) COMP. 0=ok(AOK), 16=EOF, 8=pre-op, 12=hard error. // source: CBACT03C.cbl:61-63</summary>
    private int _applResult;

    /// <summary>END-OF-FILE PIC X(01) VALUE 'N' — main-loop sentinel. // source: CBACT03C.cbl:65</summary>
    private bool _endOfFile;

    /// <summary>CARD-XREF-RECORD — the record READ ... INTO populates (CVACT03Y, 50 bytes). // source: CBACT03C.cbl:45,93</summary>
    private CardXref? _cardXrefRecord;

    /// <summary>The CARD_XREF master cursor (VSAM KSDS -&gt; relational CARD_XREF table). // source: CBACT03C.cbl:29-33</summary>
    private CardXrefRepository _xreffile = null!;

    /// <summary>Optional flat report writer mirroring SYSOUT. // source: CBACT03C.cbl:71,168,172</summary>
    private FixedFileWriter? _writer;

    private readonly List<string> _sysout = [];

    /// <summary>88 APPL-AOK VALUE 0. // source: CBACT03C.cbl:62</summary>
    private bool ApplAok => _applResult == 0;

    /// <summary>88 APPL-EOF VALUE 16. // source: CBACT03C.cbl:63</summary>
    private bool ApplEof => _applResult == 16;

    /// <summary>The SYSOUT (DISPLAY) lines produced by the run, in order.</summary>
    public IReadOnlyList<string> Sysout => _sysout;

    /// <summary>
    /// Runs CBACT03C over the relational <paramref name="db"/>. When <paramref name="sysoutPath"/> is
    /// given, every DISPLAY line is also appended to that flat report file (ASCII host) in addition to
    /// being collected into <see cref="Sysout"/>.
    /// </summary>
    /// <returns>The process RETURN-CODE (CBACT03C leaves it 0). // source: CBACT03C.cbl:87 (GOBACK)</returns>
    public int Run(RelationalDb db, string? sysoutPath = null)
        => Run(new CardXrefRepository(db), sysoutPath);

    /// <summary>Convenience overload taking a <see cref="BatchSupport"/> (uses its CARD_XREF repository).</summary>
    public int Run(BatchSupport support, string? sysoutPath = null)
        => Run(support.CardXref, sysoutPath);

    /// <summary>
    /// Runs CBACT03C over an already-resolved <see cref="CardXrefRepository"/> (the relational XREFFILE).
    /// </summary>
    public int Run(CardXrefRepository xreffile, string? sysoutPath = null)
    {
        _xreffile = xreffile;
        _writer = sysoutPath is null ? null : BatchSupport.OpenWriter(sysoutPath, HostKind.Ascii);
        try
        {
            // source: CBACT03C.cbl:71
            Display("START OF EXECUTION OF PROGRAM CBACT03C");
            // source: CBACT03C.cbl:72
            XreffileOpen0000();

            // PERFORM UNTIL END-OF-FILE = 'Y'  // source: CBACT03C.cbl:74-81
            while (!_endOfFile)
            {
                if (!_endOfFile) // IF END-OF-FILE = 'N' (redundant guard — bug #2)  // source: CBACT03C.cbl:75
                {
                    XreffileGetNext1000(); // source: CBACT03C.cbl:76
                    if (!_endOfFile)       // IF END-OF-FILE = 'N' (redundant guard — bug #2)  // source: CBACT03C.cbl:77
                        // FAITHFUL BUG #1: this is the SECOND DISPLAY of the record (the first is inside
                        // 1000-XREFFILE-GET-NEXT, line 96), so every xref record prints twice, back-to-back.
                        Display(FormatCardXrefRecord(_cardXrefRecord!)); // DISPLAY CARD-XREF-RECORD  // source: CBACT03C.cbl:78
                }
            }

            // source: CBACT03C.cbl:83
            XreffileClose9000();
            // source: CBACT03C.cbl:85
            Display("END OF EXECUTION OF PROGRAM CBACT03C");

            // source: CBACT03C.cbl:87 (GOBACK)
            return 0;
        }
        finally
        {
            _writer?.Flush();
            _writer?.Dispose();
        }
    }

    // *****************************************************************
    // * I/O ROUTINES TO ACCESS A KSDS, VSAM DATA SET...               *
    // *****************************************************************

    /// <summary>
    /// 1000-XREFFILE-GET-NEXT — sequential READ of the next cross-reference row. // source: CBACT03C.cbl:92-116
    /// </summary>
    private void XreffileGetNext1000()
    {
        // READ XREFFILE-FILE INTO CARD-XREF-RECORD  // source: CBACT03C.cbl:93
        _xreffileStatus = _xreffile.ReadNext(out CardXref? xref);
        if (xref is not null)
            _cardXrefRecord = xref; // READ ... INTO CARD-XREF-RECORD (only populated on a successful read)

        if (_xreffileStatus == FileStatus.Ok)             // XREFFILE-STATUS = '00'  // source: CBACT03C.cbl:94
        {
            _applResult = 0;                              // MOVE 0 TO APPL-RESULT  // source: CBACT03C.cbl:95
            // FAITHFUL BUG #1: the FIRST DISPLAY of the record (the mainline loop, line 78, prints it again).
            Display(FormatCardXrefRecord(_cardXrefRecord!)); // DISPLAY CARD-XREF-RECORD  // source: CBACT03C.cbl:96
        }
        else if (_xreffileStatus == FileStatus.EndOfFile) // '10'  // source: CBACT03C.cbl:98
            _applResult = 16;                             // source: CBACT03C.cbl:99
        else
            _applResult = 12;                             // source: CBACT03C.cbl:101

        if (ApplAok)                                      // source: CBACT03C.cbl:104
        {
            // CONTINUE  // source: CBACT03C.cbl:105
        }
        else if (ApplEof)                                 // source: CBACT03C.cbl:107
        {
            _endOfFile = true;                            // MOVE 'Y' TO END-OF-FILE  // source: CBACT03C.cbl:108
        }
        else
        {
            Display("ERROR READING XREFFILE");            // source: CBACT03C.cbl:110
            _ioStatus = _xreffileStatus;                  // MOVE XREFFILE-STATUS TO IO-STATUS  // source: CBACT03C.cbl:111
            DisplayIoStatus9910();                        // source: CBACT03C.cbl:112
            AbendProgram9999();                           // source: CBACT03C.cbl:113
        }
        // EXIT  // source: CBACT03C.cbl:116
    }

    // *---------------------------------------------------------------*
    /// <summary>
    /// 0000-XREFFILE-OPEN — OPEN INPUT the xref file (positions a forward, PK-ordered browse).
    /// // source: CBACT03C.cbl:118-134
    /// </summary>
    private void XreffileOpen0000()
    {
        _applResult = 8;                                  // MOVE 8 TO APPL-RESULT  // source: CBACT03C.cbl:119

        // OPEN INPUT XREFFILE-FILE — relational: position the read cursor at the first row in PK order.
        // The SQLite-backed table is always openable, so OPEN succeeds with status '00'.
        _xreffile.StartBrowse();                          // source: CBACT03C.cbl:120
        _xreffileStatus = FileStatus.Ok;

        if (_xreffileStatus == FileStatus.Ok)             // XREFFILE-STATUS = '00'  // source: CBACT03C.cbl:121
            _applResult = 0;                              // source: CBACT03C.cbl:122
        else
            _applResult = 12;                             // source: CBACT03C.cbl:124

        if (ApplAok)                                      // source: CBACT03C.cbl:126
        {
            // CONTINUE  // source: CBACT03C.cbl:127
        }
        else
        {
            Display("ERROR OPENING XREFFILE");            // source: CBACT03C.cbl:129
            _ioStatus = _xreffileStatus;                  // source: CBACT03C.cbl:130
            DisplayIoStatus9910();                        // source: CBACT03C.cbl:131
            AbendProgram9999();                           // source: CBACT03C.cbl:132
        }
        // EXIT  // source: CBACT03C.cbl:134
    }

    // *---------------------------------------------------------------*
    /// <summary>
    /// 9000-XREFFILE-CLOSE — CLOSE the xref file (ends the browse). // source: CBACT03C.cbl:136-152
    /// </summary>
    private void XreffileClose9000()
    {
        _applResult = 8;                                  // ADD 8 TO ZERO GIVING APPL-RESULT  // source: CBACT03C.cbl:137

        // CLOSE XREFFILE-FILE — relational: dispose the read cursor; always succeeds with '00'.
        _xreffile.EndBrowse();                            // source: CBACT03C.cbl:138
        _xreffileStatus = FileStatus.Ok;

        if (_xreffileStatus == FileStatus.Ok)             // XREFFILE-STATUS = '00'  // source: CBACT03C.cbl:139
            _applResult = _applResult - _applResult;      // SUBTRACT APPL-RESULT FROM APPL-RESULT (-> 0)  // source: CBACT03C.cbl:140
        else
            _applResult = 12;                             // ADD 12 TO ZERO GIVING APPL-RESULT  // source: CBACT03C.cbl:142

        if (ApplAok)                                      // source: CBACT03C.cbl:144
        {
            // CONTINUE  // source: CBACT03C.cbl:145
        }
        else
        {
            Display("ERROR CLOSING XREFFILE");            // source: CBACT03C.cbl:147
            _ioStatus = _xreffileStatus;                  // source: CBACT03C.cbl:148
            DisplayIoStatus9910();                        // source: CBACT03C.cbl:149
            AbendProgram9999();                           // source: CBACT03C.cbl:150
        }
        // EXIT  // source: CBACT03C.cbl:152
    }

    /// <summary>
    /// 9999-ABEND-PROGRAM — CALL 'CEE3ABD' USING ABCODE(999), TIMING(0). // source: CBACT03C.cbl:154-158
    /// </summary>
    private void AbendProgram9999()
    {
        Display("ABENDING PROGRAM");                      // source: CBACT03C.cbl:155
        // MOVE 0 TO TIMING; MOVE 999 TO ABCODE; CALL 'CEE3ABD' USING ABCODE, TIMING.
        // source: CBACT03C.cbl:156-158
        throw new AbendException("999", $"CBACT03C abend; FILE STATUS '{_xreffileStatus}'.");
    }

    // *****************************************************************
    /// <summary>
    /// 9910-DISPLAY-IO-STATUS — formats the 2-byte file status into a 4-char "NNNN" string and DISPLAYs
    /// <c>'FILE STATUS IS: NNNN' IO-STATUS-04</c>. // source: CBACT03C.cbl:161-174
    /// </summary>
    private void DisplayIoStatus9910()
    {
        // IO-STATUS-04 = IO-STATUS-0401 PIC 9 + IO-STATUS-0403 PIC 999  // source: CBACT03C.cbl:57-59
        string ioStat1 = _ioStatus.Length > 0 ? _ioStatus[..1] : " ";
        string ioStat2 = _ioStatus.Length > 1 ? _ioStatus.Substring(1, 1) : " ";

        string ioStatus04;

        // IF IO-STATUS NOT NUMERIC OR IO-STAT1 = '9'  // source: CBACT03C.cbl:162-163
        if (!IsNumeric(_ioStatus) || ioStat1 == "9")
        {
            // MOVE IO-STAT1 TO IO-STATUS-04(1:1) — first char is the stat1 byte as-is.  // source: CBACT03C.cbl:164
            char pos1 = ioStat1.Length > 0 ? ioStat1[0] : ' ';

            // MOVE 0 TO TWO-BYTES-BINARY; MOVE IO-STAT2 TO TWO-BYTES-RIGHT;
            // MOVE TWO-BYTES-BINARY TO IO-STATUS-0403.  // source: CBACT03C.cbl:165-167
            // FAITHFUL BUG #3: TWO-BYTES-BINARY is PIC 9(4) BINARY (big-endian halfword) redefined as two
            // bytes; only the low (right) byte is set from the status character, so reading the halfword
            // yields that character's EBCDIC code point (e.g. '0' (0xF0) -> 240). Reproduce exactly.
            char stat2Char = ioStat2.Length > 0 ? ioStat2[0] : ' ';
            int twoBytesBinary = EbcdicCodePoint(stat2Char);     // big-endian: value == low byte
            int ioStatus0403 = twoBytesBinary % 1000;            // store into PIC 999 (3 digits)

            ioStatus04 = pos1.ToString() + ioStatus0403.ToString("D3"); // source: CBACT03C.cbl:168
        }
        else
        {
            // MOVE '0000' TO IO-STATUS-04; MOVE IO-STATUS TO IO-STATUS-04(3:2).  // source: CBACT03C.cbl:170-171
            ioStatus04 = "00" + _ioStatus.PadRight(2)[..2];
        }

        // DISPLAY 'FILE STATUS IS: NNNN' IO-STATUS-04  // source: CBACT03C.cbl:168,172
        Display("FILE STATUS IS: NNNN" + ioStatus04);
        // EXIT  // source: CBACT03C.cbl:174
    }

    // --- Helpers ---------------------------------------------------------------------------------

    /// <summary>
    /// Reproduces <c>DISPLAY CARD-XREF-RECORD</c>: the 50-byte CVACT03Y logical image rendered as text —
    /// the three elementary fields in copybook order then a 14-byte FILLER of spaces. Numeric fields are
    /// zoned (USAGE DISPLAY, unsigned) zero-padded; the alphanumeric card number is left-justified,
    /// space-padded.
    /// </summary>
    /// <remarks>
    /// // source: CVACT03Y.cpy:4-8 (XREF-CARD-NUM X16, XREF-CUST-ID 9(09), XREF-ACCT-ID 9(11), FILLER X14).
    /// </remarks>
    private static string FormatCardXrefRecord(CardXref x)
    {
        var sb = new StringBuilder(50);
        sb.Append(Alpha(x.XrefCardNum, 16));   // XREF-CARD-NUM PIC X(16)
        sb.Append(Zoned(x.CustId, 9));         // XREF-CUST-ID  PIC 9(09)
        sb.Append(Zoned(x.AcctId, 11));        // XREF-ACCT-ID  PIC 9(11)
        sb.Append(new string(' ', 14));        // FILLER        PIC X(14)
        return sb.ToString();
    }

    /// <summary>PIC X(width): left-justified, space-padded, right-truncated.</summary>
    private static string Alpha(string text, int width) =>
        (text ?? "").Length >= width ? text![..width] : (text ?? "").PadRight(width, ' ');

    /// <summary>PIC 9(width) USAGE DISPLAY: unsigned, zero-padded decimal digits (low-order on overflow).</summary>
    private static string Zoned(long value, int width)
    {
        string digits = Math.Abs(value).ToString();
        return digits.Length >= width ? digits[^width..] : digits.PadLeft(width, '0');
    }

    /// <summary>COBOL "class NUMERIC" test for a 2-char IO-STATUS (every char is a decimal digit).</summary>
    private static bool IsNumeric(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (char ch in s)
            if (ch < '0' || ch > '9') return false;
        return true;
    }

    /// <summary>
    /// The EBCDIC (CP037) code-point value of a character, which is the integer the big-endian halfword
    /// aliasing in 9910 produces from a single status byte (FAITHFUL BUG #3).
    /// </summary>
    private static int EbcdicCodePoint(char ch)
    {
        byte[] b = HostEncoding.Ebcdic.GetBytes(ch.ToString());
        return b.Length > 0 ? b[0] : 0;
    }

    /// <summary>DISPLAY -&gt; SYSOUT: collect the line and (optionally) append it to the flat report file.</summary>
    private void Display(string line)
    {
        _sysout.Add(line);
        _writer?.WriteLine(line);
    }
}
