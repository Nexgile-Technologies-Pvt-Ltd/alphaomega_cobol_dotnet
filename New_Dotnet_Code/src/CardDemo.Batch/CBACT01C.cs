using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;
using CardDemo.Utilities;

namespace CardDemo.Batch;

/// <summary>
/// Faithful relational re-port of the batch/util program <c>CBACT01C</c> (account-file extract). It reads
/// the ACCOUNT master in key order (here the relational <c>ACCOUNT</c> table, browsed ascending by
/// <c>acct_id</c> — the VSAM KSDS sequential read), and for every account record: DISPLAYs every field to
/// SYSOUT, then derives and writes three sequential output datasets — a fixed-record account file
/// (<c>OUTFILE</c>, LRECL 107 FB), a fixed-record "array" file (<c>ARRYFILE</c>, LRECL 110 FB), and a
/// variable-length record file (<c>VBRCFILE</c>, RECFM VB, two physical records of length 12 and 39) —
/// each populated with a mix of source fields and hard-coded constants plus a reissue date reformatted by
/// the assembler subroutine <c>COBDATFT</c> (re-implemented as <see cref="Cobdatft"/>).
/// </summary>
/// <remarks>
/// <para>Ported paragraph-by-paragraph from <c>app/cbl/CBACT01C.cbl</c>; each PROCEDURE-DIVISION paragraph
/// is a method whose name mirrors the COBOL paragraph and whose body keeps the original statement order and
/// control flow (with <c>// source: CBACT01C.cbl:NNN</c> citations).</para>
/// <para>Per <c>_design/ARCHITECTURE.md</c>: only ACCTFILE maps to a base table (ACCOUNT, read via
/// <see cref="AccountRepository"/>); OUTFILE/ARRYFILE/VBRCFILE are derived QSAM datasets emitted as
/// byte-faithful fixed/variable-width record streams. The COMP-3 fields (OUT-ACCT-CURR-CYC-DEBIT and each
/// ARR-ACCT-CURR-CYC-DEBIT) are packed via <see cref="PackedDecimalCodec"/> so LRECL 107/110 reproduce
/// exactly; every other numeric is signed-zoned DISPLAY via <see cref="ZonedDecimalCodec"/>.</para>
/// <para>FAITHFUL BUGS preserved verbatim (see <c>_design/faithful-bugs.md</c>):
/// <list type="number">
/// <item>OUT-ACCT-CURR-CYC-DEBIT is assigned (2525.00) ONLY when the source debit is zero; otherwise the
/// output field keeps its prior-iteration value (OUT-ACCT-REC is never re-initialized per record) — a
/// genuine stale-data bug.</item>
/// <item>OUTFILE/ARRYFILE/VBRCFILE are never CLOSEd by the program; only ACCTFILE is closed. The port
/// flushes/closes them at end of run but adds no per-iteration close.</item>
/// <item>The array/VBRC write-error DISPLAY uses the misleading literal 'ACCOUNT FILE WRITE STATUS IS:'.</item>
/// <item>ARR-ACCT-BAL occurrences (4) and (5) are never populated (stay INITIALIZE-zeroed).</item>
/// <item>The misspelled name ACCT-EXPIRAION-DATE is carried through (column + SYSOUT label).</item>
/// <item>VBR-REC stale tail bytes: only WS-RECD-LEN bytes are written per VB record.</item>
/// <item>Hard-coded magic constants 2525.00, 1005.00, 1525.00, -1025.00, -2500.00.</item>
/// </list></para>
/// </remarks>
public sealed class Cbact01c
{
    // PIC widths/scales for the S9(10)V99 money fields used everywhere in the output records.
    private const int MoneyDigits = 12;   // 10 integer + 2 fraction
    private const int MoneyScale = 2;
    private const int Comp3Bytes = 7;     // ceil((12+1)/2) = 7 bytes for S9(10)V99 COMP-3

    private readonly AccountRepository _account;
    private readonly string _outFilePath;
    private readonly string _arryFilePath;
    private readonly string _vbrcFilePath;
    private readonly HostKind _host;
    private readonly List<string> _sysout = [];

    // Open output streams (held for the run; OUTFILE/ARRYFILE/VBRCFILE are never closed mid-run — bug #2).
    private FileStream? _outFile;
    private FileStream? _arryFile;
    private FileStream? _vbrcFile;

    // WORKING-STORAGE flags / status (CBACT01C lines 91-122).
    private string _acctfileStatus = "00"; // ACCTFILE-STATUS
    private string _outfileStatus = "00";  // OUTFILE-STATUS
    private string _arryfileStatus = "00"; // ARRYFILE-STATUS
    private string _vbrcfileStatus = "00"; // VBRCFILE-STATUS
    private int _applResult;               // APPL-RESULT S9(9) COMP (88 APPL-AOK=0, APPL-EOF=16)
    private bool _endOfFile;               // END-OF-FILE 'N'/'Y'

    // ACCOUNT-RECORD (CVACT01Y) — the record just READ INTO (null before the first successful read).
    private Account? _acct;

