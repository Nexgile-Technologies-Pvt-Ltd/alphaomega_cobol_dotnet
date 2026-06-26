using System.Text;
using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;

namespace CardDemo.Batch;

/// <summary>
/// Faithful relational port of the batch program <c>CBACT02C</c> ("Read and print card data file").
/// It opens the card master (the VSAM KSDS <c>CARDFILE</c>, now the relational <b>CARD</b> table) as a
/// sequential, primary-key-ordered read cursor, <c>DISPLAY</c>s each CARD-RECORD to SYSOUT once, then
/// closes the file. It performs no updates, no computations, and no filtering.
/// </summary>
/// <remarks>
/// <para>
/// Ported paragraph-by-paragraph from <c>app/cbl/CBACT02C.cbl</c> (method names mirror the COBOL
/// paragraph names; each carries a <c>// source: CBACT02C.cbl:NNN</c> citation). The VSAM file is read
/// through <see cref="CardRepository"/> (relational data layer) — <c>OPEN INPUT</c> -&gt;
/// <see cref="CardRepository.StartBrowse"/>, a sequential <c>READ</c> -&gt;
/// <see cref="CardRepository.ReadNext"/> (FileStatus '00'/'10'), <c>CLOSE</c> -&gt;
/// <see cref="CardRepository.EndBrowse"/>. The browse is ordered by the PK <c>card_num</c> ASC, which
/// reproduces the KSDS sequential (READ NEXT) order.
/// </para>
/// <para>
/// SYSOUT (every COBOL <c>DISPLAY</c>) is collected into <see cref="Sysout"/> in order; when an output
/// path is supplied to <see cref="Run(RelationalDb,string?)"/> the same lines are also written to that
/// flat report file via <see cref="BatchSupport.OpenWriter"/>.
/// </para>
/// <para>FAITHFUL BUGS reproduced (see <c>_design/specs/CBACT02C.md</c> §6): the literal placeholder
/// text <c>NNNN</c> is printed in front of the numeric file-status value in 9910; the
/// status-byte-to-number conversion in 9910 reproduces the big-endian EBCDIC halfword aliasing (a status
/// character yields its EBCDIC code point, e.g. '0' -&gt; 240); the obfuscated constant assignments in
/// 9000-CARDFILE-CLOSE are kept; OPEN does not special-case status '10'; the misspelled copybook field
/// <c>CARD-EXPIRAION-DATE</c> is noted in the serializer.</para>
/// </remarks>
public sealed class Cbact02c
{
    // --- WORKING-STORAGE (CBACT02C lines 42-67) --------------------------------------------------

    /// <summary>CARDFILE-STATUS — the file-status of the CARD cursor (2 chars). // source: CBACT02C.cbl:46-48</summary>
    private string _cardfileStatus = "00";

    /// <summary>IO-STATUS — working copy of the status used by the display routine. // source: CBACT02C.cbl:50-52</summary>
    private string _ioStatus = "00";

    /// <summary>APPL-RESULT PIC S9(9) COMP. 0=ok(AOK), 16=EOF, 8=pre-op, 12=hard error. // source: CBACT02C.cbl:61-63</summary>
    private int _applResult;

    /// <summary>END-OF-FILE PIC X(01) VALUE 'N' — main-loop sentinel. // source: CBACT02C.cbl:65</summary>
    private bool _endOfFile;

    /// <summary>CARD-RECORD — the record READ ... INTO populates (CVACT02Y, 150 bytes). // source: CBACT02C.cbl:45,93</summary>
    private Card? _cardRecord;

    /// <summary>The CARD master cursor (VSAM KSDS -&gt; relational CARD table). // source: CBACT02C.cbl:29-33</summary>
    private CardRepository _cardFile = null!;

    /// <summary>Optional flat report writer mirroring SYSOUT. // source: CBACT02C.cbl:78,168,172</summary>
    private FixedFileWriter? _writer;

    private readonly List<string> _sysout = [];

    /// <summary>88 APPL-AOK VALUE 0. // source: CBACT02C.cbl:62</summary>
    private bool ApplAok => _applResult == 0;

    /// <summary>88 APPL-EOF VALUE 16. // source: CBACT02C.cbl:63</summary>
    private bool ApplEof => _applResult == 16;

