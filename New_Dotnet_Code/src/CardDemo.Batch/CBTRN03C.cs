using CardDemo.Cobol.Runtime;
using CardDemo.Data;
using CardDemo.Domain;

namespace CardDemo.Batch;

/// <summary>
/// Faithful relational re-port of the batch program <c>CBTRN03C</c> ("Print the transaction detail
/// report."). It reads posted transactions sequentially in the upstream card-sorted order, filters each
/// record by the <c>DATEPARM</c> start/end date window (against the first 10 chars of <c>TRAN-PROC-TS</c>),
/// looks up the card cross-reference (for the account id), the transaction-type description and the
/// transaction-category description, and writes a paginated 133-column report (<c>TRANREPT</c>, LRECL 133)
/// with page totals (every <c>WS-PAGE-SIZE</c>=20 report lines), per-card account totals (on a card control
/// break) and a final grand total. Each transaction amount is accumulated into the page/account totals.
/// </summary>
/// <remarks>
/// <para>Ported paragraph-by-paragraph from <c>app/cbl/CBTRN03C.cbl</c> and the report layout
/// <c>cpy/CVTRA07Y.cpy</c>: each PROCEDURE-DIVISION paragraph is a method whose name mirrors the COBOL
/// paragraph, keeps the original statement order / PERFORM flow, and carries <c>// source: CBTRN03C.cbl:NNN</c>
/// citations. Per <c>_design/ARCHITECTURE.md</c> the VSAM/QSAM master files are now relational tables read
/// through repositories: the sequential <c>TRANFILE</c> read is a forward cursor over
/// <see cref="TransactionRepository"/> (TRANSACTION) fed <b>in card-number order</b> to make the control
/// break work (mirroring the upstream SORT in TRANREPT.jcl); <c>CARDXREF</c>/<c>TRANTYPE</c>/<c>TRANCATG</c>
/// are random keyed reads via <see cref="CardXrefRepository.ReadByKey"/>,
/// <see cref="TranTypeRepository.ReadByKey"/> and <see cref="TranCategoryRepository.ReadByKey"/> (FileStatus
/// '00'/'23' = found / INVALID KEY -> abend). The <c>DATEPARM</c> 1-record parameter dataset is supplied as
/// a start/end date pair to <see cref="Run(RelationalDb,string,string?,string?,HostKind)"/>. The
/// <c>TRANREPT</c> report is a flat fixed-width 133-byte dataset written via <see cref="FixedFileWriter"/>.</para>
/// <para>Money math uses <see cref="decimal"/> with COBOL truncate-toward-zero / silent-overflow semantics
/// (<see cref="Decimals.Store"/> into S9(9)V99); the edited amount fields use <see cref="CobolEditedNumeric"/>
/// (<c>-ZZZ,ZZZ,ZZZ.ZZ</c> for detail, <c>+ZZZ,ZZZ,ZZZ.ZZ</c> for totals).</para>
/// <para>FAITHFUL BUGS reproduced verbatim (see <c>_design/specs/CBTRN03C.md</c> §7):
/// <list type="number">
/// <item><b>Inverted date filter with <c>NEXT SENTENCE</c>:</b> an out-of-range record skips the rest of the
/// loop body (no DISPLAY / lookups / detail / accumulation) and the loop simply iterates. Reproduced as a
/// <c>continue</c> that skips processing of that record.</item>
/// <item><b>Stale last-amount double-add:</b> at EOF the program adds the STALE last transaction's
/// <c>TRAN-AMT</c> into the page &amp; account totals a SECOND time (the last real transaction's amount is
/// added twice), inflating the final page total and grand total. NOT guarded — reproduced exactly.</item>
/// <item><b>Final card's ACCOUNT total never written:</b> <c>1120-WRITE-ACCOUNT-TOTALS</c> fires only on a
/// card control break; at EOF only the page total + grand total are written, so the last card's account
/// total is silently discarded. No final flush added.</item>
/// <item><b><c>WS-LINE-COUNTER</c> counts EVERY report line</b> (headers +4, page-total +2, account-total
/// +2, detail +1, grand-total +0), so the MOD-20 page break is a mix of line types, not a clean 20-detail
/// page. Increments reproduced exactly.</item>
/// <item><b><c>1110-WRITE-GRAND-TOTALS</c> does not increment <c>WS-LINE-COUNTER</c></b> (the only write
/// paragraph that doesn't). Reproduced as-is.</item>
/// <item><b>Page-total written ahead of the re-printed header</b> on each page break (order
/// <c>1110-WRITE-PAGE-TOTALS</c> then <c>1120-WRITE-HEADERS</c>). Reproduced.</item>
/// <item><b>Redundant inner <c>IF END-OF-FILE = 'N'</c> guard</b> duplicates the <c>PERFORM UNTIL</c>
/// condition. Reproduced as-is.</item>
/// <item><b><c>9910-DISPLAY-IO-STATUS</c> big-endian halfword rendering</b> of the 2nd status byte on the
/// non-numeric / <c>IO-STAT1='9'</c> branch. Reproduced big-endian.</item>
/// <item><b><c>MOVE 23 TO IO-STATUS</c> on INVALID-KEY paths</b> places the 2 chars <c>'23'</c> in the
/// 2-byte status, rendered as <c>'0023'</c>. Reproduced.</item>
/// </list></para>
/// </remarks>
public sealed class Cbtrn03c
{
    // Edited PICs for the report amount fields (CVTRA07Y).
    private const string DetailAmtPic = "-ZZZ,ZZZ,ZZZ.ZZ"; // TRAN-REPORT-AMT  // source: cpy/CVTRA07Y.cpy:30
    private const string TotalAmtPic = "+ZZZ,ZZZ,ZZZ.ZZ";  // REPT-*-TOTAL     // source: cpy/CVTRA07Y.cpy:54,60,66

    // Money field shape for the three S9(9)V99 totals (truncate-toward-zero / silent overflow).
    private const int MoneyIntDigits = 9;
    private const int MoneyScale = 2;

    private const int ReportRecLen = 133; // FD-REPTFILE-REC PIC X(133)  // source: CBTRN03C.cbl:85

    // PIC widths for DISPLAY TRAN-RECORD (CVTRA05Y, 350 bytes).
    private const int CardNumWidth = 16; // TRAN-CARD-NUM / FD-XREF-CARD-NUM PIC X(16)

    // --- Repositories (relational replacements for the four input master/transaction files) -----------
    private TransactionRepository _transact = null!; // TRANSACT-FILE (DD TRANFILE)  -> TRANSACTION (card-sorted cursor)
    private CardXrefRepository _xref = null!;          // XREF-FILE     (DD CARDXREF) -> CARD_XREF (random keyed)
    private TranTypeRepository _trantype = null!;      // TRANTYPE-FILE (DD TRANTYPE) -> TRAN_TYPE (random keyed)
    private TranCategoryRepository _trancatg = null!;  // TRANCATG-FILE (DD TRANCATG) -> TRAN_CATEGORY (random keyed)

    // The card-sorted forward cursor over TRANSACTION (mirrors the upstream SORT by TRAN-CARD-NUM).
    private IEnumerator<Transaction>? _transactCursor;

    // --- File-status fields (CBTRN03C lines 94-120) --------------------------------------------------
    private string _tranfileStatus = "00"; // TRANFILE-STATUS   // source: CBTRN03C.cbl:94-96
    private string _cardxrefStatus = "00"; // CARDXREF-STATUS   // source: CBTRN03C.cbl:99-101
    private string _trantypeStatus = "00"; // TRANTYPE-STATUS   // source: CBTRN03C.cbl:104-106
    private string _trancatgStatus = "00"; // TRANCATG-STATUS   // source: CBTRN03C.cbl:109-111
    private string _treptStatus = "00";    // TRANREPT-STATUS   // source: CBTRN03C.cbl:114-116
    private string _dateparmStatus = "00"; // DATEPARM-STATUS   // source: CBTRN03C.cbl:118-120

    /// <summary>IO-STATUS — working copy of a file status used by 9910-DISPLAY-IO-STATUS. // source: CBTRN03C.cbl:139-141</summary>
    private string _ioStatus = "00";

    // --- WS-DATEPARM-RECORD (lines 122-125) ----------------------------------------------------------
    private string _wsStartDate = new(' ', 10); // WS-START-DATE PIC X(10)  // source: CBTRN03C.cbl:123
    private string _wsEndDate = new(' ', 10);   // WS-END-DATE   PIC X(10)  // source: CBTRN03C.cbl:125

    // --- WS-REPORT-VARS (lines 127-137) --------------------------------------------------------------
    private bool _firstTime = true;            // WS-FIRST-TIME   PIC X VALUE 'Y'  // source: CBTRN03C.cbl:128
    private long _lineCounter;                  // WS-LINE-COUNTER PIC 9(09) COMP-3 VALUE 0  // source: CBTRN03C.cbl:129-130
    private const long PageSize = 20;          // WS-PAGE-SIZE    PIC 9(03) COMP-3 VALUE 20  // source: CBTRN03C.cbl:131-132
    private decimal _pageTotal;                 // WS-PAGE-TOTAL    PIC S9(09)V99 VALUE 0  // source: CBTRN03C.cbl:134
    private decimal _accountTotal;              // WS-ACCOUNT-TOTAL PIC S9(09)V99 VALUE 0  // source: CBTRN03C.cbl:135
    private decimal _grandTotal;               // WS-GRAND-TOTAL   PIC S9(09)V99 VALUE 0  // source: CBTRN03C.cbl:136
    private string _currCardNum = new(' ', 16); // WS-CURR-CARD-NUM PIC X(16) VALUE SPACES  // source: CBTRN03C.cbl:137

