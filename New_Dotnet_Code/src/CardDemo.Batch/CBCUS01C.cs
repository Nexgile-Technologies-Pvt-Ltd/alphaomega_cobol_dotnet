using System.Text;
using CardDemo.Cobol.Runtime;
using CardDemo.Data;
using CardDemo.Domain;

namespace CardDemo.Batch;

/// <summary>
/// Faithful relational re-port of the batch/util program <c>CBCUS01C</c> ("Read and print customer data
/// file"). It opens the customer master (the VSAM KSDS <c>CUSTFILE</c>, accessed SEQUENTIAL by
/// <c>FD-CUST-ID</c>, now the relational <b>CUSTOMER</b> table) as a forward, primary-key-ordered read
/// cursor, <c>DISPLAY</c>s each CUSTOMER-RECORD to SYSOUT, then closes the file. It performs no writes,
/// updates, deletes, computation, or transformation — a pure read-and-print utility.
/// </summary>
/// <remarks>
/// <para>
/// Ported paragraph-by-paragraph from <c>app/cbl/CBCUS01C.cbl</c>; each PROCEDURE-DIVISION paragraph is a
/// method whose name mirrors the COBOL paragraph and whose body keeps the original statement order and
/// control flow (with <c>// source: CBCUS01C.cbl:NNN</c> citations). Per <c>_design/ARCHITECTURE.md</c>
/// the single VSAM file (CUSTFILE) is the only relational access; it is read through
/// <see cref="CustomerRepository"/>: <c>OPEN INPUT</c> -&gt; <see cref="CustomerRepository.StartBrowse"/>,
/// sequential <c>READ</c> -&gt; <see cref="CustomerRepository.ReadNext"/> (FileStatus '00'/'10'),
/// <c>CLOSE</c> -&gt; <see cref="CustomerRepository.EndBrowse"/>. The browse is ordered by the PK
/// <c>cust_id</c> ASC, reproducing the KSDS sequential (READ NEXT) order. There are no output files.
/// </para>
/// <para>
/// SYSOUT (every COBOL <c>DISPLAY</c>) is collected into <see cref="Sysout"/> in order; when an output
/// path is supplied the same lines are also written to that flat report file via
/// <see cref="BatchSupport.OpenWriter"/>.
/// </para>
/// <para>FAITHFUL BUGS reproduced verbatim (see <c>_design/specs/CBCUS01C.md</c> §6):
/// <list type="number">
/// <item><b>Each customer record is DISPLAYed twice</b> — once inside <c>1000-CUSTFILE-GET-NEXT</c>
/// (line 96) on a '00' read, then again in the MAIN loop (line 78). Both DISPLAYs are reproduced in the
/// same order.</item>
/// <item><b>The <c>'FILE STATUS IS: NNNN'</c> literal includes a literal <c>NNNN</c></b> placeholder
/// printed in front of the formatted status value (e.g. <c>FILE STATUS IS: NNNN0010</c>).</item>
/// <item><b>The non-numeric / '9' branch of <c>Z-DISPLAY-IO-STATUS</c></b> renders only IO-STAT1 in
/// position 1 and the raw byte value of IO-STAT2 as a 3-digit number (the big-endian EBCDIC halfword
/// aliasing idiom), not a clean status. Reproduced exactly.</item>
/// </list></para>
/// </remarks>
public sealed class Cbcus01c
{
    // --- WORKING-STORAGE (CBCUS01C lines 42-67) --------------------------------------------------

    /// <summary>CUSTFILE-STATUS — the file-status of the CUSTOMER cursor (2 chars). // source: CBCUS01C.cbl:46-48</summary>
    private string _custfileStatus = "00";

    /// <summary>IO-STATUS — working copy of the status used by the display routine. // source: CBCUS01C.cbl:50-52</summary>
    private string _ioStatus = "00";

    /// <summary>APPL-RESULT PIC S9(9) COMP. 0=ok(AOK), 16=EOF, 8=pre-op, 12=hard error. // source: CBCUS01C.cbl:61-63</summary>
    private int _applResult;

    /// <summary>END-OF-FILE PIC X(01) VALUE 'N' — main-loop sentinel. // source: CBCUS01C.cbl:65</summary>
    private bool _endOfFile;