    // --- OUT-ACCT-REC (FD OUT-FILE) — instance state, NOT re-initialized per record (bug #1) -----------
    private long _outAcctId;                 // OUT-ACCT-ID 9(11)
    private string _outAcctActiveStatus = " "; // OUT-ACCT-ACTIVE-STATUS X(1)
    private decimal _outAcctCurrBal;          // OUT-ACCT-CURR-BAL S9(10)V99
    private decimal _outAcctCreditLimit;      // OUT-ACCT-CREDIT-LIMIT S9(10)V99
    private decimal _outAcctCashCreditLimit;  // OUT-ACCT-CASH-CREDIT-LIMIT S9(10)V99
    private string _outAcctOpenDate = new(' ', 10);       // OUT-ACCT-OPEN-DATE X(10)
    private string _outAcctExpiraionDate = new(' ', 10);  // OUT-ACCT-EXPIRAION-DATE X(10) (misspelled — bug #5)
    private string _outAcctReissueDate = new(' ', 10);    // OUT-ACCT-REISSUE-DATE X(10)
    private decimal _outAcctCurrCycCredit;    // OUT-ACCT-CURR-CYC-CREDIT S9(10)V99
    private decimal _outAcctCurrCycDebit;     // OUT-ACCT-CURR-CYC-DEBIT S9(10)V99 COMP-3 (stale — bug #1)
    private string _outAcctGroupId = new(' ', 10);        // OUT-ACCT-GROUP-ID X(10)

    // --- ARR-ARRAY-REC (FD ARRY-FILE) — re-INITIALIZEd each iteration (line 169) ----------------------
    private long _arrAcctId;                   // ARR-ACCT-ID 9(11)
    private readonly decimal[] _arrAcctCurrBal = new decimal[5];      // ARR-ACCT-CURR-BAL OCCURS 5
    private readonly decimal[] _arrAcctCurrCycDebit = new decimal[5]; // ARR-ACCT-CURR-CYC-DEBIT OCCURS 5 (COMP-3)
    private string _arrFiller = new(' ', 4);   // ARR-FILLER X(4)

    // --- VBRC-REC1 / VBRC-REC2 (built in 1500) -------------------------------------------------------
    private long _vb1AcctId;                    // VB1-ACCT-ID 9(11)
    private string _vb1AcctActiveStatus = " ";  // VB1-ACCT-ACTIVE-STATUS X(1)
    private long _vb2AcctId;                     // VB2-ACCT-ID 9(11)
    private decimal _vb2AcctCurrBal;            // VB2-ACCT-CURR-BAL S9(10)V99
    private decimal _vb2AcctCreditLimit;        // VB2-ACCT-CREDIT-LIMIT S9(10)V99
    private string _vb2AcctReissueYyyy = new(' ', 4); // VB2-ACCT-REISSUE-YYYY X(4)

    // --- WS-ACCT-REISSUE-DATE (X10 with embedded FILLERs) + WS-REISSUE-DATE redefine ------------------
    // Modeled as the flat 10-char string the redefine receives; WS-ACCT-REISSUE-YYYY = first 4 chars.
    private string _wsReissueDate = new(' ', 10); // WS-REISSUE-DATE (redefine of WS-ACCT-REISSUE-DATE)
    private string WsAcctReissueYyyy => _wsReissueDate.Length >= 4 ? _wsReissueDate[..4] : _wsReissueDate.PadRight(4);

    private Cbact01c(AccountRepository account, string outFilePath, string arryFilePath, string vbrcFilePath, HostKind host)
    {
        _account = account;
        _outFilePath = outFilePath;
        _arryFilePath = arryFilePath;
        _vbrcFilePath = vbrcFilePath;
        _host = host;
    }

    /// <summary>The SYSOUT (DISPLAY) lines produced by the run, in order.</summary>
    public IReadOnlyList<string> Sysout => _sysout;

    /// <summary>
    /// Runs CBACT01C over the relational ACCOUNT table, writing the OUTFILE/ARRYFILE/VBRCFILE datasets to
    /// the given paths. Returns the SYSOUT lines (DISPLAY output) in order.
    /// </summary>
    /// <param name="account">ACCOUNT master repository (read sequentially in key order).</param>
    /// <param name="outFilePath">OUTFILE dataset path (FB, LRECL 107).</param>
    /// <param name="arryFilePath">ARRYFILE dataset path (FB, LRECL 110).</param>
    /// <param name="vbrcFilePath">VBRCFILE dataset path (RECFM VB).</param>
    /// <param name="host">Host encoding for the output datasets (defaults to EBCDIC, the mainframe form).</param>
    public static IReadOnlyList<string> Run(
        AccountRepository account,
        string outFilePath,
        string arryFilePath,
        string vbrcFilePath,
        HostKind host = HostKind.Ebcdic)
    {
        var program = new Cbact01c(account, outFilePath, arryFilePath, vbrcFilePath, host);
        program.Execute();
        return program.Sysout;
    }

    /// <summary>Convenience overload taking a <see cref="BatchSupport"/> (uses its ACCOUNT repository).</summary>
    public static IReadOnlyList<string> Run(
        BatchSupport support,
        string outFilePath,
        string arryFilePath,
        string vbrcFilePath,
        HostKind host = HostKind.Ebcdic)
        => Run(support.Account, outFilePath, arryFilePath, vbrcFilePath, host);