    /// <summary>APPL-RESULT PIC S9(9) COMP. 88 APPL-AOK=0, APPL-EOF=16. // source: CBTRN03C.cbl:150-152</summary>
    private int _applResult;

    /// <summary>END-OF-FILE PIC X(01) VALUE 'N' — loop sentinel (true == 'Y'). // source: CBTRN03C.cbl:154</summary>
    private bool _endOfFile;

    // --- Record areas the READ ... INTO statements populate ------------------------------------------

    /// <summary>
    /// TRAN-RECORD (CVTRA05Y, 350 bytes) — last record read; left STALE on EOF (Faithful Bug #2). Starts
    /// as a spaces/zeros working-storage record so the EOF path can reference TRAN-AMT even on an empty file.
    /// // source: CBTRN03C.cbl:93, 249
    /// </summary>
    private Transaction _tranRecord = new()
    {
        TranId = new string(' ', 16),
        TypeCd = new string(' ', 2),
        Source = new string(' ', 10),
        Desc = new string(' ', 100),
        MerchantName = new string(' ', 50),
        MerchantCity = new string(' ', 50),
        MerchantZip = new string(' ', 10),
        CardNum = new string(' ', 16),
        OrigTs = new string(' ', 26),
        ProcTs = new string(' ', 26),
    };

    /// <summary>CARD-XREF-RECORD (CVACT03Y) — populated by the XREF keyed read; supplies XREF-ACCT-ID. // source: CBTRN03C.cbl:98, 485</summary>
    private CardXref? _cardXrefRecord;

    /// <summary>TRAN-TYPE-RECORD (CVTRA03Y) — populated by the TRANTYPE keyed read; supplies TRAN-TYPE-DESC. // source: CBTRN03C.cbl:103, 495</summary>
    private TranType? _tranTypeRecord;

    /// <summary>TRAN-CAT-RECORD (CVTRA04Y) — populated by the TRANCATG keyed read; supplies TRAN-CAT-TYPE-DESC. // source: CBTRN03C.cbl:108, 505</summary>
    private TranCategory? _tranCatRecord;

    // --- Working-storage key fields (set in MAIN, consumed by the lookups) ---------------------------
    private string _fdXrefCardNum = new(' ', CardNumWidth); // FD-XREF-CARD-NUM PIC X(16)  // source: CBTRN03C.cbl:69, 186
    private string _fdTranType = new(' ', 2);               // FD-TRAN-TYPE     PIC X(02)  // source: CBTRN03C.cbl:74, 189
    private string _fdTranTypeCd = new(' ', 2);             // FD-TRAN-TYPE-CD  PIC X(02)  // source: CBTRN03C.cbl:80, 191
    private int _fdTranCatCd;                               // FD-TRAN-CAT-CD   PIC 9(04)  // source: CBTRN03C.cbl:81, 193

    private readonly HostKind _host;
    private FixedFileWriter? _writer;
    private readonly List<string> _sysout = [];

    /// <summary>88 APPL-AOK VALUE 0. // source: CBTRN03C.cbl:151</summary>
    private bool ApplAok => _applResult == 0;

    /// <summary>88 APPL-EOF VALUE 16. // source: CBTRN03C.cbl:152</summary>
    private bool ApplEof => _applResult == 16;

    private Cbtrn03c(HostKind host) => _host = host;

    /// <summary>The SYSOUT (DISPLAY) lines produced by the run, in order.</summary>
    public IReadOnlyList<string> Sysout => _sysout;

    /// <summary>
    /// Runs CBTRN03C over the relational <paramref name="db"/>, writing the 133-byte report to
    /// <paramref name="reportPath"/>. The <c>DATEPARM</c> window is supplied as
    /// <paramref name="startDate"/>/<paramref name="endDate"/> (each <c>CCYY-MM-DD</c>); when
    /// <paramref name="startDate"/> is <c>null</c> the (empty) DATEPARM file is modelled as EOF and no
    /// transactions are processed. Returns the SYSOUT lines in order.
    /// </summary>
    /// <param name="db">The relational database (TRANSACTION / CARD_XREF / TRAN_TYPE / TRAN_CATEGORY).</param>
    /// <param name="reportPath">TRANREPT output dataset path (flat, fixed-width 133-byte lines).</param>
    /// <param name="startDate">DATEPARM WS-START-DATE (10 chars); <c>null</c> = empty DATEPARM (EOF).</param>
    /// <param name="endDate">DATEPARM WS-END-DATE (10 chars).</param>
    /// <param name="host">Host encoding for the report dataset (defaults to ASCII).</param>
    public static IReadOnlyList<string> Run(
        RelationalDb db,
        string reportPath,
        string? startDate,
        string? endDate = null,
        HostKind host = HostKind.Ascii)
    {
        var program = new Cbtrn03c(host);
        program._transact = new TransactionRepository(db);
        program._xref = new CardXrefRepository(db);
        program._trantype = new TranTypeRepository(db);
        program._trancatg = new TranCategoryRepository(db);
        program.Execute(reportPath, startDate, endDate);
        return program.Sysout;
    }

    /// <summary>Convenience overload taking a <see cref="BatchSupport"/> (uses its repositories).</summary>
    public static IReadOnlyList<string> Run(
        BatchSupport support,
        string reportPath,
        string? startDate,
        string? endDate = null,
        HostKind host = HostKind.Ascii)
    {
        var program = new Cbtrn03c(host);
        program._transact = support.Transaction;
        program._xref = support.CardXref;
        program._trantype = support.TranType;
        program._trancatg = support.TranCategory;
        program.Execute(reportPath, startDate, endDate);
        return program.Sysout;
    }

    // =================================================================================================
    // MAIN (unnamed PROCEDURE DIVISION body) // source: CBTRN03C.cbl:159-217
    // =================================================================================================
    private void Execute(string reportPath, string? startDate, string? endDate)
    {
        // OPEN OUTPUT REPORT-FILE -> create the report dataset fresh (DISP=NEW; delete an existing file).
        if (File.Exists(reportPath)) File.Delete(reportPath);
        _writer = BatchSupport.OpenWriter(reportPath, _host);
        try
        {
            Display("START OF EXECUTION OF PROGRAM CBTRN03C"); // source: CBTRN03C.cbl:160

            TranfileOpen0000();   // source: CBTRN03C.cbl:161
            ReptfileOpen0100();   // source: CBTRN03C.cbl:162
            CardxrefOpen0200();   // source: CBTRN03C.cbl:163
            TrantypeOpen0300();   // source: CBTRN03C.cbl:164
            TrancatgOpen0400();   // source: CBTRN03C.cbl:165
            DateparmOpen0500();   // source: CBTRN03C.cbl:166

            DateparmRead0550(startDate, endDate); // source: CBTRN03C.cbl:168

            // PERFORM UNTIL END-OF-FILE = 'Y'  // source: CBTRN03C.cbl:170-206
            while (!_endOfFile)
            {
                // IF END-OF-FILE = 'N' (redundant inner guard vs the UNTIL — bug #7)  // source: CBTRN03C.cbl:171
                if (!_endOfFile)
                {
                    TranfileGetNext1000(); // source: CBTRN03C.cbl:172

                    // Date filter (lines 173-178): IF in-range CONTINUE ELSE NEXT SENTENCE.
                    // FAITHFUL BUG #1: NEXT SENTENCE jumps past the period ending the whole PERFORM ...
                    // END-PERFORM sentence, so an out-of-range record skips the rest of the loop body and
                    // the loop iterates. (CONTINUE = fall through to process the in-range record.)
                    string procDate = Substr(_tranRecord.ProcTs, 0, 10); // TRAN-PROC-TS (1:10)  // source: CBTRN03C.cbl:173-174
                    bool inRange =
                        string.CompareOrdinal(procDate, _wsStartDate) >= 0 &&
                        string.CompareOrdinal(procDate, _wsEndDate) <= 0;
                    if (!inRange)
                        continue; // NEXT SENTENCE -> skip this (out-of-range) record, iterate the loop  // source: CBTRN03C.cbl:177,206

                    // IF END-OF-FILE = 'N' (a record was actually read, not EOF)  // source: CBTRN03C.cbl:179
                    if (!_endOfFile)
                    {
                        Display(DisplayTranRecord()); // DISPLAY TRAN-RECORD  // source: CBTRN03C.cbl:180

                        // Card control break: IF WS-CURR-CARD-NUM NOT= TRAN-CARD-NUM  // source: CBTRN03C.cbl:181
                        if (!string.Equals(_currCardNum, Alpha(_tranRecord.CardNum, CardNumWidth), StringComparison.Ordinal))
                        {
                            // IF WS-FIRST-TIME = 'N' -> flush prior card's account total  // source: CBTRN03C.cbl:182-184
                            if (!_firstTime)
                                WriteAccountTotals1120();

                            _currCardNum = Alpha(_tranRecord.CardNum, CardNumWidth); // MOVE TRAN-CARD-NUM TO WS-CURR-CARD-NUM  // source: CBTRN03C.cbl:185
                            _fdXrefCardNum = Alpha(_tranRecord.CardNum, CardNumWidth); // MOVE TRAN-CARD-NUM TO FD-XREF-CARD-NUM  // source: CBTRN03C.cbl:186
                            LookupXref1500A();                                       // source: CBTRN03C.cbl:187
                        }

                        _fdTranType = Alpha(_tranRecord.TypeCd, 2); // MOVE TRAN-TYPE-CD OF TRAN-RECORD TO FD-TRAN-TYPE  // source: CBTRN03C.cbl:189
                        LookupTrantype1500B();                       // source: CBTRN03C.cbl:190

                        _fdTranTypeCd = Alpha(_tranRecord.TypeCd, 2); // MOVE TRAN-TYPE-CD OF TRAN-RECORD TO FD-TRAN-TYPE-CD  // source: CBTRN03C.cbl:191-192
                        _fdTranCatCd = _tranRecord.CatCd;             // MOVE TRAN-CAT-CD OF TRAN-RECORD TO FD-TRAN-CAT-CD  // source: CBTRN03C.cbl:193-194
                        LookupTrancatg1500C();                        // source: CBTRN03C.cbl:195

                        WriteTransactionReport1100(); // source: CBTRN03C.cbl:196
                    }
                    else // ELSE (END-OF-FILE = 'Y' — this iteration hit EOF)  // source: CBTRN03C.cbl:197
                    {
                        Display("TRAN-AMT " + DisplayAmt(_tranRecord.Amt));     // source: CBTRN03C.cbl:198
                        Display("WS-PAGE-TOTAL" + DisplayTotal(_pageTotal));    // source: CBTRN03C.cbl:199

                        // FAITHFUL BUG #2: ADD the STALE last record's TRAN-AMT into the page & account
                        // totals (a second time) — inflates the final page total and grand total.
                        AddTranAmt(_tranRecord.Amt); // ADD TRAN-AMT TO WS-PAGE-TOTAL WS-ACCOUNT-TOTAL  // source: CBTRN03C.cbl:200-201

                        WritePageTotals1110();  // source: CBTRN03C.cbl:202
                        WriteGrandTotals1110(); // source: CBTRN03C.cbl:203
                        // FAITHFUL BUG #3: 1120-WRITE-ACCOUNT-TOTALS is NOT called here, so the final card's
                        // account total is never written.
                    }
                }
            }

            TranfileClose9000();  // source: CBTRN03C.cbl:208
            ReptfileClose9100();  // source: CBTRN03C.cbl:209
            CardxrefClose9200();  // source: CBTRN03C.cbl:210
            TrantypeClose9300();  // source: CBTRN03C.cbl:211
            TrancatgClose9400();  // source: CBTRN03C.cbl:212
            DateparmClose9500();  // source: CBTRN03C.cbl:213

            Display("END OF EXECUTION OF PROGRAM CBTRN03C"); // source: CBTRN03C.cbl:215
            // GOBACK  // source: CBTRN03C.cbl:217
        }
        finally
        {
            _writer?.Flush();
            _writer?.Dispose();
        }
    }