    /// <summary>The SYSOUT (DISPLAY) lines produced by the run, in order.</summary>
    public IReadOnlyList<string> Sysout => _sysout;

    /// <summary>
    /// Runs CBACT02C over the relational <paramref name="db"/>. When <paramref name="sysoutPath"/> is
    /// given, every DISPLAY line is also appended to that flat report file (ASCII host) in addition to
    /// being collected into <see cref="Sysout"/>.
    /// </summary>
    /// <returns>The process RETURN-CODE (CBACT02C leaves it 0). // source: CBACT02C.cbl:87 (GOBACK)</returns>
    public int Run(RelationalDb db, string? sysoutPath = null)
        => Run(new CardRepository(db), sysoutPath);

    /// <summary>
    /// Runs CBACT02C over an already-resolved <see cref="CardRepository"/> (the relational CARDFILE).
    /// </summary>
    public int Run(CardRepository cardFile, string? sysoutPath = null)
    {
        _cardFile = cardFile;
        _writer = sysoutPath is null ? null : BatchSupport.OpenWriter(sysoutPath, HostKind.Ascii);
        try
        {
            // source: CBACT02C.cbl:71
            Display("START OF EXECUTION OF PROGRAM CBACT02C");
            // source: CBACT02C.cbl:72
            CardfileOpen0000();

            // PERFORM UNTIL END-OF-FILE = 'Y'  // source: CBACT02C.cbl:74-81
            while (!_endOfFile)
            {
                if (!_endOfFile) // IF END-OF-FILE = 'N'  // source: CBACT02C.cbl:75
                {
                    CardfileGetNext1000(); // source: CBACT02C.cbl:76
                    if (!_endOfFile)       // IF END-OF-FILE = 'N'  // source: CBACT02C.cbl:77
                        Display(FormatCardRecord(_cardRecord!)); // DISPLAY CARD-RECORD  // source: CBACT02C.cbl:78
                }
            }

            // source: CBACT02C.cbl:83
            CardfileClose9000();
            // source: CBACT02C.cbl:85
            Display("END OF EXECUTION OF PROGRAM CBACT02C");

            // source: CBACT02C.cbl:87 (GOBACK)
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
    /// 1000-CARDFILE-GET-NEXT — sequential READ of the next card row. // source: CBACT02C.cbl:92-116
    /// </summary>
    private void CardfileGetNext1000()
    {
        // READ CARDFILE-FILE INTO CARD-RECORD  // source: CBACT02C.cbl:93
        _cardfileStatus = _cardFile.ReadNext(out Card? card);
        if (card is not null)
            _cardRecord = card; // READ ... INTO CARD-RECORD (only populated on a successful read)

        if (_cardfileStatus == FileStatus.Ok)        // CARDFILE-STATUS = '00'  // source: CBACT02C.cbl:94
            _applResult = 0;                          // source: CBACT02C.cbl:95
        else if (_cardfileStatus == FileStatus.EndOfFile) // '10'  // source: CBACT02C.cbl:98
            _applResult = 16;                         // source: CBACT02C.cbl:99
        else
            _applResult = 12;                         // source: CBACT02C.cbl:101

        if (ApplAok)                                  // source: CBACT02C.cbl:104
        {
            // CONTINUE  // source: CBACT02C.cbl:105
        }
        else if (ApplEof)                             // source: CBACT02C.cbl:107
        {
            _endOfFile = true;                        // MOVE 'Y' TO END-OF-FILE  // source: CBACT02C.cbl:108
        }
        else
        {
            Display("ERROR READING CARDFILE");        // source: CBACT02C.cbl:110
            _ioStatus = _cardfileStatus;              // MOVE CARDFILE-STATUS TO IO-STATUS  // source: CBACT02C.cbl:111
            DisplayIoStatus9910();                    // source: CBACT02C.cbl:112
            AbendProgram9999();                       // source: CBACT02C.cbl:113
        }
        // EXIT  // source: CBACT02C.cbl:116
    }

    // *---------------------------------------------------------------*
    /// <summary>
    /// 0000-CARDFILE-OPEN — OPEN INPUT the card file (positions a forward, PK-ordered browse).
    /// // source: CBACT02C.cbl:118-134
    /// </summary>
    private void CardfileOpen0000()
    {
        _applResult = 8;                              // MOVE 8 TO APPL-RESULT  // source: CBACT02C.cbl:119

        // OPEN INPUT CARDFILE-FILE — relational: position the read cursor at the first row in PK order.
        // The SQLite-backed table is always openable, so OPEN succeeds with status '00'.
        _cardFile.StartBrowse();                      // source: CBACT02C.cbl:120
        _cardfileStatus = FileStatus.Ok;

        if (_cardfileStatus == FileStatus.Ok)         // CARDFILE-STATUS = '00'  // source: CBACT02C.cbl:121
            _applResult = 0;                          // source: CBACT02C.cbl:122
        else
            _applResult = 12;                         // source: CBACT02C.cbl:124 (faithful: '10' not handled)

        if (ApplAok)                                  // source: CBACT02C.cbl:126
        {
            // CONTINUE  // source: CBACT02C.cbl:127
        }
        else
        {
            Display("ERROR OPENING CARDFILE");        // source: CBACT02C.cbl:129
            _ioStatus = _cardfileStatus;              // source: CBACT02C.cbl:130
            DisplayIoStatus9910();                    // source: CBACT02C.cbl:131
            AbendProgram9999();                       // source: CBACT02C.cbl:132
        }
        // EXIT  // source: CBACT02C.cbl:134
    }

    // *---------------------------------------------------------------*
    /// <summary>
    /// 9000-CARDFILE-CLOSE — CLOSE the card file (ends the browse). // source: CBACT02C.cbl:136-152
    /// </summary>
    private void CardfileClose9000()
    {
        _applResult = 8;                              // ADD 8 TO ZERO GIVING APPL-RESULT  // source: CBACT02C.cbl:137

        // CLOSE CARDFILE-FILE — relational: dispose the read cursor; always succeeds with '00'.
        _cardFile.EndBrowse();                        // source: CBACT02C.cbl:138
        _cardfileStatus = FileStatus.Ok;

        if (_cardfileStatus == FileStatus.Ok)         // CARDFILE-STATUS = '00'  // source: CBACT02C.cbl:139
            _applResult = _applResult - _applResult;  // SUBTRACT APPL-RESULT FROM APPL-RESULT (-> 0)  // source: CBACT02C.cbl:140
        else
            _applResult = 12;                         // ADD 12 TO ZERO GIVING APPL-RESULT  // source: CBACT02C.cbl:142

        if (ApplAok)                                  // source: CBACT02C.cbl:144
        {
            // CONTINUE  // source: CBACT02C.cbl:145
        }
        else
        {
            Display("ERROR CLOSING CARDFILE");        // source: CBACT02C.cbl:147
            _ioStatus = _cardfileStatus;              // source: CBACT02C.cbl:148
            DisplayIoStatus9910();                    // source: CBACT02C.cbl:149
            AbendProgram9999();                       // source: CBACT02C.cbl:150
        }
        // EXIT  // source: CBACT02C.cbl:152
    }

    /// <summary>
    /// 9999-ABEND-PROGRAM — CALL 'CEE3ABD' USING ABCODE(999), TIMING(0). // source: CBACT02C.cbl:154-158
    /// </summary>
    private void AbendProgram9999()
    {
        Display("ABENDING PROGRAM");                  // source: CBACT02C.cbl:155
        // MOVE 0 TO TIMING; MOVE 999 TO ABCODE; CALL 'CEE3ABD' USING ABCODE, TIMING.
        // source: CBACT02C.cbl:156-158
        throw new AbendException("999", $"CBACT02C abend; FILE STATUS '{_cardfileStatus}'.");
    }

    // *****************************************************************
    /// <summary>
    /// 9910-DISPLAY-IO-STATUS — formats the 2-byte file status into a 4-char "NNNN" string and DISPLAYs
    /// <c>'FILE STATUS IS: NNNN' IO-STATUS-04</c>. // source: CBACT02C.cbl:161-174
    /// </summary>
    private void DisplayIoStatus9910()
    {
        // IO-STATUS-04 = IO-STATUS-0401 PIC 9 + IO-STATUS-0403 PIC 999  // source: CBACT02C.cbl:57-59
        string ioStat1 = _ioStatus.Length > 0 ? _ioStatus[..1] : " ";
        string ioStat2 = _ioStatus.Length > 1 ? _ioStatus.Substring(1, 1) : " ";

        string ioStatus04;

        // IF IO-STATUS NOT NUMERIC OR IO-STAT1 = '9'  // source: CBACT02C.cbl:162-163
        if (!IsNumeric(_ioStatus) || ioStat1 == "9")
        {
            // MOVE IO-STAT1 TO IO-STATUS-04(1:1) — first char is the stat1 byte as-is.  // source: CBACT02C.cbl:164
            char pos1 = ioStat1.Length > 0 ? ioStat1[0] : ' ';

            // MOVE 0 TO TWO-BYTES-BINARY; MOVE IO-STAT2 TO TWO-BYTES-RIGHT;
            // MOVE TWO-BYTES-BINARY TO IO-STATUS-0403.  // source: CBACT02C.cbl:165-167
            // FAITHFUL BUG #3: TWO-BYTES-BINARY is PIC 9(4) BINARY (big-endian halfword) redefined as two
            // bytes; only the low (right) byte is set from the status character, so reading the halfword
            // yields that character's EBCDIC code point (e.g. '0' (0xF0) -> 240). Reproduce exactly.
            char stat2Char = ioStat2.Length > 0 ? ioStat2[0] : ' ';
            int twoBytesBinary = EbcdicCodePoint(stat2Char);     // big-endian: value == low byte
            int ioStatus0403 = twoBytesBinary % 1000;            // store into PIC 999 (3 digits)

            // FAITHFUL BUG #1: the literal placeholder 'NNNN' is emitted, then the value.
            ioStatus04 = pos1.ToString() + ioStatus0403.ToString("D3"); // source: CBACT02C.cbl:168
        }
        else
        {
            // MOVE '0000' TO IO-STATUS-04; MOVE IO-STATUS TO IO-STATUS-04(3:2).  // source: CBACT02C.cbl:170-171
            ioStatus04 = "00" + _ioStatus.PadRight(2)[..2];
        }

        // DISPLAY 'FILE STATUS IS: NNNN' IO-STATUS-04  // source: CBACT02C.cbl:168,172
        Display("FILE STATUS IS: NNNN" + ioStatus04);
        // EXIT  // source: CBACT02C.cbl:174
    }

    // --- Helpers ---------------------------------------------------------------------------------

    /// <summary>
    /// Reproduces <c>DISPLAY CARD-RECORD</c>: the 150-byte CVACT02Y logical image rendered as text — the
    /// six elementary fields in copybook order then a 59-byte FILLER of spaces. Numeric fields are zoned
    /// (USAGE DISPLAY) zero-padded; alphanumeric fields are left-justified, space-padded.
    /// </summary>
    /// <remarks>
    /// // source: CVACT02Y.cpy:4-11 (CARD-NUM X16, CARD-ACCT-ID 9(11), CARD-CVV-CD 9(3),
    /// CARD-EMBOSSED-NAME X50, CARD-EXPIRAION-DATE X10 [misspelled — FAITHFUL BUG #2],
    /// CARD-ACTIVE-STATUS X1, FILLER X59).
    /// </remarks>
    private static string FormatCardRecord(Card c)
    {
        var sb = new StringBuilder(150);
        sb.Append(Alpha(c.CardNum, 16));            // CARD-NUM            PIC X(16)
        sb.Append(Zoned(c.AcctId, 11));             // CARD-ACCT-ID        PIC 9(11)
        sb.Append(Zoned(c.CvvCd, 3));               // CARD-CVV-CD         PIC 9(03)
        sb.Append(Alpha(c.EmbossedName, 50));       // CARD-EMBOSSED-NAME  PIC X(50)
        sb.Append(Alpha(c.ExpirationDate, 10));     // CARD-EXPIRAION-DATE PIC X(10) (misspelled in source)
        sb.Append(Alpha(c.ActiveStatus, 1));        // CARD-ACTIVE-STATUS  PIC X(01)
        sb.Append(new string(' ', 59));             // FILLER              PIC X(59)
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