    // =================================================================================================
    // MAIN (unnamed PROCEDURE DIVISION body) // source: CBACT01C.cbl:140-160
    // =================================================================================================
    private void Execute()
    {
        try
        {
            _sysout.Add("START OF EXECUTION OF PROGRAM CBACT01C");        // source: CBACT01C.cbl:141
            Open0000Acctfile();                                          // source: CBACT01C.cbl:142
            Open2000Outfile();                                           // source: CBACT01C.cbl:143
            Open3000Arrfile();                                           // source: CBACT01C.cbl:144
            Open4000Vbrfile();                                           // source: CBACT01C.cbl:145

            // PERFORM UNTIL END-OF-FILE = 'Y' (the inner IFs are redundant with the loop test but kept).
            while (!_endOfFile)                                          // source: CBACT01C.cbl:147
            {
                if (!_endOfFile)                                         // source: CBACT01C.cbl:148
                {
                    Get1000AcctfileNext();                               // source: CBACT01C.cbl:149
                    if (!_endOfFile)                                     // source: CBACT01C.cbl:150
                    {
                        _sysout.Add(DisplayAccountRecord());            // DISPLAY ACCOUNT-RECORD // source: CBACT01C.cbl:151
                    }
                }
            }

            Close9000Acctfile();                                        // source: CBACT01C.cbl:156

            _sysout.Add("END OF EXECUTION OF PROGRAM CBACT01C");         // source: CBACT01C.cbl:158
            // GOBACK — note: NO CLOSE for OUTFILE/ARRYFILE/VBRCFILE (bug #2). // source: CBACT01C.cbl:160
        }
        finally
        {
            // On the mainframe the runtime closes these at GOBACK; here we flush/close at end of run
            // (no per-iteration close was ever issued — bug #2).
            _outFile?.Dispose();
            _arryFile?.Dispose();
            _vbrcFile?.Dispose();
        }
    }

    // =================================================================================================
    // 1000-ACCTFILE-GET-NEXT // source: CBACT01C.cbl:165-198
    // =================================================================================================
    private void Get1000AcctfileNext()
    {
        // READ ACCTFILE-FILE INTO ACCOUNT-RECORD (sequential next). // source: CBACT01C.cbl:166
        _acctfileStatus = _account.ReadNext(out Account? next);

        if (_acctfileStatus == FileStatus.Ok)                          // source: CBACT01C.cbl:167
        {
            _acct = next;
            _applResult = 0;                                           // MOVE 0 TO APPL-RESULT // source: CBACT01C.cbl:168
            InitializeArrArrayRec();                                   // INITIALIZE ARR-ARRAY-REC // source: CBACT01C.cbl:169
            Display1100AcctRecord();                                   // source: CBACT01C.cbl:170
            Popul1300AcctRecord();                                     // source: CBACT01C.cbl:171
            Write1350AcctRecord();                                     // source: CBACT01C.cbl:172
            Popul1400ArrayRecord();                                    // source: CBACT01C.cbl:173
            Write1450ArryRecord();                                     // source: CBACT01C.cbl:174
            InitializeVbrcRec1();                                      // INITIALIZE VBRC-REC1 // source: CBACT01C.cbl:175
            Popul1500VbrcRecord();                                     // source: CBACT01C.cbl:176
            Write1550Vb1Record();                                      // source: CBACT01C.cbl:177
            Write1575Vb2Record();                                      // source: CBACT01C.cbl:178
        }
        else if (_acctfileStatus == FileStatus.EndOfFile)             // status '10' // source: CBACT01C.cbl:180
        {
            _applResult = 16;                                          // MOVE 16 TO APPL-RESULT // source: CBACT01C.cbl:181
        }
        else
        {
            _applResult = 12;                                          // MOVE 12 TO APPL-RESULT // source: CBACT01C.cbl:183
        }

        if (_applResult == 0)                                         // IF APPL-AOK // source: CBACT01C.cbl:186
        {
            // CONTINUE
        }
        else if (_applResult == 16)                                   // IF APPL-EOF // source: CBACT01C.cbl:189
        {
            _endOfFile = true;                                        // MOVE 'Y' TO END-OF-FILE // source: CBACT01C.cbl:190
        }
        else
        {
            _sysout.Add("ERROR READING ACCOUNT FILE");                // source: CBACT01C.cbl:192
            Display9910IoStatus(_acctfileStatus);                     // source: CBACT01C.cbl:193-194
            Abend9999Program();                                       // source: CBACT01C.cbl:195
        }
    }

    // -------------------------------------------------------------------------------------------------
    // 1100-DISPLAY-ACCT-RECORD // source: CBACT01C.cbl:200-213
    // Pure SYSOUT side-effect; the labels (2-space gaps) and misspelling are reproduced verbatim.
    private void Display1100AcctRecord()
    {
        Account a = _acct!;
        _sysout.Add("ACCT-ID                 :" + Zoned(a.AcctId, 11, 0, signed: false)); // source: CBACT01C.cbl:201
        _sysout.Add("ACCT-ACTIVE-STATUS      :" + a.ActiveStatus);                        // source: CBACT01C.cbl:202
        _sysout.Add("ACCT-CURR-BAL           :" + Zoned(a.CurrBal, MoneyDigits, MoneyScale, signed: true));        // source: CBACT01C.cbl:203
        _sysout.Add("ACCT-CREDIT-LIMIT       :" + Zoned(a.CreditLimit, MoneyDigits, MoneyScale, signed: true));    // source: CBACT01C.cbl:204
        _sysout.Add("ACCT-CASH-CREDIT-LIMIT  :" + Zoned(a.CashCreditLimit, MoneyDigits, MoneyScale, signed: true));// source: CBACT01C.cbl:205
        _sysout.Add("ACCT-OPEN-DATE          :" + a.OpenDate);                            // source: CBACT01C.cbl:206
        _sysout.Add("ACCT-EXPIRAION-DATE     :" + a.ExpirationDate);                      // source: CBACT01C.cbl:207 (misspelled — bug #5)
        _sysout.Add("ACCT-REISSUE-DATE       :" + a.ReissueDate);                         // source: CBACT01C.cbl:208
        _sysout.Add("ACCT-CURR-CYC-CREDIT    :" + Zoned(a.CurrCycCredit, MoneyDigits, MoneyScale, signed: true));  // source: CBACT01C.cbl:209
        _sysout.Add("ACCT-CURR-CYC-DEBIT     :" + Zoned(a.CurrCycDebit, MoneyDigits, MoneyScale, signed: true));   // source: CBACT01C.cbl:210
        _sysout.Add("ACCT-GROUP-ID           :" + a.GroupId);                             // source: CBACT01C.cbl:211
        _sysout.Add("-------------------------------------------------");                 // source: CBACT01C.cbl:212
    }