    // *---------------------------------------------------------------*
    /// <summary>
    /// 0550-DATEPARM-READ — READ DATE-PARMS-FILE INTO WS-DATEPARM-RECORD. The DATEPARM dataset is modelled
    /// as the supplied start/end date pair: a non-null start date is status '00' (proceed); a null start
    /// date is an empty file (status '10' -> APPL-EOF -> END-OF-FILE='Y'). // source: CBTRN03C.cbl:220-243
    /// </summary>
    private void DateparmRead0550(string? startDate, string? endDate)
    {
        // READ DATE-PARMS-FILE INTO WS-DATEPARM-RECORD  // source: CBTRN03C.cbl:221
        if (startDate is not null)
        {
            _dateparmStatus = FileStatus.Ok;
            _wsStartDate = Alpha(startDate, 10);          // WS-START-DATE PIC X(10)
            _wsEndDate = Alpha(endDate ?? "", 10);        // WS-END-DATE   PIC X(10)
        }
        else
        {
            _dateparmStatus = FileStatus.EndOfFile;       // empty DATEPARM -> '10'
        }

        // EVALUATE DATEPARM-STATUS: '00'->0; '10'->16; OTHER->12  // source: CBTRN03C.cbl:222-229
        if (_dateparmStatus == FileStatus.Ok)
            _applResult = 0;
        else if (_dateparmStatus == FileStatus.EndOfFile)
            _applResult = 16;
        else
            _applResult = 12;

        if (ApplAok)                                      // IF APPL-AOK  // source: CBTRN03C.cbl:231
        {
            // DISPLAY 'Reporting from ' WS-START-DATE ' to ' WS-END-DATE  // source: CBTRN03C.cbl:232-233
            Display("Reporting from " + _wsStartDate + " to " + _wsEndDate);
        }
        else if (ApplEof)                                 // IF APPL-EOF  // source: CBTRN03C.cbl:235
        {
            _endOfFile = true;                            // MOVE 'Y' TO END-OF-FILE  // source: CBTRN03C.cbl:236
        }
        else
        {
            Display("ERROR READING DATEPARM FILE");       // source: CBTRN03C.cbl:238
            _ioStatus = _dateparmStatus;                  // MOVE DATEPARM-STATUS TO IO-STATUS  // source: CBTRN03C.cbl:239
            DisplayIoStatus9910();                        // source: CBTRN03C.cbl:240
            AbendProgram9999();                           // source: CBTRN03C.cbl:241
        }
        // (no EXIT; paragraph ends at the period on line 243)
    }

    // *****************************************************************
    // * I/O ROUTINES TO ACCESS A KSDS, VSAM DATA SET...               *
    // *****************************************************************
    /// <summary>
    /// 1000-TRANFILE-GET-NEXT — sequential READ of the next transaction row (card-sorted cursor). On EOF the
    /// TRAN-RECORD is left unchanged (READ ... INTO is not done on status '10'). // source: CBTRN03C.cbl:248-272
    /// </summary>
    private void TranfileGetNext1000()
    {
        // READ TRANSACT-FILE INTO TRAN-RECORD  // source: CBTRN03C.cbl:249
        _tranfileStatus = ReadNextCardSorted(out Transaction? next);
        if (next is not null)
            _tranRecord = next; // INTO TRAN-RECORD — only populated on a successful read (status '00').

        // EVALUATE TRANFILE-STATUS: '00'->0; '10'->16; OTHER->12  // source: CBTRN03C.cbl:251-258
        if (_tranfileStatus == FileStatus.Ok)
            _applResult = 0;
        else if (_tranfileStatus == FileStatus.EndOfFile)
            _applResult = 16;
        else
            _applResult = 12;

        if (ApplAok)                                      // IF APPL-AOK  // source: CBTRN03C.cbl:260
        {
            // CONTINUE  // source: CBTRN03C.cbl:261
        }
        else if (ApplEof)                                 // IF APPL-EOF  // source: CBTRN03C.cbl:263
        {
            _endOfFile = true;                            // MOVE 'Y' TO END-OF-FILE  // source: CBTRN03C.cbl:264
        }
        else
        {
            Display("ERROR READING TRANSACTION FILE");    // source: CBTRN03C.cbl:266
            _ioStatus = _tranfileStatus;                  // MOVE TRANFILE-STATUS TO IO-STATUS  // source: CBTRN03C.cbl:267
            DisplayIoStatus9910();                        // source: CBTRN03C.cbl:268
            AbendProgram9999();                           // source: CBTRN03C.cbl:269
        }
        // EXIT  // source: CBTRN03C.cbl:272
    }

    // *---------------------------------------------------------------*
    /// <summary>
    /// 1100-WRITE-TRANSACTION-REPORT — one-time header on first detail, MOD-20 page break, accumulate this
    /// in-range record's amount, then write the detail line. // source: CBTRN03C.cbl:274-290
    /// </summary>
    private void WriteTransactionReport1100()
    {
        // IF WS-FIRST-TIME = 'Y'  // source: CBTRN03C.cbl:275
        if (_firstTime)
        {
            _firstTime = false;                 // MOVE 'N' TO WS-FIRST-TIME  // source: CBTRN03C.cbl:276
            // MOVE WS-START-DATE TO REPT-START-DATE; MOVE WS-END-DATE TO REPT-END-DATE  // source: CBTRN03C.cbl:277-278
            // (carried directly into the REPORT-NAME-HEADER template; see BuildReportNameHeader.)
            WriteHeaders1120();                 // source: CBTRN03C.cbl:279
        }

        // IF FUNCTION MOD(WS-LINE-COUNTER, WS-PAGE-SIZE) = 0  // source: CBTRN03C.cbl:282
        if (_lineCounter % PageSize == 0)
        {
            // FAITHFUL BUG #6: page-total then header (order matters).
            WritePageTotals1110();              // source: CBTRN03C.cbl:283
            WriteHeaders1120();                 // source: CBTRN03C.cbl:284
        }

        AddTranAmt(_tranRecord.Amt);            // ADD TRAN-AMT TO WS-PAGE-TOTAL WS-ACCOUNT-TOTAL  // source: CBTRN03C.cbl:287-288
        WriteDetail1120();                      // source: CBTRN03C.cbl:289
        // EXIT  // source: CBTRN03C.cbl:290
    }