    /// <summary>CUSTOMER-RECORD — the record READ ... INTO populates (CVCUS01Y, 500 bytes). // source: CBCUS01C.cbl:45,93</summary>
    private Customer? _customerRecord;

    /// <summary>The CUSTOMER master cursor (VSAM KSDS -&gt; relational CUSTOMER table). // source: CBCUS01C.cbl:29-33</summary>
    private CustomerRepository _custFile = null!;

    /// <summary>Optional flat report writer mirroring SYSOUT. // source: CBCUS01C.cbl:78,96,168,172</summary>
    private FixedFileWriter? _writer;

    private readonly List<string> _sysout = [];

    /// <summary>88 APPL-AOK VALUE 0. // source: CBCUS01C.cbl:62</summary>
    private bool ApplAok => _applResult == 0;

    /// <summary>88 APPL-EOF VALUE 16. // source: CBCUS01C.cbl:63</summary>
    private bool ApplEof => _applResult == 16;

    /// <summary>The SYSOUT (DISPLAY) lines produced by the run, in order.</summary>
    public IReadOnlyList<string> Sysout => _sysout;

    /// <summary>
    /// Runs CBCUS01C over the relational <paramref name="db"/>. When <paramref name="sysoutPath"/> is
    /// given, every DISPLAY line is also appended to that flat report file (ASCII host) in addition to
    /// being collected into <see cref="Sysout"/>.
    /// </summary>
    /// <returns>The process RETURN-CODE (CBCUS01C leaves it 0). // source: CBCUS01C.cbl:87 (GOBACK)</returns>
    public int Run(RelationalDb db, string? sysoutPath = null)
        => Run(new CustomerRepository(db), sysoutPath);

    /// <summary>Convenience overload taking a <see cref="BatchSupport"/> (uses its CUSTOMER repository).</summary>
    public int Run(BatchSupport support, string? sysoutPath = null)
        => Run(support.Customer, sysoutPath);