    // -------------------------------------------------------------------------------------------------
    // 1300-POPUL-ACCT-RECORD (builds OUT-ACCT-REC) // source: CBACT01C.cbl:215-240
    private void Popul1300AcctRecord()
    {
        Account a = _acct!;
        _outAcctId = a.AcctId;                                  // source: CBACT01C.cbl:216
        _outAcctActiveStatus = Fixed(a.ActiveStatus, 1);        // source: CBACT01C.cbl:217
        _outAcctCurrBal = a.CurrBal;                            // source: CBACT01C.cbl:218
        _outAcctCreditLimit = a.CreditLimit;                    // source: CBACT01C.cbl:219
        _outAcctCashCreditLimit = a.CashCreditLimit;            // source: CBACT01C.cbl:220
        _outAcctOpenDate = Fixed(a.OpenDate, 10);               // source: CBACT01C.cbl:221
        _outAcctExpiraionDate = Fixed(a.ExpirationDate, 10);    // source: CBACT01C.cbl:222

        // MOVE ACCT-REISSUE-DATE TO CODATECN-INP-DATE WS-REISSUE-DATE. // source: CBACT01C.cbl:223-224
        // ACCT-REISSUE-DATE is X(10); CODATECN-INP-DATE is X(20) -> right space-padded.
        // WS-REISSUE-DATE (X10 redefine) receives the raw 10 chars.
        _wsReissueDate = Fixed(a.ReissueDate, 10);

        // CODATECN-REC is an 80-byte buffer: TYPE(0,1) INP-DATE(1,20) OUTTYPE(21,1) OUT-DATE(22,20) ERR(42,38).
        byte[] codatecn = BlankCodatecnRec();
        PutText(codatecn, 1, Fixed(a.ReissueDate, 10).PadRight(20), 20); // CODATECN-INP-DATE
        PutText(codatecn, 0, "2", 1);   // MOVE '2' TO CODATECN-TYPE    // source: CBACT01C.cbl:225
        PutText(codatecn, 21, "2", 1);  // MOVE '2' TO CODATECN-OUTTYPE // source: CBACT01C.cbl:226

        // CALL 'COBDATFT' USING CODATECN-REC. // source: CBACT01C.cbl:231
        Cobdatft.Convert(codatecn, _host);

        // MOVE CODATECN-0UT-DATE (X20) TO OUT-ACCT-REISSUE-DATE (X10) -> first 10 chars. // source: CBACT01C.cbl:233
        _outAcctReissueDate = GetText(codatecn, 22, 20)[..10];

        _outAcctCurrCycCredit = a.CurrCycCredit;                // source: CBACT01C.cbl:235

        // FAITHFUL BUG #1: OUT-ACCT-CURR-CYC-DEBIT is assigned ONLY when the source debit is zero; when
        // the source is non-zero the field is left untouched (keeps the prior iteration's value, or its
        // initial 0 on the first record — OUT-ACCT-REC is never re-initialized). // source: CBACT01C.cbl:236-238
        if (a.CurrCycDebit == 0m)
            _outAcctCurrCycDebit = 2525.00m;

        _outAcctGroupId = Fixed(a.GroupId, 10);                 // source: CBACT01C.cbl:239
    }

    // -------------------------------------------------------------------------------------------------
    // 1350-WRITE-ACCT-RECORD // source: CBACT01C.cbl:242-251
    private void Write1350AcctRecord()
    {
        // Serialize OUT-ACCT-REC (LRECL 107): 11 zoned + 1 X + 4*12 zoned money + 10+10+10 X + 12 zoned
        // money + 7 COMP-3 + 10 X.
        byte[] image = BuildOutAcctRec();
        _outFile!.Write(image);                                  // WRITE OUT-ACCT-REC // source: CBACT01C.cbl:243
        _outfileStatus = FileStatus.Ok;

        if (_outfileStatus != FileStatus.Ok && _outfileStatus != FileStatus.EndOfFile) // source: CBACT01C.cbl:245
        {
            _sysout.Add("ACCOUNT FILE WRITE STATUS IS:" + _outfileStatus); // source: CBACT01C.cbl:246
            Display9910IoStatus(_outfileStatus);                           // source: CBACT01C.cbl:247-248
            Abend9999Program();                                            // source: CBACT01C.cbl:249
        }
    }

    // -------------------------------------------------------------------------------------------------
    // 1400-POPUL-ARRAY-RECORD (builds ARR-ARRAY-REC) // source: CBACT01C.cbl:253-261
    private void Popul1400ArrayRecord()
    {
        Account a = _acct!;
        _arrAcctId = a.AcctId;                       // source: CBACT01C.cbl:254
        _arrAcctCurrBal[0] = a.CurrBal;              // ARR-ACCT-CURR-BAL(1) // source: CBACT01C.cbl:255
        _arrAcctCurrCycDebit[0] = 1005.00m;          // ARR-ACCT-CURR-CYC-DEBIT(1) (magic) // source: CBACT01C.cbl:256
        _arrAcctCurrBal[1] = a.CurrBal;              // ARR-ACCT-CURR-BAL(2) // source: CBACT01C.cbl:257
        _arrAcctCurrCycDebit[1] = 1525.00m;          // ARR-ACCT-CURR-CYC-DEBIT(2) (magic) // source: CBACT01C.cbl:258
        _arrAcctCurrBal[2] = -1025.00m;              // ARR-ACCT-CURR-BAL(3) (magic) // source: CBACT01C.cbl:259
        _arrAcctCurrCycDebit[2] = -2500.00m;         // ARR-ACCT-CURR-CYC-DEBIT(3) (magic) // source: CBACT01C.cbl:260
        // FAITHFUL BUG #4: occurrences (4) and (5) are never populated -> stay INITIALIZE-zeroed.
    }