    // *---------------------------------------------------------------*
    /// <summary>
    /// 1110-WRITE-PAGE-TOTALS — write the page-total line, roll the page total into the grand total, reset
    /// the page total, then write a HEADER-2 rule line. (+2 to WS-LINE-COUNTER.) // source: CBTRN03C.cbl:293-304
    /// </summary>
    private void WritePageTotals1110()
    {
        // MOVE WS-PAGE-TOTAL TO REPT-PAGE-TOTAL; MOVE REPORT-PAGE-TOTALS TO FD-REPTFILE-REC  // source: CBTRN03C.cbl:294-295
        WriteReportRec1111(BuildReportPageTotals(_pageTotal)); // source: CBTRN03C.cbl:296
        _grandTotal = AddMoney(_grandTotal, _pageTotal);       // ADD WS-PAGE-TOTAL TO WS-GRAND-TOTAL  // source: CBTRN03C.cbl:297
        _pageTotal = 0m;                                       // MOVE 0 TO WS-PAGE-TOTAL  // source: CBTRN03C.cbl:298
        _lineCounter += 1;                                     // ADD 1 TO WS-LINE-COUNTER  // source: CBTRN03C.cbl:299
        WriteReportRec1111(BuildTransactionHeader2());         // MOVE TRANSACTION-HEADER-2 ...  // source: CBTRN03C.cbl:300-301
        _lineCounter += 1;                                     // ADD 1 TO WS-LINE-COUNTER  // source: CBTRN03C.cbl:302
        // EXIT  // source: CBTRN03C.cbl:304
    }

    // *---------------------------------------------------------------*
    /// <summary>
    /// 1120-WRITE-ACCOUNT-TOTALS — write the account-total line, reset the account total, then write a
    /// HEADER-2 rule line. (+2 to WS-LINE-COUNTER.) // source: CBTRN03C.cbl:306-316
    /// </summary>
    private void WriteAccountTotals1120()
    {
        // MOVE WS-ACCOUNT-TOTAL TO REPT-ACCOUNT-TOTAL; MOVE REPORT-ACCOUNT-TOTALS TO FD-REPTFILE-REC  // source: CBTRN03C.cbl:307-308
        WriteReportRec1111(BuildReportAccountTotals(_accountTotal)); // source: CBTRN03C.cbl:309
        _accountTotal = 0m;                                          // MOVE 0 TO WS-ACCOUNT-TOTAL  // source: CBTRN03C.cbl:310
        _lineCounter += 1;                                          // ADD 1 TO WS-LINE-COUNTER  // source: CBTRN03C.cbl:311
        WriteReportRec1111(BuildTransactionHeader2());              // MOVE TRANSACTION-HEADER-2 ...  // source: CBTRN03C.cbl:312-313
        _lineCounter += 1;                                          // ADD 1 TO WS-LINE-COUNTER  // source: CBTRN03C.cbl:314
        // EXIT  // source: CBTRN03C.cbl:316
    }

    // *---------------------------------------------------------------*
    /// <summary>
    /// 1110-WRITE-GRAND-TOTALS — write the grand-total line. FAITHFUL BUG #5: does NOT increment
    /// WS-LINE-COUNTER. // source: CBTRN03C.cbl:318-322
    /// </summary>
    private void WriteGrandTotals1110()
    {
        // MOVE WS-GRAND-TOTAL TO REPT-GRAND-TOTAL; MOVE REPORT-GRAND-TOTALS TO FD-REPTFILE-REC  // source: CBTRN03C.cbl:319-320
        WriteReportRec1111(BuildReportGrandTotals(_grandTotal)); // source: CBTRN03C.cbl:321
        // (no ADD 1 TO WS-LINE-COUNTER — bug #5)
        // EXIT  // source: CBTRN03C.cbl:322
    }

    // *---------------------------------------------------------------*
    /// <summary>
    /// 1120-WRITE-HEADERS — write the 4-line header block (name header, blank line, HEADER-1, HEADER-2).
    /// (+4 to WS-LINE-COUNTER.) // source: CBTRN03C.cbl:324-341
    /// </summary>
    private void WriteHeaders1120()
    {
        WriteReportRec1111(BuildReportNameHeader());      // MOVE REPORT-NAME-HEADER ...  // source: CBTRN03C.cbl:325-326
        _lineCounter += 1;                                // source: CBTRN03C.cbl:327
        WriteReportRec1111(new string(' ', ReportRecLen)); // MOVE WS-BLANK-LINE ...  // source: CBTRN03C.cbl:329-330
        _lineCounter += 1;                                // source: CBTRN03C.cbl:331
        WriteReportRec1111(BuildTransactionHeader1());    // MOVE TRANSACTION-HEADER-1 ...  // source: CBTRN03C.cbl:333-334
        _lineCounter += 1;                                // source: CBTRN03C.cbl:335
        WriteReportRec1111(BuildTransactionHeader2());    // MOVE TRANSACTION-HEADER-2 ...  // source: CBTRN03C.cbl:337-338
        _lineCounter += 1;                                // source: CBTRN03C.cbl:339
        // EXIT (Header block = 4 lines, +4 to WS-LINE-COUNTER.)  // source: CBTRN03C.cbl:341
    }

    // *---------------------------------------------------------------*
    /// <summary>
    /// 1111-WRITE-REPORT-REC — WRITE FD-REPTFILE-REC (133 bytes). Status '00' -> ok, else 12 -> abend.
    /// // source: CBTRN03C.cbl:343-359
    /// </summary>
    private void WriteReportRec1111(string record)
    {
        // WRITE FD-REPTFILE-REC (FD record is X(133); pad/truncate to exactly 133).  // source: CBTRN03C.cbl:345
        _writer!.WriteText(FixedLine(record, ReportRecLen));
        _treptStatus = FileStatus.Ok;

        if (_treptStatus == FileStatus.Ok)               // IF TRANREPT-STATUS = '00'  // source: CBTRN03C.cbl:346
            _applResult = 0;                             // source: CBTRN03C.cbl:347
        else
            _applResult = 12;                            // source: CBTRN03C.cbl:349

        if (ApplAok)                                     // IF APPL-AOK  // source: CBTRN03C.cbl:351
        {
            // CONTINUE  // source: CBTRN03C.cbl:352
        }
        else
        {
            Display("ERROR WRITING REPTFILE");           // source: CBTRN03C.cbl:354
            _ioStatus = _treptStatus;                    // MOVE TRANREPT-STATUS TO IO-STATUS  // source: CBTRN03C.cbl:355
            DisplayIoStatus9910();                       // source: CBTRN03C.cbl:356
            AbendProgram9999();                          // source: CBTRN03C.cbl:357
        }
        // EXIT  // source: CBTRN03C.cbl:359
    }

    // *---------------------------------------------------------------*
    /// <summary>
    /// 1120-WRITE-DETAIL — INITIALIZE the detail line, move the transaction/xref/type/category fields into
    /// it (with the COBOL MOVE truncations), then write it. (+1 to WS-LINE-COUNTER.) // source: CBTRN03C.cbl:361-374
    /// </summary>
    private void WriteDetail1120()
    {
        // INITIALIZE TRANSACTION-DETAIL-REPORT (data items -> spaces/zeros; FILLERs keep their VALUEs).
        // Modeled by building a fresh detail line whose FILLER positions are the constant '-'/' ' separators.
        // source: CBTRN03C.cbl:362
        string transId = Alpha(_tranRecord.TranId, 16);                 // MOVE TRAN-ID  // source: CBTRN03C.cbl:363
        string accountId = Alpha(ZonedDigits(_cardXrefRecord?.AcctId ?? 0, 11), 11); // MOVE XREF-ACCT-ID (9(11)) TO X(11)  // source: CBTRN03C.cbl:364
        string typeCd = Alpha(_tranRecord.TypeCd, 2);                    // MOVE TRAN-TYPE-CD OF TRAN-RECORD  // source: CBTRN03C.cbl:365
        string typeDesc = Alpha(_tranTypeRecord?.TranTypeDesc ?? "", 15); // MOVE TRAN-TYPE-DESC (X50 -> X15, truncated)  // source: CBTRN03C.cbl:366
        int catCd = _tranRecord.CatCd;                                   // MOVE TRAN-CAT-CD OF TRAN-RECORD (9(4))  // source: CBTRN03C.cbl:367
        string catDesc = Alpha(_tranCatRecord?.TranCatTypeDesc ?? "", 29); // MOVE TRAN-CAT-TYPE-DESC (X50 -> X29, truncated)  // source: CBTRN03C.cbl:368
        string source = Alpha(_tranRecord.Source, 10);                   // MOVE TRAN-SOURCE  // source: CBTRN03C.cbl:369
        decimal amt = _tranRecord.Amt;                                   // MOVE TRAN-AMT (-> -ZZZ,ZZZ,ZZZ.ZZ)  // source: CBTRN03C.cbl:370

        // MOVE TRANSACTION-DETAIL-REPORT TO FD-REPTFILE-REC  // source: CBTRN03C.cbl:371
        WriteReportRec1111(BuildTransactionDetailReport(
            transId, accountId, typeCd, typeDesc, catCd, catDesc, source, amt)); // source: CBTRN03C.cbl:372
        _lineCounter += 1;                                               // ADD 1 TO WS-LINE-COUNTER  // source: CBTRN03C.cbl:373
        // EXIT  // source: CBTRN03C.cbl:374
    }