    /// <summary>
    /// Runs CBCUS01C over an already-resolved <see cref="CustomerRepository"/> (the relational CUSTFILE).
    /// Returns the SYSOUT (DISPLAY) lines via <see cref="Sysout"/> and the RETURN-CODE (0).
    /// </summary>
    public int Run(CustomerRepository custFile, string? sysoutPath = null)
    {
        _custFile = custFile;
        _writer = sysoutPath is null ? null : BatchSupport.OpenWriter(sysoutPath, HostKind.Ascii);
        try
        {
            // source: CBCUS01C.cbl:71
            Display("START OF EXECUTION OF PROGRAM CBCUS01C");
            // source: CBCUS01C.cbl:72
            CustfileOpen0000();

            // PERFORM UNTIL END-OF-FILE = 'Y'  // source: CBCUS01C.cbl:74-81
            while (!_endOfFile)
            {
                if (!_endOfFile) // IF END-OF-FILE = 'N'  // source: CBCUS01C.cbl:75
                {
                    CustfileGetNext1000(); // source: CBCUS01C.cbl:76
                    if (!_endOfFile)       // IF END-OF-FILE = 'N'  // source: CBCUS01C.cbl:77
                        Display(FormatCustomerRecord(_customerRecord!)); // DISPLAY CUSTOMER-RECORD  // source: CBCUS01C.cbl:78
                }
            }

            // source: CBCUS01C.cbl:83
            CustfileClose9000();
            // source: CBCUS01C.cbl:85
            Display("END OF EXECUTION OF PROGRAM CBCUS01C");

            // source: CBCUS01C.cbl:87 (GOBACK)
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
    /// 1000-CUSTFILE-GET-NEXT — sequential READ of the next customer row. // source: CBCUS01C.cbl:92-116
    /// </summary>
    private void CustfileGetNext1000()
    {
        // READ CUSTFILE-FILE INTO CUSTOMER-RECORD  // source: CBCUS01C.cbl:93
        _custfileStatus = _custFile.ReadNext(out Customer? customer);
        if (customer is not null)
            _customerRecord = customer; // READ ... INTO CUSTOMER-RECORD (only populated on a successful read)

        if (_custfileStatus == FileStatus.Ok)        // CUSTFILE-STATUS = '00'  // source: CBCUS01C.cbl:94
        {
            _applResult = 0;                          // MOVE 0 TO APPL-RESULT  // source: CBCUS01C.cbl:95
            // FAITHFUL BUG #1: DISPLAY CUSTOMER-RECORD here, in 1000-CUSTFILE-GET-NEXT, in addition to the
            // MAIN-loop DISPLAY at line 78 — so every record is printed twice. Reproduced, not fixed.
            Display(FormatCustomerRecord(_customerRecord!)); // DISPLAY CUSTOMER-RECORD  // source: CBCUS01C.cbl:96
        }
        else if (_custfileStatus == FileStatus.EndOfFile) // '10'  // source: CBCUS01C.cbl:98
        {
            _applResult = 16;                         // MOVE 16 TO APPL-RESULT  // source: CBCUS01C.cbl:99
        }
        else
        {
            _applResult = 12;                         // MOVE 12 TO APPL-RESULT  // source: CBCUS01C.cbl:101
        }

        if (ApplAok)                                  // IF APPL-AOK  // source: CBCUS01C.cbl:104
        {
            // CONTINUE  // source: CBCUS01C.cbl:105
        }
        else if (ApplEof)                             // IF APPL-EOF  // source: CBCUS01C.cbl:107
        {
            _endOfFile = true;                        // MOVE 'Y' TO END-OF-FILE  // source: CBCUS01C.cbl:108
        }
        else
        {
            Display("ERROR READING CUSTOMER FILE");    // source: CBCUS01C.cbl:110
            _ioStatus = _custfileStatus;              // MOVE CUSTFILE-STATUS TO IO-STATUS  // source: CBCUS01C.cbl:111
            ZDisplayIoStatus();                       // source: CBCUS01C.cbl:112
            ZAbendProgram();                          // source: CBCUS01C.cbl:113
        }
        // EXIT  // source: CBCUS01C.cbl:116
    }

    // *---------------------------------------------------------------*
    /// <summary>
    /// 0000-CUSTFILE-OPEN — OPEN INPUT the customer file (positions a forward, PK-ordered browse).
    /// // source: CBCUS01C.cbl:118-134
    /// </summary>
    private void CustfileOpen0000()
    {
        _applResult = 8;                              // MOVE 8 TO APPL-RESULT  // source: CBCUS01C.cbl:119

        // OPEN INPUT CUSTFILE-FILE — relational: position the read cursor at the first row in PK order.
        // The SQLite-backed table is always openable, so OPEN succeeds with status '00'.
        _custFile.StartBrowse();                      // source: CBCUS01C.cbl:120
        _custfileStatus = FileStatus.Ok;

        if (_custfileStatus == FileStatus.Ok)         // CUSTFILE-STATUS = '00'  // source: CBCUS01C.cbl:121
            _applResult = 0;                          // MOVE 0 TO APPL-RESULT  // source: CBCUS01C.cbl:122
        else
            _applResult = 12;                         // MOVE 12 TO APPL-RESULT  // source: CBCUS01C.cbl:124

        if (ApplAok)                                  // IF APPL-AOK  // source: CBCUS01C.cbl:126
        {
            // CONTINUE  // source: CBCUS01C.cbl:127
        }
        else
        {
            Display("ERROR OPENING CUSTFILE");        // source: CBCUS01C.cbl:129
            _ioStatus = _custfileStatus;              // MOVE CUSTFILE-STATUS TO IO-STATUS  // source: CBCUS01C.cbl:130
            ZDisplayIoStatus();                       // source: CBCUS01C.cbl:131
            ZAbendProgram();                          // source: CBCUS01C.cbl:132
        }
        // EXIT  // source: CBCUS01C.cbl:134
    }

    // *---------------------------------------------------------------*
    /// <summary>
    /// 9000-CUSTFILE-CLOSE — CLOSE the customer file (ends the browse). // source: CBCUS01C.cbl:136-152
    /// </summary>
    private void CustfileClose9000()
    {
        _applResult = 8;                              // ADD 8 TO ZERO GIVING APPL-RESULT (-> 8)  // source: CBCUS01C.cbl:137

        // CLOSE CUSTFILE-FILE — relational: dispose the read cursor; always succeeds with '00'.
        _custFile.EndBrowse();                        // source: CBCUS01C.cbl:138
        _custfileStatus = FileStatus.Ok;

        if (_custfileStatus == FileStatus.Ok)         // CUSTFILE-STATUS = '00'  // source: CBCUS01C.cbl:139
            _applResult = _applResult - _applResult;  // SUBTRACT APPL-RESULT FROM APPL-RESULT (-> 0)  // source: CBCUS01C.cbl:140
        else
            _applResult = 12;                         // ADD 12 TO ZERO GIVING APPL-RESULT (-> 12)  // source: CBCUS01C.cbl:142

        if (ApplAok)                                  // IF APPL-AOK  // source: CBCUS01C.cbl:144
        {
            // CONTINUE  // source: CBCUS01C.cbl:145
        }
        else
        {
            Display("ERROR CLOSING CUSTOMER FILE");   // source: CBCUS01C.cbl:147
            _ioStatus = _custfileStatus;              // MOVE CUSTFILE-STATUS TO IO-STATUS  // source: CBCUS01C.cbl:148
            ZDisplayIoStatus();                       // source: CBCUS01C.cbl:149
            ZAbendProgram();                          // source: CBCUS01C.cbl:150
        }
        // EXIT  // source: CBCUS01C.cbl:152
    }

    /// <summary>
    /// Z-ABEND-PROGRAM — CALL 'CEE3ABD' USING ABCODE(999), TIMING(0). // source: CBCUS01C.cbl:154-158
    /// </summary>
    private void ZAbendProgram()
    {
        Display("ABENDING PROGRAM");                  // source: CBCUS01C.cbl:155
        // MOVE 0 TO TIMING; MOVE 999 TO ABCODE; CALL 'CEE3ABD' USING ABCODE, TIMING.
        // source: CBCUS01C.cbl:156-158
        throw new AbendException("999", $"CBCUS01C abend; FILE STATUS '{_custfileStatus}'.");
    }

    // *****************************************************************
    /// <summary>
    /// Z-DISPLAY-IO-STATUS — formats the 2-byte file status into a 4-char "NNNN" string and DISPLAYs
    /// <c>'FILE STATUS IS: NNNN' IO-STATUS-04</c>. // source: CBCUS01C.cbl:160-174
    /// </summary>
    private void ZDisplayIoStatus()
    {
        // IO-STATUS-04 = IO-STATUS-0401 PIC 9 + IO-STATUS-0403 PIC 999  // source: CBCUS01C.cbl:57-59
        string ioStat1 = _ioStatus.Length > 0 ? _ioStatus[..1] : " ";
        string ioStat2 = _ioStatus.Length > 1 ? _ioStatus.Substring(1, 1) : " ";

        string ioStatus04;

        // IF IO-STATUS NOT NUMERIC OR IO-STAT1 = '9'  // source: CBCUS01C.cbl:162-163
        if (!IsNumeric(_ioStatus) || ioStat1 == "9")
        {
            // MOVE IO-STAT1 TO IO-STATUS-04(1:1) — first char is the stat1 byte as-is.  // source: CBCUS01C.cbl:164
            char pos1 = ioStat1.Length > 0 ? ioStat1[0] : ' ';

            // MOVE 0 TO TWO-BYTES-BINARY; MOVE IO-STAT2 TO TWO-BYTES-RIGHT;
            // MOVE TWO-BYTES-BINARY TO IO-STATUS-0403.  // source: CBCUS01C.cbl:165-167
            // FAITHFUL BUG #3: TWO-BYTES-BINARY is PIC 9(4) BINARY (big-endian halfword) redefined as two
            // bytes; only the low (right) byte is set from the status character, so reading the halfword
            // yields that character's EBCDIC code point (e.g. '0' (0xF0) -> 240). Reproduce exactly.
            char stat2Char = ioStat2.Length > 0 ? ioStat2[0] : ' ';
            int twoBytesBinary = EbcdicCodePoint(stat2Char);     // big-endian: value == low byte
            int ioStatus0403 = twoBytesBinary % 1000;            // store into PIC 999 (3 digits)

            // FAITHFUL BUG #2: the literal placeholder 'NNNN' is emitted, then the formatted value.
            ioStatus04 = pos1.ToString() + ioStatus0403.ToString("D3"); // source: CBCUS01C.cbl:168
        }
        else
        {
            // MOVE '0000' TO IO-STATUS-04; MOVE IO-STATUS TO IO-STATUS-04(3:2).  // source: CBCUS01C.cbl:170-171
            ioStatus04 = "00" + _ioStatus.PadRight(2)[..2];
        }

        // DISPLAY 'FILE STATUS IS: NNNN' IO-STATUS-04  // source: CBCUS01C.cbl:168,172
        Display("FILE STATUS IS: NNNN" + ioStatus04);
        // EXIT  // source: CBCUS01C.cbl:174
    }

    // --- Helpers ---------------------------------------------------------------------------------

    /// <summary>
    /// Reproduces <c>DISPLAY CUSTOMER-RECORD</c>: the 500-byte CVCUS01Y logical image rendered as text —
    /// the eighteen elementary fields in copybook order then the 168-byte FILLER of spaces. Numeric fields
    /// (CUST-ID, CUST-SSN, CUST-FICO-CREDIT-SCORE) are unsigned zoned (USAGE DISPLAY) and rendered as plain
    /// zero-padded digits (no sign overpunch — they are PIC 9, not S9); alphanumeric fields are
    /// left-justified, space-padded.
    /// </summary>
    /// <remarks>
    /// // source: CVCUS01Y.cpy:5-23 (CUST-ID 9(9), CUST-FIRST-NAME X25, CUST-MIDDLE-NAME X25,
    /// CUST-LAST-NAME X25, CUST-ADDR-LINE-1/2/3 X50, CUST-ADDR-STATE-CD X2, CUST-ADDR-COUNTRY-CD X3,
    /// CUST-ADDR-ZIP X10, CUST-PHONE-NUM-1 X15, CUST-PHONE-NUM-2 X15, CUST-SSN 9(9),
    /// CUST-GOVT-ISSUED-ID X20, CUST-DOB-YYYY-MM-DD X10, CUST-EFT-ACCOUNT-ID X10,
    /// CUST-PRI-CARD-HOLDER-IND X1, CUST-FICO-CREDIT-SCORE 9(3), FILLER X168).
    /// </remarks>
    private static string FormatCustomerRecord(Customer c)
    {
        var sb = new StringBuilder(500);
        sb.Append(Zoned(c.CustId, 9));              // CUST-ID                  PIC 9(09)
        sb.Append(Alpha(c.FirstName, 25));          // CUST-FIRST-NAME          PIC X(25)
        sb.Append(Alpha(c.MiddleName, 25));         // CUST-MIDDLE-NAME         PIC X(25)
        sb.Append(Alpha(c.LastName, 25));           // CUST-LAST-NAME           PIC X(25)
        sb.Append(Alpha(c.AddrLine1, 50));          // CUST-ADDR-LINE-1         PIC X(50)
        sb.Append(Alpha(c.AddrLine2, 50));          // CUST-ADDR-LINE-2         PIC X(50)
        sb.Append(Alpha(c.AddrLine3, 50));          // CUST-ADDR-LINE-3         PIC X(50)
        sb.Append(Alpha(c.AddrStateCd, 2));         // CUST-ADDR-STATE-CD       PIC X(02)
        sb.Append(Alpha(c.AddrCountryCd, 3));       // CUST-ADDR-COUNTRY-CD     PIC X(03)
        sb.Append(Alpha(c.AddrZip, 10));            // CUST-ADDR-ZIP            PIC X(10)
        sb.Append(Alpha(c.PhoneNum1, 15));          // CUST-PHONE-NUM-1         PIC X(15)
        sb.Append(Alpha(c.PhoneNum2, 15));          // CUST-PHONE-NUM-2         PIC X(15)
        sb.Append(Zoned(c.Ssn, 9));                 // CUST-SSN                 PIC 9(09)
        sb.Append(Alpha(c.GovtIssuedId, 20));       // CUST-GOVT-ISSUED-ID      PIC X(20)
        sb.Append(Alpha(c.DobYyyyMmDd, 10));        // CUST-DOB-YYYY-MM-DD      PIC X(10)
        sb.Append(Alpha(c.EftAccountId, 10));       // CUST-EFT-ACCOUNT-ID      PIC X(10)
        sb.Append(Alpha(c.PriCardHolderInd, 1));    // CUST-PRI-CARD-HOLDER-IND PIC X(01)
        sb.Append(Zoned(c.FicoCreditScore, 3));     // CUST-FICO-CREDIT-SCORE   PIC 9(03)
        sb.Append(new string(' ', 168));            // FILLER                   PIC X(168)
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
    /// aliasing in Z-DISPLAY-IO-STATUS produces from a single status byte (FAITHFUL BUG #3).
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