    // -------------------------------------------------------------------------------------------------
    // 1450-WRITE-ARRY-RECORD // source: CBACT01C.cbl:263-274
    private void Write1450ArryRecord()
    {
        // Serialize ARR-ARRAY-REC (LRECL 110): 11 zoned + 5*(12 zoned + 7 COMP-3) + 4 X.
        byte[] image = BuildArrArrayRec();
        _arryFile!.Write(image);                                 // WRITE ARR-ARRAY-REC // source: CBACT01C.cbl:264
        _arryfileStatus = FileStatus.Ok;

        if (_arryfileStatus != FileStatus.Ok && _arryfileStatus != FileStatus.EndOfFile) // source: CBACT01C.cbl:266-267
        {
            // FAITHFUL BUG #3: misleading 'ACCOUNT FILE' label for the array-file write error.
            _sysout.Add("ACCOUNT FILE WRITE STATUS IS:" + _arryfileStatus); // source: CBACT01C.cbl:268-269
            Display9910IoStatus(_arryfileStatus);                           // source: CBACT01C.cbl:270-271
            Abend9999Program();                                            // source: CBACT01C.cbl:272
        }
    }

    // -------------------------------------------------------------------------------------------------
    // 1500-POPUL-VBRC-RECORD (builds VBRC-REC1 & VBRC-REC2) // source: CBACT01C.cbl:276-285
    private void Popul1500VbrcRecord()
    {
        Account a = _acct!;
        _vb1AcctId = a.AcctId;                          // source: CBACT01C.cbl:277
        _vb2AcctId = a.AcctId;                           // source: CBACT01C.cbl:278
        _vb1AcctActiveStatus = Fixed(a.ActiveStatus, 1); // source: CBACT01C.cbl:279
        _vb2AcctCurrBal = a.CurrBal;                     // source: CBACT01C.cbl:280
        _vb2AcctCreditLimit = a.CreditLimit;             // source: CBACT01C.cbl:281
        _vb2AcctReissueYyyy = WsAcctReissueYyyy;         // MOVE WS-ACCT-REISSUE-YYYY // source: CBACT01C.cbl:282
        _sysout.Add("VBRC-REC1:" + BuildVbrcRec1Text()); // source: CBACT01C.cbl:283
        _sysout.Add("VBRC-REC2:" + BuildVbrcRec2Text()); // source: CBACT01C.cbl:284
    }

    // -------------------------------------------------------------------------------------------------
    // 1550-WRITE-VB1-RECORD // source: CBACT01C.cbl:287-300
    private void Write1550Vb1Record()
    {
        // MOVE 12 TO WS-RECD-LEN; MOVE VBRC-REC1 TO VBR-REC(1:12); WRITE VBR-REC. // source: CBACT01C.cbl:288-290
        const int len = 12;
        byte[] data = BuildVbrcRec1Bytes(); // exactly 12 bytes (VB1-ACCT-ID 9(11) + status X1)
        WriteVbRecord(data, len);
        _vbrcfileStatus = FileStatus.Ok;

        if (_vbrcfileStatus != FileStatus.Ok && _vbrcfileStatus != FileStatus.EndOfFile) // source: CBACT01C.cbl:292-293
        {
            // FAITHFUL BUG #3: misleading 'ACCOUNT FILE' label for the VBRC-file write error.
            _sysout.Add("ACCOUNT FILE WRITE STATUS IS:" + _vbrcfileStatus); // source: CBACT01C.cbl:294-295
            Display9910IoStatus(_vbrcfileStatus);                           // source: CBACT01C.cbl:296-297
            Abend9999Program();                                            // source: CBACT01C.cbl:298
        }
    }

    // -------------------------------------------------------------------------------------------------
    // 1575-WRITE-VB2-RECORD // source: CBACT01C.cbl:302-315
    private void Write1575Vb2Record()
    {
        // MOVE 39 TO WS-RECD-LEN; MOVE VBRC-REC2 TO VBR-REC(1:39); WRITE VBR-REC. // source: CBACT01C.cbl:303-305
        const int len = 39;
        byte[] data = BuildVbrcRec2Bytes(); // exactly 39 bytes (id 11 + bal 12 + limit 12 + yyyy 4)
        WriteVbRecord(data, len);
        _vbrcfileStatus = FileStatus.Ok;

        if (_vbrcfileStatus != FileStatus.Ok && _vbrcfileStatus != FileStatus.EndOfFile) // source: CBACT01C.cbl:307-308
        {
            _sysout.Add("ACCOUNT FILE WRITE STATUS IS:" + _vbrcfileStatus); // source: CBACT01C.cbl:309-310
            Display9910IoStatus(_vbrcfileStatus);                           // source: CBACT01C.cbl:311-312
            Abend9999Program();                                            // source: CBACT01C.cbl:313
        }
    }