    // *---------------------------------------------------------------*
    /// <summary>0000-TRANFILE-OPEN — OPEN INPUT TRANSACT-FILE -> position the card-sorted forward cursor. // source: CBTRN03C.cbl:376-392</summary>
    private void TranfileOpen0000()
    {
        _applResult = 8;                                  // MOVE 8 TO APPL-RESULT  // source: CBTRN03C.cbl:377
        // OPEN INPUT TRANSACT-FILE -> build the forward cursor over TRANSACTION in card-number order
        // (mirrors the upstream SORT BY TRAN-CARD-NUM in TRANREPT.jcl).  // source: CBTRN03C.cbl:378
        _transactCursor = _transact.ReadAll()
            .OrderBy(t => t.CardNum, StringComparer.Ordinal)
            .GetEnumerator();
        _tranfileStatus = FileStatus.Ok;

        if (_tranfileStatus == FileStatus.Ok) _applResult = 0; else _applResult = 12; // source: CBTRN03C.cbl:379-383
        if (ApplAok) { /* CONTINUE */ }                   // source: CBTRN03C.cbl:384-385
        else
        {
            Display("ERROR OPENING TRANFILE");            // source: CBTRN03C.cbl:387
            _ioStatus = _tranfileStatus;                  // MOVE TRANFILE-STATUS TO IO-STATUS  // source: CBTRN03C.cbl:388
            DisplayIoStatus9910();                        // source: CBTRN03C.cbl:389
            AbendProgram9999();                           // source: CBTRN03C.cbl:390
        }
        // EXIT  // source: CBTRN03C.cbl:392
    }

    // *---------------------------------------------------------------*
    /// <summary>0100-REPTFILE-OPEN — OPEN OUTPUT REPORT-FILE (the report dataset was created in Execute). // source: CBTRN03C.cbl:394-410</summary>
    private void ReptfileOpen0100()
    {
        _applResult = 8;                                  // MOVE 8 TO APPL-RESULT  // source: CBTRN03C.cbl:395
        // OPEN OUTPUT REPORT-FILE -> the FixedFileWriter was already opened in Execute.  // source: CBTRN03C.cbl:396
        _treptStatus = FileStatus.Ok;

        if (_treptStatus == FileStatus.Ok) _applResult = 0; else _applResult = 12; // source: CBTRN03C.cbl:397-401
        if (ApplAok) { /* CONTINUE */ }                   // source: CBTRN03C.cbl:402-403
        else
        {
            Display("ERROR OPENING REPTFILE");            // source: CBTRN03C.cbl:405
            _ioStatus = _treptStatus;                     // MOVE TRANREPT-STATUS TO IO-STATUS  // source: CBTRN03C.cbl:406
            DisplayIoStatus9910();                        // source: CBTRN03C.cbl:407
            AbendProgram9999();                           // source: CBTRN03C.cbl:408
        }
        // EXIT  // source: CBTRN03C.cbl:410
    }

    // *---------------------------------------------------------------*
    /// <summary>0200-CARDXREF-OPEN — OPEN INPUT XREF-FILE (CARD_XREF handle for random keyed reads). // source: CBTRN03C.cbl:412-428</summary>
    private void CardxrefOpen0200()
    {
        _applResult = 8;                                  // MOVE 8 TO APPL-RESULT  // source: CBTRN03C.cbl:413
        _ = _xref;                                        // OPEN INPUT XREF-FILE  // source: CBTRN03C.cbl:414
        _cardxrefStatus = FileStatus.Ok;

        if (_cardxrefStatus == FileStatus.Ok) _applResult = 0; else _applResult = 12; // source: CBTRN03C.cbl:415-419
        if (ApplAok) { /* CONTINUE */ }                   // source: CBTRN03C.cbl:420-421
        else
        {
            Display("ERROR OPENING CROSS REF FILE");      // source: CBTRN03C.cbl:423
            _ioStatus = _cardxrefStatus;                  // MOVE CARDXREF-STATUS TO IO-STATUS  // source: CBTRN03C.cbl:424
            DisplayIoStatus9910();                        // source: CBTRN03C.cbl:425
            AbendProgram9999();                           // source: CBTRN03C.cbl:426
        }
        // EXIT  // source: CBTRN03C.cbl:428
    }

    // *---------------------------------------------------------------*
    /// <summary>0300-TRANTYPE-OPEN — OPEN INPUT TRANTYPE-FILE (TRAN_TYPE handle for random keyed reads). // source: CBTRN03C.cbl:430-446</summary>
    private void TrantypeOpen0300()
    {
        _applResult = 8;                                  // MOVE 8 TO APPL-RESULT  // source: CBTRN03C.cbl:431
        _ = _trantype;                                    // OPEN INPUT TRANTYPE-FILE  // source: CBTRN03C.cbl:432
        _trantypeStatus = FileStatus.Ok;

        if (_trantypeStatus == FileStatus.Ok) _applResult = 0; else _applResult = 12; // source: CBTRN03C.cbl:433-437
        if (ApplAok) { /* CONTINUE */ }                   // source: CBTRN03C.cbl:438-439
        else
        {
            Display("ERROR OPENING TRANSACTION TYPE FILE"); // source: CBTRN03C.cbl:441
            _ioStatus = _trantypeStatus;                    // MOVE TRANTYPE-STATUS TO IO-STATUS  // source: CBTRN03C.cbl:442
            DisplayIoStatus9910();                          // source: CBTRN03C.cbl:443
            AbendProgram9999();                             // source: CBTRN03C.cbl:444
        }
        // EXIT  // source: CBTRN03C.cbl:446
    }

    // *---------------------------------------------------------------*
    /// <summary>0400-TRANCATG-OPEN — OPEN INPUT TRANCATG-FILE (TRAN_CATEGORY handle for random keyed reads). // source: CBTRN03C.cbl:448-464</summary>
    private void TrancatgOpen0400()
    {
        _applResult = 8;                                  // MOVE 8 TO APPL-RESULT  // source: CBTRN03C.cbl:449
        _ = _trancatg;                                    // OPEN INPUT TRANCATG-FILE  // source: CBTRN03C.cbl:450
        _trancatgStatus = FileStatus.Ok;

        if (_trancatgStatus == FileStatus.Ok) _applResult = 0; else _applResult = 12; // source: CBTRN03C.cbl:451-455
        if (ApplAok) { /* CONTINUE */ }                   // source: CBTRN03C.cbl:456-457
        else
        {
            Display("ERROR OPENING TRANSACTION CATG FILE"); // source: CBTRN03C.cbl:459
            _ioStatus = _trancatgStatus;                    // MOVE TRANCATG-STATUS TO IO-STATUS  // source: CBTRN03C.cbl:460
            DisplayIoStatus9910();                          // source: CBTRN03C.cbl:461
            AbendProgram9999();                             // source: CBTRN03C.cbl:462
        }
        // EXIT  // source: CBTRN03C.cbl:464
    }

    // *---------------------------------------------------------------*
    /// <summary>0500-DATEPARM-OPEN — OPEN INPUT DATE-PARMS-FILE (the parameter dataset). // source: CBTRN03C.cbl:466-482</summary>
    private void DateparmOpen0500()
    {
        _applResult = 8;                                  // MOVE 8 TO APPL-RESULT  // source: CBTRN03C.cbl:467
        // OPEN INPUT DATE-PARMS-FILE -> the parameter pair is supplied to Run.  // source: CBTRN03C.cbl:468
        _dateparmStatus = FileStatus.Ok;

        if (_dateparmStatus == FileStatus.Ok) _applResult = 0; else _applResult = 12; // source: CBTRN03C.cbl:469-473
        if (ApplAok) { /* CONTINUE */ }                   // source: CBTRN03C.cbl:474-475
        else
        {
            Display("ERROR OPENING DATE PARM FILE");      // source: CBTRN03C.cbl:477
            _ioStatus = _dateparmStatus;                  // MOVE DATEPARM-STATUS TO IO-STATUS  // source: CBTRN03C.cbl:478
            DisplayIoStatus9910();                        // source: CBTRN03C.cbl:479
            AbendProgram9999();                           // source: CBTRN03C.cbl:480
        }
        // EXIT  // source: CBTRN03C.cbl:482
    }

    // *---------------------------------------------------------------*
    /// <summary>
    /// 1500-A-LOOKUP-XREF — random keyed READ of CARD_XREF by FD-XREF-CARD-NUM (INVALID KEY -> abend).
    /// On success XREF-ACCT-ID is set for 1120-WRITE-DETAIL. // source: CBTRN03C.cbl:484-492
    /// </summary>
    private void LookupXref1500A()
    {
        // READ XREF-FILE INTO CARD-XREF-RECORD (KEY = FD-XREF-CARD-NUM)  // source: CBTRN03C.cbl:485
        _cardxrefStatus = _xref.ReadByKey(_fdXrefCardNum, out CardXref? xref);
        if (_cardxrefStatus != FileStatus.Ok)
        {
            // INVALID KEY  // source: CBTRN03C.cbl:486
            Display("INVALID CARD NUMBER : " + _fdXrefCardNum); // source: CBTRN03C.cbl:487
            _ioStatus = "23";                                   // MOVE 23 TO IO-STATUS (bug #9 -> '0023')  // source: CBTRN03C.cbl:488
            DisplayIoStatus9910();                              // source: CBTRN03C.cbl:489
            AbendProgram9999();                                 // source: CBTRN03C.cbl:490
        }
        else
        {
            _cardXrefRecord = xref; // READ ... INTO CARD-XREF-RECORD (only on a successful read)
        }
        // END-READ / EXIT  // source: CBTRN03C.cbl:491-492
    }

    // *---------------------------------------------------------------*
    /// <summary>
    /// 1500-B-LOOKUP-TRANTYPE — random keyed READ of TRAN_TYPE by FD-TRAN-TYPE (INVALID KEY -> abend).
    /// On success TRAN-TYPE-DESC is set. // source: CBTRN03C.cbl:494-502
    /// </summary>
    private void LookupTrantype1500B()
    {
        // READ TRANTYPE-FILE INTO TRAN-TYPE-RECORD (KEY = FD-TRAN-TYPE)  // source: CBTRN03C.cbl:495
        _trantypeStatus = _trantype.ReadByKey(_fdTranType, out TranType? type);
        if (_trantypeStatus != FileStatus.Ok)
        {
            // INVALID KEY  // source: CBTRN03C.cbl:496
            Display("INVALID TRANSACTION TYPE : " + _fdTranType); // source: CBTRN03C.cbl:497
            _ioStatus = "23";                                     // MOVE 23 TO IO-STATUS  // source: CBTRN03C.cbl:498
            DisplayIoStatus9910();                                // source: CBTRN03C.cbl:499
            AbendProgram9999();                                   // source: CBTRN03C.cbl:500
        }
        else
        {
            _tranTypeRecord = type; // READ ... INTO TRAN-TYPE-RECORD (only on a successful read)
        }
        // END-READ / EXIT  // source: CBTRN03C.cbl:501-502
    }

    // *---------------------------------------------------------------*
    /// <summary>
    /// 1500-C-LOOKUP-TRANCATG — random keyed READ of TRAN_CATEGORY by the composite FD-TRAN-CAT-KEY
    /// (FD-TRAN-TYPE-CD + FD-TRAN-CAT-CD) (INVALID KEY -> abend). On success TRAN-CAT-TYPE-DESC is set.
    /// // source: CBTRN03C.cbl:504-512
    /// </summary>
    private void LookupTrancatg1500C()
    {
        // READ TRANCATG-FILE INTO TRAN-CAT-RECORD (KEY = FD-TRAN-CAT-KEY)  // source: CBTRN03C.cbl:505
        _trancatgStatus = _trancatg.ReadByKey(_fdTranTypeCd, _fdTranCatCd, out TranCategory? category);
        if (_trancatgStatus != FileStatus.Ok)
        {
            // INVALID KEY  // source: CBTRN03C.cbl:506
            Display("INVALID TRAN CATG KEY : " + _fdTranTypeCd + ZonedDigits(_fdTranCatCd, 4)); // source: CBTRN03C.cbl:507
            _ioStatus = "23";                                                                    // MOVE 23 TO IO-STATUS  // source: CBTRN03C.cbl:508
            DisplayIoStatus9910();                                                               // source: CBTRN03C.cbl:509
            AbendProgram9999();                                                                  // source: CBTRN03C.cbl:510
        }
        else
        {
            _tranCatRecord = category; // READ ... INTO TRAN-CAT-RECORD (only on a successful read)
        }
        // END-READ / EXIT  // source: CBTRN03C.cbl:511-512
    }

    // *---------------------------------------------------------------*
    /// <summary>9000-TRANFILE-CLOSE — CLOSE TRANSACT-FILE (end the card-sorted cursor). // source: CBTRN03C.cbl:514-530</summary>
    private void TranfileClose9000()
    {
        _applResult = 0 + 8;                              // ADD 8 TO ZERO GIVING APPL-RESULT (-> 8)  // source: CBTRN03C.cbl:515
        _transactCursor?.Dispose();                       // CLOSE TRANSACT-FILE  // source: CBTRN03C.cbl:516
        _transactCursor = null;
        _tranfileStatus = FileStatus.Ok;

        if (_tranfileStatus == FileStatus.Ok) _applResult -= _applResult; else _applResult = 0 + 12; // source: CBTRN03C.cbl:517-521
        if (ApplAok) { /* CONTINUE */ }                   // source: CBTRN03C.cbl:522-523
        else
        {
            Display("ERROR CLOSING POSTED TRANSACTION FILE"); // source: CBTRN03C.cbl:525
            _ioStatus = _tranfileStatus;                      // MOVE TRANFILE-STATUS TO IO-STATUS  // source: CBTRN03C.cbl:526
            DisplayIoStatus9910();                            // source: CBTRN03C.cbl:527
            AbendProgram9999();                               // source: CBTRN03C.cbl:528
        }
        // EXIT  // source: CBTRN03C.cbl:530
    }

    // *---------------------------------------------------------------*
    /// <summary>9100-REPTFILE-CLOSE — CLOSE REPORT-FILE (the report writer is flushed/closed in Execute). // source: CBTRN03C.cbl:532-548</summary>
    private void ReptfileClose9100()
    {
        _applResult = 0 + 8;                              // ADD 8 TO ZERO GIVING APPL-RESULT  // source: CBTRN03C.cbl:533
        _writer?.Flush();                                 // CLOSE REPORT-FILE (final dispose in Execute's finally)  // source: CBTRN03C.cbl:534
        _treptStatus = FileStatus.Ok;

        if (_treptStatus == FileStatus.Ok) _applResult -= _applResult; else _applResult = 0 + 12; // source: CBTRN03C.cbl:535-539
        if (ApplAok) { /* CONTINUE */ }                   // source: CBTRN03C.cbl:540-541
        else
        {
            Display("ERROR CLOSING REPORT FILE");         // source: CBTRN03C.cbl:543
            _ioStatus = _treptStatus;                     // MOVE TRANREPT-STATUS TO IO-STATUS  // source: CBTRN03C.cbl:544
            DisplayIoStatus9910();                        // source: CBTRN03C.cbl:545
            AbendProgram9999();                           // source: CBTRN03C.cbl:546
        }
        // EXIT  // source: CBTRN03C.cbl:548
    }

    // *---------------------------------------------------------------*
    /// <summary>9200-CARDXREF-CLOSE — CLOSE XREF-FILE. // source: CBTRN03C.cbl:551-567</summary>
    private void CardxrefClose9200()
    {
        _applResult = 8;                                  // MOVE 8 TO APPL-RESULT  // source: CBTRN03C.cbl:552
        _ = _xref;                                        // CLOSE XREF-FILE  // source: CBTRN03C.cbl:553
        _cardxrefStatus = FileStatus.Ok;

        if (_cardxrefStatus == FileStatus.Ok) _applResult = 0; else _applResult = 12; // source: CBTRN03C.cbl:554-558
        if (ApplAok) { /* CONTINUE */ }                   // source: CBTRN03C.cbl:559-560
        else
        {
            Display("ERROR CLOSING CROSS REF FILE");      // source: CBTRN03C.cbl:562
            _ioStatus = _cardxrefStatus;                  // MOVE CARDXREF-STATUS TO IO-STATUS  // source: CBTRN03C.cbl:563
            DisplayIoStatus9910();                        // source: CBTRN03C.cbl:564
            AbendProgram9999();                           // source: CBTRN03C.cbl:565
        }
        // EXIT  // source: CBTRN03C.cbl:567
    }

    // *---------------------------------------------------------------*
    /// <summary>9300-TRANTYPE-CLOSE — CLOSE TRANTYPE-FILE. // source: CBTRN03C.cbl:569-585</summary>
    private void TrantypeClose9300()
    {
        _applResult = 8;                                  // MOVE 8 TO APPL-RESULT  // source: CBTRN03C.cbl:570
        _ = _trantype;                                    // CLOSE TRANTYPE-FILE  // source: CBTRN03C.cbl:571
        _trantypeStatus = FileStatus.Ok;

        if (_trantypeStatus == FileStatus.Ok) _applResult = 0; else _applResult = 12; // source: CBTRN03C.cbl:572-576
        if (ApplAok) { /* CONTINUE */ }                   // source: CBTRN03C.cbl:577-578
        else
        {
            Display("ERROR CLOSING TRANSACTION TYPE FILE"); // source: CBTRN03C.cbl:580
            _ioStatus = _trantypeStatus;                    // MOVE TRANTYPE-STATUS TO IO-STATUS  // source: CBTRN03C.cbl:581
            DisplayIoStatus9910();                          // source: CBTRN03C.cbl:582
            AbendProgram9999();                             // source: CBTRN03C.cbl:583
        }
        // EXIT  // source: CBTRN03C.cbl:585
    }