    // =================================================================================================
    // 0000-ACCTFILE-OPEN // source: CBACT01C.cbl:317-333
    // =================================================================================================
    private void Open0000Acctfile()
    {
        _applResult = 8;                              // source: CBACT01C.cbl:318
        // OPEN INPUT ACCTFILE-FILE -> position the forward browse over ACCOUNT (ascending acct_id).
        _account.StartBrowse();
        _acctfileStatus = FileStatus.Ok;             // OPEN of a present table succeeds.
        if (_acctfileStatus == FileStatus.Ok) _applResult = 0; else _applResult = 12; // source: CBACT01C.cbl:320-323
        if (_applResult == 0) { /* CONTINUE */ }     // source: CBACT01C.cbl:325
        else
        {
            _sysout.Add("ERROR OPENING ACCTFILE");                  // source: CBACT01C.cbl:328
            Display9910IoStatus(_acctfileStatus);                  // source: CBACT01C.cbl:329-330
            Abend9999Program();                                    // source: CBACT01C.cbl:331
        }
    }

    // 2000-OUTFILE-OPEN // source: CBACT01C.cbl:334-350
    private void Open2000Outfile()
    {
        _applResult = 8;                             // source: CBACT01C.cbl:335
        // OPEN OUTPUT OUT-FILE -> create fresh (the JCL PREDEL step deletes it first; DISP=NEW).
        _outFile = OpenOutput(_outFilePath);
        _outfileStatus = FileStatus.Ok;
        if (_outfileStatus == FileStatus.Ok) _applResult = 0; else _applResult = 12; // source: CBACT01C.cbl:337-340
        if (_applResult == 0) { /* CONTINUE */ }     // source: CBACT01C.cbl:342
        else
        {
            _sysout.Add("ERROR OPENING OUTFILE" + _outfileStatus); // source: CBACT01C.cbl:345
            Display9910IoStatus(_outfileStatus);                   // source: CBACT01C.cbl:346-347
            Abend9999Program();                                    // source: CBACT01C.cbl:348
        }
    }

    // 3000-ARRFILE-OPEN // source: CBACT01C.cbl:352-368
    private void Open3000Arrfile()
    {
        _applResult = 8;                             // source: CBACT01C.cbl:353
        _arryFile = OpenOutput(_arryFilePath);       // OPEN OUTPUT ARRY-FILE // source: CBACT01C.cbl:354
        _arryfileStatus = FileStatus.Ok;
        if (_arryfileStatus == FileStatus.Ok) _applResult = 0; else _applResult = 12; // source: CBACT01C.cbl:355-358
        if (_applResult == 0) { /* CONTINUE */ }     // source: CBACT01C.cbl:360
        else
        {
            _sysout.Add("ERROR OPENING ARRAYFILE" + _arryfileStatus); // source: CBACT01C.cbl:363
            Display9910IoStatus(_arryfileStatus);                     // source: CBACT01C.cbl:364-365
            Abend9999Program();                                       // source: CBACT01C.cbl:366
        }
    }

    // 4000-VBRFILE-OPEN // source: CBACT01C.cbl:370-386
    private void Open4000Vbrfile()
    {
        _applResult = 8;                             // source: CBACT01C.cbl:371
        _vbrcFile = OpenOutput(_vbrcFilePath);       // OPEN OUTPUT VBRC-FILE // source: CBACT01C.cbl:372
        _vbrcfileStatus = FileStatus.Ok;
        if (_vbrcfileStatus == FileStatus.Ok) _applResult = 0; else _applResult = 12; // source: CBACT01C.cbl:373-376
        if (_applResult == 0) { /* CONTINUE */ }     // source: CBACT01C.cbl:378
        else
        {
            _sysout.Add("ERROR OPENING VBRC FILE" + _vbrcfileStatus); // source: CBACT01C.cbl:381
            Display9910IoStatus(_vbrcfileStatus);                     // source: CBACT01C.cbl:382-383
            Abend9999Program();                                       // source: CBACT01C.cbl:384
        }
    }

    // 9000-ACCTFILE-CLOSE // source: CBACT01C.cbl:388-404 (only ACCTFILE is closed — bug #2)
    private void Close9000Acctfile()
    {
        _applResult = 8;                             // ADD 8 TO ZERO GIVING APPL-RESULT // source: CBACT01C.cbl:389
        _account.EndBrowse();                        // CLOSE ACCTFILE-FILE // source: CBACT01C.cbl:390
        _acctfileStatus = FileStatus.Ok;
        if (_acctfileStatus == FileStatus.Ok) _applResult -= _applResult; else _applResult = 12; // source: CBACT01C.cbl:391-395
        if (_applResult == 0) { /* CONTINUE */ }     // source: CBACT01C.cbl:396
        else
        {
            _sysout.Add("ERROR CLOSING ACCOUNT FILE");             // source: CBACT01C.cbl:399
            Display9910IoStatus(_acctfileStatus);                  // source: CBACT01C.cbl:400-401
            Abend9999Program();                                    // source: CBACT01C.cbl:402
        }
    }

    // 9999-ABEND-PROGRAM // source: CBACT01C.cbl:406-410
    private void Abend9999Program()
    {
        _sysout.Add("ABENDING PROGRAM");             // source: CBACT01C.cbl:407
        // MOVE 0 TO TIMING; MOVE 999 TO ABCODE; CALL 'CEE3ABD' USING ABCODE, TIMING.
        throw new AbendException("999", "CBACT01C abend (CEE3ABD).");
    }

    // 9910-DISPLAY-IO-STATUS // source: CBACT01C.cbl:413-426
    private void Display9910IoStatus(string ioStatus)
    {
        string s = ioStatus.Length >= 2 ? ioStatus[..2] : ioStatus.PadRight(2);
        char stat1 = s[0], stat2 = s[1];
        bool numeric = char.IsDigit(stat1) && char.IsDigit(stat2);

        if (!numeric || stat1 == '9')                // source: CBACT01C.cbl:414-415
        {
            // TWO-BYTES-BINARY is a 9(4) BINARY (big-endian halfword); MOVE IO-STAT2 TO its low byte,
            // then render the binary value as a 3-digit number. So digit3 = (int)(byte)IO-STAT2 formatted
            // %03d, with IO-STAT1 placed in the first display digit. // source: CBACT01C.cbl:416-420
            int low = HostEncoding.For(_host).GetBytes(stat2.ToString())[0];
            string status04 = stat1.ToString() + low.ToString("D3");
            _sysout.Add("FILE STATUS IS: NNNN" + status04);        // source: CBACT01C.cbl:420
        }
        else
        {
            // '0000' with the 2-digit status in positions 3-4. // source: CBACT01C.cbl:422-424
            string status04 = "00" + s;
            _sysout.Add("FILE STATUS IS: NNNN" + status04);        // source: CBACT01C.cbl:424
        }
    }

    // =================================================================================================
    // INITIALIZE helpers
    // =================================================================================================

    // INITIALIZE ARR-ARRAY-REC: numerics -> 0, alphanumerics -> spaces. // source: CBACT01C.cbl:169
    private void InitializeArrArrayRec()
    {
        _arrAcctId = 0;
        Array.Clear(_arrAcctCurrBal);
        Array.Clear(_arrAcctCurrCycDebit);
        _arrFiller = new string(' ', 4);
    }

    // INITIALIZE VBRC-REC1: VB1-ACCT-ID -> 0, VB1-ACCT-ACTIVE-STATUS -> space. // source: CBACT01C.cbl:175
    private void InitializeVbrcRec1()
    {
        _vb1AcctId = 0;
        _vb1AcctActiveStatus = " ";
    }

    // =================================================================================================
    // Record serializers (fixed/variable-width byte images)
    // =================================================================================================

    // OUT-ACCT-REC -> 107 bytes. // source: CBACT01C.cbl:56-69
    private byte[] BuildOutAcctRec()
    {
        var w = new RecordWriter(_host);
        w.Zoned(_outAcctId, 11, 0, signed: false);                 // OUT-ACCT-ID 9(11)
        w.Alpha(_outAcctActiveStatus, 1);                          // OUT-ACCT-ACTIVE-STATUS X(1)
        w.Zoned(_outAcctCurrBal, MoneyDigits, MoneyScale, true);   // OUT-ACCT-CURR-BAL
        w.Zoned(_outAcctCreditLimit, MoneyDigits, MoneyScale, true); // OUT-ACCT-CREDIT-LIMIT
        w.Zoned(_outAcctCashCreditLimit, MoneyDigits, MoneyScale, true); // OUT-ACCT-CASH-CREDIT-LIMIT
        w.Alpha(_outAcctOpenDate, 10);                             // OUT-ACCT-OPEN-DATE X(10)
        w.Alpha(_outAcctExpiraionDate, 10);                        // OUT-ACCT-EXPIRAION-DATE X(10)
        w.Alpha(_outAcctReissueDate, 10);                          // OUT-ACCT-REISSUE-DATE X(10)
        w.Zoned(_outAcctCurrCycCredit, MoneyDigits, MoneyScale, true); // OUT-ACCT-CURR-CYC-CREDIT
        w.Comp3(_outAcctCurrCycDebit, MoneyDigits, MoneyScale, true);  // OUT-ACCT-CURR-CYC-DEBIT COMP-3 (7 bytes)
        w.Alpha(_outAcctGroupId, 10);                             // OUT-ACCT-GROUP-ID X(10)
        return w.ToArray(107);
    }

    // ARR-ARRAY-REC -> 110 bytes. // source: CBACT01C.cbl:71-78
    private byte[] BuildArrArrayRec()
    {
        var w = new RecordWriter(_host);
        w.Zoned(_arrAcctId, 11, 0, signed: false);                // ARR-ACCT-ID 9(11)
        for (int i = 0; i < 5; i++)                                // ARR-ACCT-BAL OCCURS 5 TIMES
        {
            w.Zoned(_arrAcctCurrBal[i], MoneyDigits, MoneyScale, true);    // ARR-ACCT-CURR-BAL (zoned 12)
            w.Comp3(_arrAcctCurrCycDebit[i], MoneyDigits, MoneyScale, true); // ARR-ACCT-CURR-CYC-DEBIT COMP-3 (7)
        }
        w.Alpha(_arrFiller, 4);                                    // ARR-FILLER X(4)
        return w.ToArray(110);
    }

    // VBRC-REC1 data bytes (12): VB1-ACCT-ID 9(11) zoned + VB1-ACCT-ACTIVE-STATUS X(1). // source: CBACT01C.cbl:123-125
    private byte[] BuildVbrcRec1Bytes()
    {
        var w = new RecordWriter(_host);
        w.Zoned(_vb1AcctId, 11, 0, signed: false);
        w.Alpha(_vb1AcctActiveStatus, 1);
        return w.ToArray(12);
    }

    // VBRC-REC2 data bytes (39): id 11 zoned + bal 12 zoned + limit 12 zoned + reissue-yyyy X(4). // source: CBACT01C.cbl:126-130
    private byte[] BuildVbrcRec2Bytes()
    {
        var w = new RecordWriter(_host);
        w.Zoned(_vb2AcctId, 11, 0, signed: false);
        w.Zoned(_vb2AcctCurrBal, MoneyDigits, MoneyScale, true);
        w.Zoned(_vb2AcctCreditLimit, MoneyDigits, MoneyScale, true);
        w.Alpha(_vb2AcctReissueYyyy, 4);
        return w.ToArray(39);
    }