    // *---------------------------------------------------------------*
    /// <summary>9400-TRANCATG-CLOSE — CLOSE TRANCATG-FILE. // source: CBTRN03C.cbl:587-603</summary>
    private void TrancatgClose9400()
    {
        _applResult = 8;                                  // MOVE 8 TO APPL-RESULT  // source: CBTRN03C.cbl:588
        _ = _trancatg;                                    // CLOSE TRANCATG-FILE  // source: CBTRN03C.cbl:589
        _trancatgStatus = FileStatus.Ok;

        if (_trancatgStatus == FileStatus.Ok) _applResult = 0; else _applResult = 12; // source: CBTRN03C.cbl:590-594
        if (ApplAok) { /* CONTINUE */ }                   // source: CBTRN03C.cbl:595-596
        else
        {
            Display("ERROR CLOSING TRANSACTION CATG FILE"); // source: CBTRN03C.cbl:598
            _ioStatus = _trancatgStatus;                    // MOVE TRANCATG-STATUS TO IO-STATUS  // source: CBTRN03C.cbl:599
            DisplayIoStatus9910();                          // source: CBTRN03C.cbl:600
            AbendProgram9999();                             // source: CBTRN03C.cbl:601
        }
        // EXIT  // source: CBTRN03C.cbl:603
    }

    // *---------------------------------------------------------------*
    /// <summary>9500-DATEPARM-CLOSE — CLOSE DATE-PARMS-FILE. // source: CBTRN03C.cbl:605-621</summary>
    private void DateparmClose9500()
    {
        _applResult = 8;                                  // MOVE 8 TO APPL-RESULT  // source: CBTRN03C.cbl:606
        // CLOSE DATE-PARMS-FILE  // source: CBTRN03C.cbl:607
        _dateparmStatus = FileStatus.Ok;

        if (_dateparmStatus == FileStatus.Ok) _applResult = 0; else _applResult = 12; // source: CBTRN03C.cbl:608-612
        if (ApplAok) { /* CONTINUE */ }                   // source: CBTRN03C.cbl:613-614
        else
        {
            Display("ERROR CLOSING DATE PARM FILE");      // source: CBTRN03C.cbl:616
            _ioStatus = _dateparmStatus;                  // MOVE DATEPARM-STATUS TO IO-STATUS  // source: CBTRN03C.cbl:617
            DisplayIoStatus9910();                        // source: CBTRN03C.cbl:618
            AbendProgram9999();                           // source: CBTRN03C.cbl:619
        }
        // EXIT  // source: CBTRN03C.cbl:621
    }

    // *---------------------------------------------------------------*
    /// <summary>
    /// 9999-ABEND-PROGRAM — DISPLAY 'ABENDING PROGRAM'; MOVE 0 TO TIMING; MOVE 999 TO ABCODE;
    /// CALL 'CEE3ABD' USING ABCODE, TIMING. Maps to a 999 abend (no graceful return). // source: CBTRN03C.cbl:626-630
    /// </summary>
    private void AbendProgram9999()
    {
        Display("ABENDING PROGRAM"); // source: CBTRN03C.cbl:627
        // MOVE 0 TO TIMING; MOVE 999 TO ABCODE; CALL 'CEE3ABD' USING ABCODE, TIMING.  // source: CBTRN03C.cbl:628-630
        _writer?.Flush();
        throw new AbendException("999", $"CBTRN03C abend (CEE3ABD); FILE STATUS '{_ioStatus}'.");
    }

    // *****************************************************************
    /// <summary>
    /// 9910-DISPLAY-IO-STATUS — formats the 2-byte file status into a 4-char "NNNN" string and DISPLAYs
    /// <c>'FILE STATUS IS: NNNN' IO-STATUS-04</c>. // source: CBTRN03C.cbl:633-646
    /// </summary>
    private void DisplayIoStatus9910()
    {
        // IO-STATUS-04 = IO-STATUS-0401 PIC 9 + IO-STATUS-0403 PIC 999  // source: CBTRN03C.cbl:146-148
        string s = _ioStatus.Length >= 2 ? _ioStatus[..2] : _ioStatus.PadRight(2);
        char ioStat1 = s[0];
        char ioStat2 = s[1];

        string ioStatus04;

        // IF IO-STATUS NOT NUMERIC OR IO-STAT1 = '9'  // source: CBTRN03C.cbl:634-635
        if (!IsNumeric(s) || ioStat1 == '9')
        {
            // MOVE IO-STAT1 TO IO-STATUS-04(1:1)  // source: CBTRN03C.cbl:636
            char pos1 = ioStat1;

            // MOVE 0 TO TWO-BYTES-BINARY; MOVE IO-STAT2 TO TWO-BYTES-RIGHT;
            // MOVE TWO-BYTES-BINARY TO IO-STATUS-0403.  // source: CBTRN03C.cbl:637-639
            // FAITHFUL BUG #8: TWO-BYTES-BINARY is a 9(4) BINARY (big-endian halfword) whose low (right)
            // byte is set from IO-STAT2, so reading the halfword yields that character's host code point.
            int twoBytesBinary = HostEncoding.For(_host).GetBytes(ioStat2.ToString())[0]; // low byte value
            int ioStatus0403 = twoBytesBinary % 1000;            // store into PIC 999 (3 digits)
            ioStatus04 = pos1.ToString() + ioStatus0403.ToString("D3"); // source: CBTRN03C.cbl:636-639
        }
        else
        {
            // MOVE '0000' TO IO-STATUS-04; MOVE IO-STATUS TO IO-STATUS-04(3:2).  // source: CBTRN03C.cbl:642-643
            ioStatus04 = "00" + s;
        }

        // DISPLAY 'FILE STATUS IS: NNNN' IO-STATUS-04  // source: CBTRN03C.cbl:640,644
        Display("FILE STATUS IS: NNNN" + ioStatus04);
        // EXIT  // source: CBTRN03C.cbl:646
    }

    // =================================================================================================
    // CVTRA07Y report-layout builders (each returns a fixed-width string, padded to 133 on WRITE)
    // =================================================================================================

    /// <summary>REPORT-NAME-HEADER (115 chars before pad). // source: cpy/CVTRA07Y.cpy:4-13</summary>
    private string BuildReportNameHeader()
    {
        var sb = new System.Text.StringBuilder(ReportRecLen);
        sb.Append(Alpha("DALYREPT", 38));                  // REPT-SHORT-NAME X(38) VALUE 'DALYREPT'
        sb.Append(Alpha("Daily Transaction Report", 41));  // REPT-LONG-NAME  X(41)
        sb.Append(Alpha("Date Range: ", 12));              // REPT-DATE-HEADER X(12)
        sb.Append(Alpha(_wsStartDate, 10));                // REPT-START-DATE X(10) (= WS-START-DATE)
        sb.Append(" to ");                                  // FILLER X(04) VALUE ' to '
        sb.Append(Alpha(_wsEndDate, 10));                  // REPT-END-DATE X(10) (= WS-END-DATE)
        return sb.ToString();
    }

    /// <summary>
    /// TRANSACTION-DETAIL-REPORT (113 chars before pad). The FILLER positions are the constant
    /// separators ('-' at the two type/cat separators, spaces elsewhere) per INITIALIZE semantics.
    /// // source: cpy/CVTRA07Y.cpy:15-31
    /// </summary>
    private static string BuildTransactionDetailReport(
        string transId, string accountId, string typeCd, string typeDesc,
        int catCd, string catDesc, string source, decimal amt)
    {
        var sb = new System.Text.StringBuilder(ReportRecLen);
        sb.Append(Alpha(transId, 16));        // TRAN-REPORT-TRANS-ID X(16)
        sb.Append(' ');                       // FILLER X(01) sp
        sb.Append(Alpha(accountId, 11));      // TRAN-REPORT-ACCOUNT-ID X(11)
        sb.Append(' ');                       // FILLER X(01) sp
        sb.Append(Alpha(typeCd, 2));          // TRAN-REPORT-TYPE-CD X(02)
        sb.Append('-');                       // FILLER X(01) '-'
        sb.Append(Alpha(typeDesc, 15));       // TRAN-REPORT-TYPE-DESC X(15)
        sb.Append(' ');                       // FILLER X(01) sp
        sb.Append(ZonedDigits(catCd, 4));     // TRAN-REPORT-CAT-CD 9(04)
        sb.Append('-');                       // FILLER X(01) '-'
        sb.Append(Alpha(catDesc, 29));        // TRAN-REPORT-CAT-DESC X(29)
        sb.Append(' ');                       // FILLER X(01) sp
        sb.Append(Alpha(source, 10));         // TRAN-REPORT-SOURCE X(10)
        sb.Append("    ");                     // FILLER X(04) sp
        sb.Append(CobolEditedNumeric.Format(StoreMoney(amt), DetailAmtPic)); // TRAN-REPORT-AMT -ZZZ,ZZZ,ZZZ.ZZ
        sb.Append("  ");                       // FILLER X(02) sp
        return sb.ToString();
    }

    /// <summary>TRANSACTION-HEADER-1 (114 chars before pad). // source: cpy/CVTRA07Y.cpy:33-46</summary>
    private static string BuildTransactionHeader1()
    {
        var sb = new System.Text.StringBuilder(ReportRecLen);
        sb.Append(Alpha("Transaction ID", 17)); // FILLER X(17)
        sb.Append(Alpha("Account ID", 12));      // FILLER X(12)
        sb.Append(Alpha("Transaction Type", 19));// FILLER X(19)
        sb.Append(Alpha("Tran Category", 35));    // FILLER X(35)
        sb.Append(Alpha("Tran Source", 14));      // FILLER X(14)
        sb.Append(' ');                           // FILLER X(01) sp
        sb.Append(Alpha("        Amount", 16));    // FILLER X(16) VALUE '        Amount'
        return sb.ToString();
    }

    /// <summary>TRANSACTION-HEADER-2 — PIC X(133) VALUE ALL '-'. // source: cpy/CVTRA07Y.cpy:48</summary>
    private static string BuildTransactionHeader2() => new('-', ReportRecLen);

    /// <summary>REPORT-PAGE-TOTALS (111 chars before pad). // source: cpy/CVTRA07Y.cpy:50-54</summary>
    private string BuildReportPageTotals(decimal pageTotal)
    {
        var sb = new System.Text.StringBuilder(ReportRecLen);
        sb.Append(Alpha("Page Total", 11));  // FILLER X(11) VALUE 'Page Total'
        sb.Append(new string('.', 86));       // FILLER X(86) VALUE ALL '.'
        sb.Append(CobolEditedNumeric.Format(StoreMoney(pageTotal), TotalAmtPic)); // REPT-PAGE-TOTAL +ZZZ,ZZZ,ZZZ.ZZ
        return sb.ToString();
    }

    /// <summary>REPORT-ACCOUNT-TOTALS (111 chars before pad). // source: cpy/CVTRA07Y.cpy:56-60</summary>
    private string BuildReportAccountTotals(decimal accountTotal)
    {
        var sb = new System.Text.StringBuilder(ReportRecLen);
        sb.Append(Alpha("Account Total", 13)); // FILLER X(13) VALUE 'Account Total'
        sb.Append(new string('.', 84));         // FILLER X(84) VALUE ALL '.'
        sb.Append(CobolEditedNumeric.Format(StoreMoney(accountTotal), TotalAmtPic)); // REPT-ACCOUNT-TOTAL
        return sb.ToString();
    }

    /// <summary>REPORT-GRAND-TOTALS (111 chars before pad). // source: cpy/CVTRA07Y.cpy:62-66</summary>
    private string BuildReportGrandTotals(decimal grandTotal)
    {
        var sb = new System.Text.StringBuilder(ReportRecLen);
        sb.Append(Alpha("Grand Total", 11)); // FILLER X(11) VALUE 'Grand Total'
        sb.Append(new string('.', 86));       // FILLER X(86) VALUE ALL '.'
        sb.Append(CobolEditedNumeric.Format(StoreMoney(grandTotal), TotalAmtPic)); // REPT-GRAND-TOTAL
        return sb.ToString();
    }

    // =================================================================================================
    // Helpers
    // =================================================================================================

    /// <summary>
    /// Reproduces <c>DISPLAY TRAN-RECORD</c>: the 350-byte CVTRA05Y logical image rendered as host text —
    /// every elementary field in copybook order then a 20-byte FILLER of spaces. Numeric (USAGE DISPLAY)
    /// fields are zoned (CAT-CD / MERCHANT-ID unsigned; AMT signed with the sign overpunched on the last
    /// digit, no decimal point); alphanumerics are left-justified, space-padded. // source: CBTRN03C.cbl:180; cpy/CVTRA05Y.cpy:4-18
    /// </summary>
    private string DisplayTranRecord()
    {
        Transaction t = _tranRecord;
        var bytes = new List<byte>(350);
        void Az(string text, int width)
        {
            string padded = (text ?? "").Length >= width ? text![..width] : (text ?? "").PadRight(width, ' ');
            bytes.AddRange(HostEncoding.For(_host).GetBytes(padded));
        }
        void Nz(decimal value, int totalDigits, int scale, bool signed)
        {
            var field = new byte[totalDigits];
            ZonedDecimalCodec.Encode(value, field, totalDigits, scale, signed, _host);
            bytes.AddRange(field);
        }

        Az(t.TranId, 16);                 // TRAN-ID            PIC X(16)
        Az(t.TypeCd, 2);                  // TRAN-TYPE-CD       PIC X(02)
        Nz(t.CatCd, 4, 0, signed: false); // TRAN-CAT-CD        PIC 9(04)
        Az(t.Source, 10);                 // TRAN-SOURCE        PIC X(10)
        Az(t.Desc, 100);                  // TRAN-DESC          PIC X(100)
        Nz(t.Amt, 11, 2, signed: true);   // TRAN-AMT           PIC S9(09)V99
        Nz(t.MerchantId, 9, 0, signed: false); // TRAN-MERCHANT-ID PIC 9(09)
        Az(t.MerchantName, 50);           // TRAN-MERCHANT-NAME PIC X(50)
        Az(t.MerchantCity, 50);           // TRAN-MERCHANT-CITY PIC X(50)
        Az(t.MerchantZip, 10);            // TRAN-MERCHANT-ZIP  PIC X(10)
        Az(t.CardNum, 16);                // TRAN-CARD-NUM      PIC X(16)
        Az(t.OrigTs, 26);                 // TRAN-ORIG-TS       PIC X(26)
        Az(t.ProcTs, 26);                 // TRAN-PROC-TS       PIC X(26)
        Az("", 20);                       // FILLER             PIC X(20)
        return HostEncoding.For(_host).GetString(bytes.ToArray());
    }

    /// <summary>
    /// READ TRANSACT-FILE INTO TRAN-RECORD — advance the card-sorted forward cursor. Returns '00' with the
    /// next row, or '10' at end of file (leaving TRAN-RECORD unchanged). // source: CBTRN03C.cbl:249
    /// </summary>
    private string ReadNextCardSorted(out Transaction? next)
    {
        if (_transactCursor is not null && _transactCursor.MoveNext())
        {
            next = _transactCursor.Current;
            return FileStatus.Ok;
        }
        next = null;
        return FileStatus.EndOfFile;
    }

    /// <summary>
    /// ADD TRAN-AMT TO WS-PAGE-TOTAL WS-ACCOUNT-TOTAL — accumulate into both S9(9)V99 totals with COBOL
    /// truncate-toward-zero / silent overflow. // source: CBTRN03C.cbl:200-201, 287-288
    /// </summary>
    private void AddTranAmt(decimal amt)
    {
        _pageTotal = AddMoney(_pageTotal, amt);
        _accountTotal = AddMoney(_accountTotal, amt);
    }

    /// <summary>S9(9)V99 ADD: sum then store into the field (truncate-toward-zero, silent high-order overflow).</summary>
    private static decimal AddMoney(decimal a, decimal b)
        => Decimals.Store(a + b, MoneyIntDigits, MoneyScale, signed: true);

    /// <summary>Stores a value into the S9(9)V99 shape before edited formatting (matches the field width).</summary>
    private static decimal StoreMoney(decimal value)
        => Decimals.Store(value, MoneyIntDigits, MoneyScale, signed: true);

    /// <summary>DISPLAY of a (possibly signed) zoned PIC -> host text (no decimal point; implied V). // SYSOUT only.</summary>
    private string DisplayAmt(decimal amt)
    {
        var field = new byte[11]; // TRAN-AMT S9(09)V99 -> 11 digits
        ZonedDecimalCodec.Encode(amt, field, 11, 2, signed: true, _host);
        return HostEncoding.For(_host).GetString(field);
    }

    /// <summary>DISPLAY of WS-PAGE-TOTAL (S9(09)V99) -> 11 zoned digits, host text. // SYSOUT only.</summary>
    private string DisplayTotal(decimal total)
    {
        var field = new byte[11];
        ZonedDecimalCodec.Encode(total, field, 11, 2, signed: true, _host);
        return HostEncoding.For(_host).GetString(field);
    }

    /// <summary>DISPLAY -&gt; SYSOUT: collect the line (the report dataset is written separately).</summary>
    private void Display(string line) => _sysout.Add(line);

    /// <summary>COBOL alphanumeric MOVE of a PIC X(width): left-justify, space-pad / right-truncate.</summary>
    private static string Alpha(string? text, int width)
    {
        text ??= "";
        return text.Length >= width ? text[..width] : text.PadRight(width, ' ');
    }

    /// <summary>Substring reference modification value(off:len) over a string (space-padded if short).</summary>
    private static string Substr(string? text, int offset, int length)
    {
        text ??= "";
        if (text.Length < offset + length) text = text.PadRight(offset + length, ' ');
        return text.Substring(offset, length);
    }

    /// <summary>Unsigned zoned-digit string for a numeric MOVE to an alphanumeric/edited 9(width) display.</summary>
    private static string ZonedDigits(long value, int width)
    {
        // Unsigned PIC 9(width): absolute value, zero-padded, low-order digits kept on overflow.
        decimal modulus = Decimals.Pow10(width);
        decimal v = Math.Abs((decimal)value) % modulus;
        return ((long)v).ToString().PadLeft(width, '0');
    }

    /// <summary>WRITE FD-REPTFILE-REC: pad/truncate the built template to exactly the 133-byte record.</summary>
    private static string FixedLine(string text, int width)
        => text.Length >= width ? text[..width] : text.PadRight(width, ' ');

    /// <summary>COBOL "class NUMERIC" test for a 2-char IO-STATUS (every char is a decimal digit).</summary>
    private static bool IsNumeric(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (char ch in s)
            if (ch < '0' || ch > '9') return false;
        return true;
    }
}