    // DISPLAY renderings of VBRC-REC1 / VBRC-REC2 (host-decoded text of the data bytes).
    private string BuildVbrcRec1Text() => HostEncoding.For(_host).GetString(BuildVbrcRec1Bytes());
    private string BuildVbrcRec2Text() => HostEncoding.For(_host).GetString(BuildVbrcRec2Bytes());

    // Writes one RECFM=VB record: 4-byte RDW (length = data+4, big-endian) + exactly WS-RECD-LEN data
    // bytes. Only WS-RECD-LEN bytes reach the file (the VBR-REC stale tail never does — bug #6).
    private void WriteVbRecord(byte[] data, int recdLen)
    {
        int total = recdLen + 4;
        var rdw = new byte[4];
        rdw[0] = (byte)((total >> 8) & 0xFF);
        rdw[1] = (byte)(total & 0xFF);
        rdw[2] = 0;
        rdw[3] = 0;
        _vbrcFile!.Write(rdw);
        _vbrcFile.Write(data, 0, recdLen);
    }

    // =================================================================================================
    // DISPLAY ACCOUNT-RECORD (line 151): the whole 300-byte record image rendered as host text.
    // =================================================================================================
    private string DisplayAccountRecord()
    {
        Account a = _acct!;
        var w = new RecordWriter(_host);
        w.Zoned(a.AcctId, 11, 0, signed: false);
        w.Alpha(a.ActiveStatus, 1);
        w.Zoned(a.CurrBal, MoneyDigits, MoneyScale, true);
        w.Zoned(a.CreditLimit, MoneyDigits, MoneyScale, true);
        w.Zoned(a.CashCreditLimit, MoneyDigits, MoneyScale, true);
        w.Alpha(a.OpenDate, 10);
        w.Alpha(a.ExpirationDate, 10);
        w.Alpha(a.ReissueDate, 10);
        w.Zoned(a.CurrCycCredit, MoneyDigits, MoneyScale, true);
        w.Zoned(a.CurrCycDebit, MoneyDigits, MoneyScale, true);
        w.Alpha(a.AddrZip, 10);
        w.Alpha(a.GroupId, 10);
        w.Alpha("", 178); // FILLER X(178)
        return HostEncoding.For(_host).GetString(w.ToArray(300));
    }

    // =================================================================================================
    // Small helpers
    // =================================================================================================

    // Render a numeric value as COBOL DISPLAY would (signed-zoned bytes -> host text, sign overpunch on
    // the last digit). Used only for SYSOUT (1100-DISPLAY-ACCT-RECORD).
    private string Zoned(decimal value, int totalDigits, int scale, bool signed)
    {
        var field = new byte[totalDigits];
        ZonedDecimalCodec.Encode(value, field, totalDigits, scale, signed, _host);
        return HostEncoding.For(_host).GetString(field);
    }

    private string Zoned(long value, int totalDigits, int scale, bool signed)
        => Zoned((decimal)value, totalDigits, scale, signed);

    // COBOL alphanumeric MOVE: left-justify, space-pad / right-truncate to width.
    private static string Fixed(string text, int width)
    {
        text ??= "";
        return text.Length >= width ? text[..width] : text.PadRight(width, ' ');
    }

    // ---- CODATECN-REC (80-byte) byte helpers --------------------------------------------------------
    private byte[] BlankCodatecnRec()
    {
        var rec = new byte[80];
        byte space = HostEncoding.For(_host).GetBytes(" ")[0];
        Array.Fill(rec, space);
        return rec;
    }

    private void PutText(byte[] rec, int offset, string text, int width)
    {
        byte[] bytes = HostEncoding.For(_host).GetBytes(Fixed(text, width));
        Array.Copy(bytes, 0, rec, offset, width);
    }

    private string GetText(byte[] rec, int offset, int width)
        => HostEncoding.For(_host).GetString(rec, offset, width);

    /// <summary>Builds a fixed-width record image, packing zoned / COMP-3 / alphanumeric fields.</summary>
    private sealed class RecordWriter(HostKind host)
    {
        private readonly List<byte> _bytes = [];
        private readonly HostKind _host = host;

        public void Zoned(decimal value, int totalDigits, int scale, bool signed)
        {
            var field = new byte[totalDigits];
            ZonedDecimalCodec.Encode(value, field, totalDigits, scale, signed, _host);
            _bytes.AddRange(field);
        }

        public void Zoned(long value, int totalDigits, int scale, bool signed)
            => Zoned((decimal)value, totalDigits, scale, signed);

        public void Comp3(decimal value, int totalDigits, int scale, bool signed)
        {
            var field = new byte[PackedDecimalCodec.ByteLength(totalDigits)];
            PackedDecimalCodec.Encode(value, field, totalDigits, scale, signed);
            _bytes.AddRange(field);
        }

        public void Alpha(string text, int width)
        {
            string padded = text.Length >= width ? text[..width] : text.PadRight(width, ' ');
            _bytes.AddRange(HostEncoding.For(_host).GetBytes(padded));
        }

        public byte[] ToArray(int expectedLength)
        {
            if (_bytes.Count != expectedLength)
                throw new InvalidOperationException(
                    $"Record length {_bytes.Count} != expected {expectedLength}.");
            return _bytes.ToArray();
        }
    }

    // OPEN OUTPUT (DISP=NEW after the JCL PREDEL step): create fresh, truncating any existing file.
    private static FileStream OpenOutput(string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        return new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
    }
}
