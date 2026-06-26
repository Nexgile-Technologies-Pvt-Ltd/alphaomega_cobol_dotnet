using CardDemo.Cobol.Runtime;
using CardDemo.Data;
using CardDemo.Domain;

namespace CardDemo.Batch;

/// <summary>
/// Faithful relational re-port of the batch/util program <c>CBTRN01C</c> ("Post the records from daily
/// transaction file" — header text only). In practice it is a daily-transaction <b>read-and-validate</b>
/// driver: it opens the sequential daily-transaction input plus five indexed masters, then loops over the
/// daily-transaction file record by record. For each record it (a) <c>DISPLAY</c>s the raw 350-byte image,
/// (b) takes the transaction's card number and looks it up in the card cross-reference (random keyed read),
/// and (c) if the xref is found, uses the cross-referenced account id to do a random keyed read of the
/// account master. It emits only <c>DISPLAY</c> diagnostics — <b>no updates, no writes, no posting, and no
/// balance arithmetic</b> (the real posting logic lives in CBTRN02C). After end-of-file it closes all six
/// files and <c>GOBACK</c>s.
/// </summary>
/// <remarks>
/// <para>Ported paragraph-by-paragraph from <c>app/cbl/CBTRN01C.cbl</c>: each PROCEDURE-DIVISION paragraph
/// is a method whose name mirrors the COBOL paragraph, keeps the original statement order / PERFORM flow,
/// and carries <c>// source: CBTRN01C.cbl:NNN</c> citations. Per <c>_design/ARCHITECTURE.md</c> the VSAM
/// files are now relational tables read through repositories: the sequential DALYTRAN read is a forward
/// cursor over <see cref="DailyTransactionRepository"/> (DAILY_TRANSACTION, ascending <c>tran_id</c>); the
/// XREF/ACCOUNT keyed reads are <see cref="CardXrefRepository.ReadByKey"/> /
/// <see cref="AccountRepository.ReadByKey"/> (FileStatus '00'/'23' = found/INVALID KEY). Every COBOL
/// <c>DISPLAY</c> is collected into <see cref="Sysout"/> in order; when an output path is supplied to
/// <see cref="Run(RelationalDb,string?)"/> the same lines are also appended to that flat SYSOUT report file.</para>
/// <para>FAITHFUL BUGS reproduced verbatim (see <c>_design/specs/CBTRN01C.md</c> §7):
/// <list type="number">
/// <item><b>CUSTOMER, CARD, and TRANSACT files are opened and closed but never read/written.</b> Pure
/// OPEN/CLOSE dead weight; reproduced as open/close no-ops with no query issued.</item>
/// <item><b>The XREF/ACCOUNT lookup runs one extra time on the EOF iteration using the STALE last
/// record.</b> The loop body's lookups (lines 170-184) sit OUTSIDE the inner <c>IF END-OF-DAILY-TRANS-FILE
/// = 'N'</c> guard (which only guards the <c>DISPLAY DALYTRAN-RECORD</c>), so after EOF they re-validate
/// whatever <c>DALYTRAN-RECORD</c> still held (READ ... INTO leaves the record unchanged on status '10').
/// The last real transaction is validated twice. NOT guarded — reproduced exactly.</item>
/// <item><b>Wrong DISPLAY literal in 9000-DALYTRAN-CLOSE:</b> a DALYTRAN-close error displays
/// <c>'ERROR CLOSING CUSTOMER FILE'</c> (copy-paste bug), not a daily-transaction message.</item>
/// <item><b>Wrong status field in 9000-DALYTRAN-CLOSE:</b> on a DALYTRAN-close error it does
/// <c>MOVE CUSTFILE-STATUS TO IO-STATUS</c> — it reports the CUSTOMER file's status, not DALYTRAN's.</item>
/// <item><b>Inconsistent priming style (cosmetic):</b> opens use <c>MOVE 8 TO APPL-RESULT</c>, closes use
/// <c>ADD 8 TO ZERO GIVING APPL-RESULT</c>. Both yield 8; reproduced as written.</item>
/// <item><b>Redundant inner <c>IF END-OF-DAILY-TRANS-FILE = 'N'</c> guard</b> duplicates the
/// <c>PERFORM UNTIL ... = 'Y'</c> condition; reproduced as-is.</item>
/// <item><b><c>Z-DISPLAY-IO-STATUS</c> big-endian halfword rendering:</b> on the non-numeric /
/// <c>IO-STAT1='9'</c> branch the 2nd status char is reinterpreted as the low byte of a big-endian
/// halfword and printed as a 0..255 number (its host code point). Reproduced big-endian.</item>
/// </list></para>
/// </remarks>
public sealed class Cbtrn01c
{
    // PIC widths for the DALYTRAN-RECORD subfields (CVTRA06Y, RECLN 350). Used by DISPLAY DALYTRAN-RECORD.
    private const int CardNumWidth = 16;   // DALYTRAN-CARD-NUM / XREF-CARD-NUM / FD-XREF-CARD-NUM PIC X(16)
    private const int AcctIdDigits = 11;   // ACCT-ID / FD-ACCT-ID / XREF-ACCT-ID PIC 9(11)
    private const int CustIdDigits = 9;    // XREF-CUST-ID PIC 9(09)

    // --- Repositories (the relational replacements for the six VSAM/QSAM files) ----------------------
    private DailyTransactionRepository _dalytran = null!; // DALYTRAN-FILE (DD DALYTRAN) -> DAILY_TRANSACTION
    private CustomerRepository _custfile = null!;          // CUSTOMER-FILE (DD CUSTFILE) -> CUSTOMER (open/close only)
    private CardXrefRepository _xreffile = null!;          // XREF-FILE     (DD XREFFILE) -> CARD_XREF
    private CardRepository _cardfile = null!;              // CARD-FILE     (DD CARDFILE) -> CARD (open/close only)
    private AccountRepository _acctfile = null!;           // ACCOUNT-FILE  (DD ACCTFILE) -> ACCOUNT
    private TransactionRepository _tranfile = null!;       // TRANSACT-FILE (DD TRANFILE) -> TRANSACTION (open/close only)

    // --- WORKING-STORAGE file-status fields (CBTRN01C lines 100-127) ----------------------------------
    private string _dalytranStatus = "00"; // DALYTRAN-STATUS  // source: CBTRN01C.cbl:100-102
    private string _custfileStatus = "00"; // CUSTFILE-STATUS  // source: CBTRN01C.cbl:105-107
    private string _xreffileStatus = "00"; // XREFFILE-STATUS  // source: CBTRN01C.cbl:110-112
    private string _cardfileStatus = "00"; // CARDFILE-STATUS  // source: CBTRN01C.cbl:115-117
    private string _acctfileStatus = "00"; // ACCTFILE-STATUS  // source: CBTRN01C.cbl:120-122
    private string _tranfileStatus = "00"; // TRANFILE-STATUS  // source: CBTRN01C.cbl:125-127

    /// <summary>IO-STATUS — working copy of a file status used by Z-DISPLAY-IO-STATUS. // source: CBTRN01C.cbl:129-131</summary>
    private string _ioStatus = "00";

    /// <summary>APPL-RESULT PIC S9(9) COMP. 88 APPL-AOK=0, APPL-EOF=16; 8=priming, 12=hard error. // source: CBTRN01C.cbl:142-144</summary>
    private int _applResult;

    /// <summary>END-OF-DAILY-TRANS-FILE PIC X(01) VALUE 'N' — loop sentinel (true == 'Y'). // source: CBTRN01C.cbl:146</summary>
    private bool _endOfDailyTransFile;

    /// <summary>WS-XREF-READ-STATUS PIC 9(04) — 0=found, 4=not found. // source: CBTRN01C.cbl:150</summary>
    private int _wsXrefReadStatus;

    /// <summary>WS-ACCT-READ-STATUS PIC 9(04) — 0=found, 4=not found. // source: CBTRN01C.cbl:151</summary>
    private int _wsAcctReadStatus;

    // --- Record areas the READ ... INTO statements populate -------------------------------------------

    /// <summary>
    /// DALYTRAN-RECORD (CVTRA06Y, 350 bytes) — last record read; stale on EOF (Faithful Bug #2). Starts as
    /// a spaces/zeros working-storage record so that, on an empty DALYTRAN file, the unconditional EOF-pass
    /// lookups run against the uninitialized record exactly as COBOL would (no crash). // source: CBTRN01C.cbl:99,203
    /// </summary>
    private DailyTransaction _dalytranRecord = new()
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

    /// <summary>CARD-XREF-RECORD (CVACT03Y, 50 bytes) — populated by the XREF keyed read. // source: CBTRN01C.cbl:109,229</summary>
    private CardXref? _cardXrefRecord;

    /// <summary>ACCOUNT-RECORD (CVACT01Y, 300 bytes) — target of the ACCOUNT keyed read (contents unused). // source: CBTRN01C.cbl:119,243</summary>
    private Account? _accountRecord;

    // --- Working-storage scalar key fields (set by MAIN, consumed by the lookups) ---------------------

    /// <summary>XREF-CARD-NUM PIC X(16) — set from DALYTRAN-CARD-NUM in MAIN, source of FD-XREF-CARD-NUM. // source: CBTRN01C.cbl:171,228</summary>
    private string _xrefCardNum = new(' ', CardNumWidth);

    /// <summary>ACCT-ID PIC 9(11) — set from XREF-ACCT-ID in MAIN, source of FD-ACCT-ID. // source: CBTRN01C.cbl:175,242</summary>
    private long _acctId;

    private readonly HostKind _host;
    private FixedFileWriter? _writer;
    private readonly List<string> _sysout = [];

    /// <summary>88 APPL-AOK VALUE 0. // source: CBTRN01C.cbl:143</summary>
    private bool ApplAok => _applResult == 0;

    /// <summary>88 APPL-EOF VALUE 16. // source: CBTRN01C.cbl:144</summary>
    private bool ApplEof => _applResult == 16;

    private Cbtrn01c(HostKind host) => _host = host;

    /// <summary>The SYSOUT (DISPLAY) lines produced by the run, in order.</summary>
    public IReadOnlyList<string> Sysout => _sysout;

    /// <summary>
    /// Runs CBTRN01C over the relational <paramref name="db"/>. When <paramref name="sysoutPath"/> is
    /// given, every DISPLAY line is also appended to that flat report file (ASCII host) in addition to
    /// being collected into <see cref="Sysout"/>. Returns the SYSOUT lines in order.
    /// </summary>
    public static IReadOnlyList<string> Run(RelationalDb db, string? sysoutPath = null)
    {
        var program = new Cbtrn01c(HostKind.Ascii);
        program._dalytran = new DailyTransactionRepository(db);
        program._custfile = new CustomerRepository(db);
        program._xreffile = new CardXrefRepository(db);
        program._cardfile = new CardRepository(db);
        program._acctfile = new AccountRepository(db);
        program._tranfile = new TransactionRepository(db);
        program.Execute(sysoutPath);
        return program.Sysout;
    }

    /// <summary>Convenience overload taking a <see cref="BatchSupport"/> (uses its repositories).</summary>
    public static IReadOnlyList<string> Run(BatchSupport support, string? sysoutPath = null)
    {
        var program = new Cbtrn01c(HostKind.Ascii);
        program._dalytran = support.DailyTransaction;
        program._custfile = support.Customer;
        program._xreffile = support.CardXref;
        program._cardfile = support.Card;
        program._acctfile = support.Account;
        program._tranfile = support.Transaction;
        program.Execute(sysoutPath);
        return program.Sysout;
    }

    // =================================================================================================
    // MAIN-PARA // source: CBTRN01C.cbl:155-197
    // =================================================================================================
    private void Execute(string? sysoutPath)
    {
        _writer = sysoutPath is null ? null : BatchSupport.OpenWriter(sysoutPath, HostKind.Ascii);
        try
        {
            Display("START OF EXECUTION OF PROGRAM CBTRN01C"); // source: CBTRN01C.cbl:156

            DalytranOpen0000();   // source: CBTRN01C.cbl:157
            CustfileOpen0100();   // source: CBTRN01C.cbl:158
            XreffileOpen0200();   // source: CBTRN01C.cbl:159
            CardfileOpen0300();   // source: CBTRN01C.cbl:160
            AcctfileOpen0400();   // source: CBTRN01C.cbl:161
            TranfileOpen0500();   // source: CBTRN01C.cbl:162

            // PERFORM UNTIL END-OF-DAILY-TRANS-FILE = 'Y'  // source: CBTRN01C.cbl:164-186
            while (!_endOfDailyTransFile)
            {
                // IF END-OF-DAILY-TRANS-FILE = 'N' (redundant inner guard — bug #6)  // source: CBTRN01C.cbl:165
                if (!_endOfDailyTransFile)
                {
                    DalytranGetNext1000(); // source: CBTRN01C.cbl:166

                    // IF END-OF-DAILY-TRANS-FILE = 'N' -> DISPLAY DALYTRAN-RECORD  // source: CBTRN01C.cbl:167-169
                    if (!_endOfDailyTransFile)
                        Display(DisplayDalytranRecord()); // DISPLAY DALYTRAN-RECORD  // source: CBTRN01C.cbl:168

                    // FAITHFUL BUG #2: lines 170-184 run UNCONDITIONALLY (outside the EOF guard), so on the
                    // EOF iteration they re-validate the STALE last DALYTRAN-RECORD (READ INTO left it
                    // unchanged on status '10') — the final transaction is validated a second time.
                    _wsXrefReadStatus = 0;                                   // MOVE 0 TO WS-XREF-READ-STATUS  // source: CBTRN01C.cbl:170
                    _xrefCardNum = Alpha(_dalytranRecord.CardNum, CardNumWidth); // MOVE DALYTRAN-CARD-NUM TO XREF-CARD-NUM  // source: CBTRN01C.cbl:171
                    LookupXref2000();                                       // source: CBTRN01C.cbl:172

                    if (_wsXrefReadStatus == 0) // IF WS-XREF-READ-STATUS = 0  // source: CBTRN01C.cbl:173
                    {
                        _wsAcctReadStatus = 0;                  // MOVE 0 TO WS-ACCT-READ-STATUS  // source: CBTRN01C.cbl:174
                        _acctId = _cardXrefRecord!.AcctId;      // MOVE XREF-ACCT-ID TO ACCT-ID  // source: CBTRN01C.cbl:175
                        ReadAccount3000();                      // source: CBTRN01C.cbl:176
                        if (_wsAcctReadStatus != 0)             // IF WS-ACCT-READ-STATUS NOT = 0  // source: CBTRN01C.cbl:177
                            // DISPLAY 'ACCOUNT ' ACCT-ID ' NOT FOUND'  // source: CBTRN01C.cbl:178
                            Display("ACCOUNT " + Zoned(_acctId, AcctIdDigits) + " NOT FOUND");
                    }
                    else // ELSE  // source: CBTRN01C.cbl:180
                    {
                        // DISPLAY 'CARD NUMBER ' DALYTRAN-CARD-NUM
                        //   ' COULD NOT BE VERIFIED. SKIPPING TRANSACTION ID-' DALYTRAN-ID  // source: CBTRN01C.cbl:181-183
                        Display("CARD NUMBER " + Alpha(_dalytranRecord.CardNum, CardNumWidth) +
                                " COULD NOT BE VERIFIED. SKIPPING TRANSACTION ID-" +
                                Alpha(_dalytranRecord.TranId, CardNumWidth));
                    }
                }
            }

            DalytranClose9000();  // source: CBTRN01C.cbl:188
            CustfileClose9100();  // source: CBTRN01C.cbl:189
            XreffileClose9200();  // source: CBTRN01C.cbl:190
            CardfileClose9300();  // source: CBTRN01C.cbl:191
            AcctfileClose9400();  // source: CBTRN01C.cbl:192
            TranfileClose9500();  // source: CBTRN01C.cbl:193

            Display("END OF EXECUTION OF PROGRAM CBTRN01C"); // source: CBTRN01C.cbl:195
            // GOBACK  // source: CBTRN01C.cbl:197
        }
        finally
        {
            _writer?.Flush();
            _writer?.Dispose();
        }
    }

    // *****************************************************************
    // * READS FILE                                                    *
    // *****************************************************************

    /// <summary>
    /// 1000-DALYTRAN-GET-NEXT — sequential READ of the next daily-transaction row. On EOF the
    /// DALYTRAN-RECORD is left unchanged (READ ... INTO is not performed for status '10'). // source: CBTRN01C.cbl:202-225
    /// </summary>
    private void DalytranGetNext1000()
    {
        // READ DALYTRAN-FILE INTO DALYTRAN-RECORD  // source: CBTRN01C.cbl:203
        _dalytranStatus = _dalytran.ReadNext(out DailyTransaction? next);
        if (next is not null)
            _dalytranRecord = next; // INTO DALYTRAN-RECORD — only populated on a successful read (status '00').

        if (_dalytranStatus == FileStatus.Ok)             // DALYTRAN-STATUS = '00'  // source: CBTRN01C.cbl:204
            _applResult = 0;                              // MOVE 0 TO APPL-RESULT  // source: CBTRN01C.cbl:205
        else if (_dalytranStatus == FileStatus.EndOfFile) // DALYTRAN-STATUS = '10'  // source: CBTRN01C.cbl:207
            _applResult = 16;                             // MOVE 16 TO APPL-RESULT  // source: CBTRN01C.cbl:208
        else
            _applResult = 12;                             // MOVE 12 TO APPL-RESULT  // source: CBTRN01C.cbl:210

        if (ApplAok)                                      // IF APPL-AOK  // source: CBTRN01C.cbl:213
        {
            // CONTINUE  // source: CBTRN01C.cbl:214
        }
        else if (ApplEof)                                 // IF APPL-EOF  // source: CBTRN01C.cbl:216
        {
            _endOfDailyTransFile = true;                  // MOVE 'Y' TO END-OF-DAILY-TRANS-FILE  // source: CBTRN01C.cbl:217
        }
        else
        {
            Display("ERROR READING DAILY TRANSACTION FILE"); // source: CBTRN01C.cbl:219
            _ioStatus = _dalytranStatus;                     // MOVE DALYTRAN-STATUS TO IO-STATUS  // source: CBTRN01C.cbl:220
            DisplayIoStatusZ();                              // PERFORM Z-DISPLAY-IO-STATUS  // source: CBTRN01C.cbl:221
            AbendProgramZ();                                 // PERFORM Z-ABEND-PROGRAM  // source: CBTRN01C.cbl:222
        }
        // EXIT  // source: CBTRN01C.cbl:225
    }

    // *---------------------------------------------------------------*
    /// <summary>
    /// 2000-LOOKUP-XREF — random keyed READ of CARD_XREF by XREF-CARD-NUM (INVALID KEY / NOT INVALID KEY).
    /// // source: CBTRN01C.cbl:227-239
    /// </summary>
    private void LookupXref2000()
    {
        // MOVE XREF-CARD-NUM TO FD-XREF-CARD-NUM  // source: CBTRN01C.cbl:228
        string fdXrefCardNum = _xrefCardNum;

        // READ XREF-FILE RECORD INTO CARD-XREF-RECORD KEY IS FD-XREF-CARD-NUM  // source: CBTRN01C.cbl:229-230
        _xreffileStatus = _xreffile.ReadByKey(fdXrefCardNum, out CardXref? xref);
        if (_xreffileStatus != FileStatus.Ok)
        {
            // INVALID KEY  // source: CBTRN01C.cbl:231
            Display("INVALID CARD NUMBER FOR XREF"); // source: CBTRN01C.cbl:232
            _wsXrefReadStatus = 4;                   // MOVE 4 TO WS-XREF-READ-STATUS  // source: CBTRN01C.cbl:233
            // On INVALID KEY the contents of CARD-XREF-RECORD are NOT updated (stay stale).
        }
        else
        {
            // NOT INVALID KEY  // source: CBTRN01C.cbl:234
            _cardXrefRecord = xref; // READ ... INTO CARD-XREF-RECORD (only on a successful read)
            Display("SUCCESSFUL READ OF XREF");                                            // source: CBTRN01C.cbl:235
            Display("CARD NUMBER: " + Alpha(_cardXrefRecord!.XrefCardNum, CardNumWidth));  // source: CBTRN01C.cbl:236
            Display("ACCOUNT ID : " + Zoned(_cardXrefRecord!.AcctId, AcctIdDigits));       // source: CBTRN01C.cbl:237
            Display("CUSTOMER ID: " + Zoned(_cardXrefRecord!.CustId, CustIdDigits));       // source: CBTRN01C.cbl:238
            // WS-XREF-READ-STATUS left at 0.
        }
        // END-READ  // source: CBTRN01C.cbl:239 (no explicit EXIT — control falls through)
    }

    // *---------------------------------------------------------------*
    /// <summary>
    /// 3000-READ-ACCOUNT — random keyed READ of ACCOUNT by ACCT-ID (INVALID KEY / NOT INVALID KEY).
    /// Only existence (status) is used; the record contents are not referenced. // source: CBTRN01C.cbl:241-250
    /// </summary>
    private void ReadAccount3000()
    {
        // MOVE ACCT-ID TO FD-ACCT-ID  // source: CBTRN01C.cbl:242
        long fdAcctId = _acctId;

        // READ ACCOUNT-FILE RECORD INTO ACCOUNT-RECORD KEY IS FD-ACCT-ID  // source: CBTRN01C.cbl:243-244
        _acctfileStatus = _acctfile.ReadByKey(fdAcctId, out Account? account);
        if (_acctfileStatus != FileStatus.Ok)
        {
            // INVALID KEY  // source: CBTRN01C.cbl:245
            Display("INVALID ACCOUNT NUMBER FOUND"); // source: CBTRN01C.cbl:246
            _wsAcctReadStatus = 4;                   // MOVE 4 TO WS-ACCT-READ-STATUS  // source: CBTRN01C.cbl:247
        }
        else
        {
            // NOT INVALID KEY  // source: CBTRN01C.cbl:248
            _accountRecord = account; // READ ... INTO ACCOUNT-RECORD (only on a successful read)
            Display("SUCCESSFUL READ OF ACCOUNT FILE"); // source: CBTRN01C.cbl:249
            // WS-ACCT-READ-STATUS stays 0.
        }
        // END-READ  // source: CBTRN01C.cbl:250 (no explicit EXIT)
    }

    // *---------------------------------------------------------------*
    /// <summary>0000-DALYTRAN-OPEN — OPEN INPUT the daily-transaction sequential file. // source: CBTRN01C.cbl:252-268</summary>
    private void DalytranOpen0000()
    {
        _applResult = 8;                                  // MOVE 8 TO APPL-RESULT  // source: CBTRN01C.cbl:253
        // OPEN INPUT DALYTRAN-FILE -> position a forward cursor over DAILY_TRANSACTION (ascending tran_id).
        _dalytran.StartBrowse();                          // source: CBTRN01C.cbl:254
        _dalytranStatus = FileStatus.Ok;                  // a present table is always openable -> '00'

        if (_dalytranStatus == FileStatus.Ok)             // DALYTRAN-STATUS = '00'  // source: CBTRN01C.cbl:255
            _applResult = 0;                              // source: CBTRN01C.cbl:256
        else
            _applResult = 12;                             // source: CBTRN01C.cbl:258

        if (ApplAok) { /* CONTINUE */ }                   // source: CBTRN01C.cbl:260-261
        else
        {
            Display("ERROR OPENING DAILY TRANSACTION FILE"); // source: CBTRN01C.cbl:263
            _ioStatus = _dalytranStatus;                     // MOVE DALYTRAN-STATUS TO IO-STATUS  // source: CBTRN01C.cbl:264
            DisplayIoStatusZ();                              // source: CBTRN01C.cbl:265
            AbendProgramZ();                                 // source: CBTRN01C.cbl:266
        }
        // EXIT  // source: CBTRN01C.cbl:268
    }

    // *---------------------------------------------------------------*
    /// <summary>0100-CUSTFILE-OPEN — OPEN INPUT the customer file (opened but never read — bug #1). // source: CBTRN01C.cbl:271-287</summary>
    private void CustfileOpen0100()
    {
        _applResult = 8;                                  // MOVE 8 TO APPL-RESULT  // source: CBTRN01C.cbl:272
        // OPEN INPUT CUSTOMER-FILE -> open a CUSTOMER handle; NO query is ever issued against it (bug #1).
        _ = _custfile;                                    // source: CBTRN01C.cbl:273
        _custfileStatus = FileStatus.Ok;

        if (_custfileStatus == FileStatus.Ok)             // CUSTFILE-STATUS = '00'  // source: CBTRN01C.cbl:274
            _applResult = 0;                              // source: CBTRN01C.cbl:275
        else
            _applResult = 12;                             // source: CBTRN01C.cbl:277

        if (ApplAok) { /* CONTINUE */ }                   // source: CBTRN01C.cbl:279-280
        else
        {
            Display("ERROR OPENING CUSTOMER FILE");       // source: CBTRN01C.cbl:282
            _ioStatus = _custfileStatus;                  // MOVE CUSTFILE-STATUS TO IO-STATUS  // source: CBTRN01C.cbl:283
            DisplayIoStatusZ();                           // source: CBTRN01C.cbl:284
            AbendProgramZ();                              // source: CBTRN01C.cbl:285
        }
        // EXIT  // source: CBTRN01C.cbl:287
    }

    // *---------------------------------------------------------------*
    /// <summary>0200-XREFFILE-OPEN — OPEN INPUT the cross-reference file. // source: CBTRN01C.cbl:289-305</summary>
    private void XreffileOpen0200()
    {
        _applResult = 8;                                  // MOVE 8 TO APPL-RESULT  // source: CBTRN01C.cbl:290
        // OPEN INPUT XREF-FILE -> open the CARD_XREF handle for random keyed reads.
        _ = _xreffile;                                    // source: CBTRN01C.cbl:291
        _xreffileStatus = FileStatus.Ok;

        if (_xreffileStatus == FileStatus.Ok)             // XREFFILE-STATUS = '00'  // source: CBTRN01C.cbl:292
            _applResult = 0;                              // source: CBTRN01C.cbl:293
        else
            _applResult = 12;                             // source: CBTRN01C.cbl:295

        if (ApplAok) { /* CONTINUE */ }                   // source: CBTRN01C.cbl:297-298
        else
        {
            Display("ERROR OPENING CROSS REF FILE");      // source: CBTRN01C.cbl:300
            _ioStatus = _xreffileStatus;                  // MOVE XREFFILE-STATUS TO IO-STATUS  // source: CBTRN01C.cbl:301
            DisplayIoStatusZ();                           // source: CBTRN01C.cbl:302
            AbendProgramZ();                              // source: CBTRN01C.cbl:303
        }
        // EXIT  // source: CBTRN01C.cbl:305
    }

    // *---------------------------------------------------------------*
    /// <summary>0300-CARDFILE-OPEN — OPEN INPUT the card file (opened but never read — bug #1). // source: CBTRN01C.cbl:307-323</summary>
    private void CardfileOpen0300()
    {
        _applResult = 8;                                  // MOVE 8 TO APPL-RESULT  // source: CBTRN01C.cbl:308
        // OPEN INPUT CARD-FILE -> open a CARD handle; NO query is ever issued against it (bug #1).
        _ = _cardfile;                                    // source: CBTRN01C.cbl:309
        _cardfileStatus = FileStatus.Ok;

        if (_cardfileStatus == FileStatus.Ok)             // CARDFILE-STATUS = '00'  // source: CBTRN01C.cbl:310
            _applResult = 0;                              // source: CBTRN01C.cbl:311
        else
            _applResult = 12;                             // source: CBTRN01C.cbl:313

        if (ApplAok) { /* CONTINUE */ }                   // source: CBTRN01C.cbl:315-316
        else
        {
            Display("ERROR OPENING CARD FILE");           // source: CBTRN01C.cbl:318
            _ioStatus = _cardfileStatus;                  // MOVE CARDFILE-STATUS TO IO-STATUS  // source: CBTRN01C.cbl:319
            DisplayIoStatusZ();                           // source: CBTRN01C.cbl:320
            AbendProgramZ();                              // source: CBTRN01C.cbl:321
        }
        // EXIT  // source: CBTRN01C.cbl:323
    }

    // *---------------------------------------------------------------*
    /// <summary>0400-ACCTFILE-OPEN — OPEN INPUT the account file. // source: CBTRN01C.cbl:325-341</summary>
    private void AcctfileOpen0400()
    {
        _applResult = 8;                                  // MOVE 8 TO APPL-RESULT  // source: CBTRN01C.cbl:326
        // OPEN INPUT ACCOUNT-FILE -> open the ACCOUNT handle for random keyed reads.
        _ = _acctfile;                                    // source: CBTRN01C.cbl:327
        _acctfileStatus = FileStatus.Ok;

        if (_acctfileStatus == FileStatus.Ok)             // ACCTFILE-STATUS = '00'  // source: CBTRN01C.cbl:328
            _applResult = 0;                              // source: CBTRN01C.cbl:329
        else
            _applResult = 12;                             // source: CBTRN01C.cbl:331

        if (ApplAok) { /* CONTINUE */ }                   // source: CBTRN01C.cbl:333-334
        else
        {
            Display("ERROR OPENING ACCOUNT FILE");        // source: CBTRN01C.cbl:336
            _ioStatus = _acctfileStatus;                  // MOVE ACCTFILE-STATUS TO IO-STATUS  // source: CBTRN01C.cbl:337
            DisplayIoStatusZ();                           // source: CBTRN01C.cbl:338
            AbendProgramZ();                              // source: CBTRN01C.cbl:339
        }
        // EXIT  // source: CBTRN01C.cbl:341
    }

    // *---------------------------------------------------------------*
    /// <summary>0500-TRANFILE-OPEN — OPEN INPUT the transaction file (opened but never read/written — bug #1). // source: CBTRN01C.cbl:343-359</summary>
    private void TranfileOpen0500()
    {
        _applResult = 8;                                  // MOVE 8 TO APPL-RESULT  // source: CBTRN01C.cbl:344
        // OPEN INPUT TRANSACT-FILE -> open a TRANSACTION handle; NO query is ever issued against it (bug #1).
        _ = _tranfile;                                    // source: CBTRN01C.cbl:345
        _tranfileStatus = FileStatus.Ok;

        if (_tranfileStatus == FileStatus.Ok)             // TRANFILE-STATUS = '00'  // source: CBTRN01C.cbl:346
            _applResult = 0;                              // source: CBTRN01C.cbl:347
        else
            _applResult = 12;                             // source: CBTRN01C.cbl:349

        if (ApplAok) { /* CONTINUE */ }                   // source: CBTRN01C.cbl:351-352
        else
        {
            Display("ERROR OPENING TRANSACTION FILE");    // source: CBTRN01C.cbl:354
            _ioStatus = _tranfileStatus;                  // MOVE TRANFILE-STATUS TO IO-STATUS  // source: CBTRN01C.cbl:355
            DisplayIoStatusZ();                           // source: CBTRN01C.cbl:356
            AbendProgramZ();                              // source: CBTRN01C.cbl:357
        }
        // EXIT  // source: CBTRN01C.cbl:359
    }

    // *---------------------------------------------------------------*
    /// <summary>
    /// 9000-DALYTRAN-CLOSE — CLOSE the daily-transaction file. // source: CBTRN01C.cbl:361-377
    /// FAITHFUL BUGS #3/#4: on a close error it DISPLAYs 'ERROR CLOSING CUSTOMER FILE' and moves
    /// CUSTFILE-STATUS (not DALYTRAN-STATUS) into IO-STATUS.
    /// </summary>
    private void DalytranClose9000()
    {
        _applResult = 0 + 8;                              // ADD 8 TO ZERO GIVING APPL-RESULT (-> 8) (bug #5 style)  // source: CBTRN01C.cbl:362
        // CLOSE DALYTRAN-FILE -> end the DAILY_TRANSACTION cursor.
        _dalytran.EndBrowse();                            // source: CBTRN01C.cbl:363
        _dalytranStatus = FileStatus.Ok;

        if (_dalytranStatus == FileStatus.Ok)             // DALYTRAN-STATUS = '00'  // source: CBTRN01C.cbl:364
            _applResult = 0;                              // source: CBTRN01C.cbl:365
        else
            _applResult = 12;                             // source: CBTRN01C.cbl:367

        if (ApplAok) { /* CONTINUE */ }                   // source: CBTRN01C.cbl:369-370
        else
        {
            // FAITHFUL BUG #3: wrong literal (copy-paste): says CUSTOMER, not DAILY TRANSACTION.
            Display("ERROR CLOSING CUSTOMER FILE");       // source: CBTRN01C.cbl:372
            // FAITHFUL BUG #4: moves CUSTFILE-STATUS, not DALYTRAN-STATUS, into IO-STATUS.
            _ioStatus = _custfileStatus;                  // MOVE CUSTFILE-STATUS TO IO-STATUS  // source: CBTRN01C.cbl:373
            DisplayIoStatusZ();                           // source: CBTRN01C.cbl:374
            AbendProgramZ();                              // source: CBTRN01C.cbl:375
        }
        // EXIT  // source: CBTRN01C.cbl:377
    }

    // *---------------------------------------------------------------*
    /// <summary>9100-CUSTFILE-CLOSE — CLOSE the customer file. // source: CBTRN01C.cbl:379-395</summary>
    private void CustfileClose9100()
    {
        _applResult = 0 + 8;                              // ADD 8 TO ZERO GIVING APPL-RESULT  // source: CBTRN01C.cbl:380
        _ = _custfile;                                    // CLOSE CUSTOMER-FILE  // source: CBTRN01C.cbl:381
        _custfileStatus = FileStatus.Ok;

        if (_custfileStatus == FileStatus.Ok)             // CUSTFILE-STATUS = '00'  // source: CBTRN01C.cbl:382
            _applResult = 0;                              // source: CBTRN01C.cbl:383
        else
            _applResult = 12;                             // source: CBTRN01C.cbl:385

        if (ApplAok) { /* CONTINUE */ }                   // source: CBTRN01C.cbl:387-388
        else
        {
            Display("ERROR CLOSING CUSTOMER FILE");       // source: CBTRN01C.cbl:390
            _ioStatus = _custfileStatus;                  // MOVE CUSTFILE-STATUS TO IO-STATUS  // source: CBTRN01C.cbl:391
            DisplayIoStatusZ();                           // source: CBTRN01C.cbl:392
            AbendProgramZ();                              // source: CBTRN01C.cbl:393
        }
        // EXIT  // source: CBTRN01C.cbl:395
    }

    // *---------------------------------------------------------------*
    /// <summary>9200-XREFFILE-CLOSE — CLOSE the cross-reference file. // source: CBTRN01C.cbl:397-413</summary>
    private void XreffileClose9200()
    {
        _applResult = 0 + 8;                              // ADD 8 TO ZERO GIVING APPL-RESULT  // source: CBTRN01C.cbl:398
        _ = _xreffile;                                    // CLOSE XREF-FILE  // source: CBTRN01C.cbl:399
        _xreffileStatus = FileStatus.Ok;

        if (_xreffileStatus == FileStatus.Ok)             // XREFFILE-STATUS = '00'  // source: CBTRN01C.cbl:400
            _applResult = 0;                              // source: CBTRN01C.cbl:401
        else
            _applResult = 12;                             // source: CBTRN01C.cbl:403

        if (ApplAok) { /* CONTINUE */ }                   // source: CBTRN01C.cbl:405-406
        else
        {
            Display("ERROR CLOSING CROSS REF FILE");      // source: CBTRN01C.cbl:408
            _ioStatus = _xreffileStatus;                  // MOVE XREFFILE-STATUS TO IO-STATUS  // source: CBTRN01C.cbl:409
            DisplayIoStatusZ();                           // source: CBTRN01C.cbl:410
            AbendProgramZ();                              // source: CBTRN01C.cbl:411
        }
        // EXIT  // source: CBTRN01C.cbl:413
    }

    // *---------------------------------------------------------------*
    /// <summary>9300-CARDFILE-CLOSE — CLOSE the card file. // source: CBTRN01C.cbl:415-431</summary>
    private void CardfileClose9300()
    {
        _applResult = 0 + 8;                              // ADD 8 TO ZERO GIVING APPL-RESULT  // source: CBTRN01C.cbl:416
        _ = _cardfile;                                    // CLOSE CARD-FILE  // source: CBTRN01C.cbl:417
        _cardfileStatus = FileStatus.Ok;

        if (_cardfileStatus == FileStatus.Ok)             // CARDFILE-STATUS = '00'  // source: CBTRN01C.cbl:418
            _applResult = 0;                              // source: CBTRN01C.cbl:419
        else
            _applResult = 12;                             // source: CBTRN01C.cbl:421

        if (ApplAok) { /* CONTINUE */ }                   // source: CBTRN01C.cbl:423-424
        else
        {
            Display("ERROR CLOSING CARD FILE");           // source: CBTRN01C.cbl:426
            _ioStatus = _cardfileStatus;                  // MOVE CARDFILE-STATUS TO IO-STATUS  // source: CBTRN01C.cbl:427
            DisplayIoStatusZ();                           // source: CBTRN01C.cbl:428
            AbendProgramZ();                              // source: CBTRN01C.cbl:429
        }
        // EXIT  // source: CBTRN01C.cbl:431
    }

    // *---------------------------------------------------------------*
    /// <summary>9400-ACCTFILE-CLOSE — CLOSE the account file. // source: CBTRN01C.cbl:433-449</summary>
    private void AcctfileClose9400()
    {
        _applResult = 0 + 8;                              // ADD 8 TO ZERO GIVING APPL-RESULT  // source: CBTRN01C.cbl:434
        _ = _acctfile;                                    // CLOSE ACCOUNT-FILE  // source: CBTRN01C.cbl:435
        _acctfileStatus = FileStatus.Ok;

        if (_acctfileStatus == FileStatus.Ok)             // ACCTFILE-STATUS = '00'  // source: CBTRN01C.cbl:436
            _applResult = 0;                              // source: CBTRN01C.cbl:437
        else
            _applResult = 12;                             // source: CBTRN01C.cbl:439

        if (ApplAok) { /* CONTINUE */ }                   // source: CBTRN01C.cbl:441-442
        else
        {
            Display("ERROR CLOSING ACCOUNT FILE");        // source: CBTRN01C.cbl:444
            _ioStatus = _acctfileStatus;                  // MOVE ACCTFILE-STATUS TO IO-STATUS  // source: CBTRN01C.cbl:445
            DisplayIoStatusZ();                           // source: CBTRN01C.cbl:446
            AbendProgramZ();                              // source: CBTRN01C.cbl:447
        }
        // EXIT  // source: CBTRN01C.cbl:449
    }

    // *---------------------------------------------------------------*
    /// <summary>9500-TRANFILE-CLOSE — CLOSE the transaction file. // source: CBTRN01C.cbl:451-467</summary>
    private void TranfileClose9500()
    {
        _applResult = 0 + 8;                              // ADD 8 TO ZERO GIVING APPL-RESULT  // source: CBTRN01C.cbl:452
        _ = _tranfile;                                    // CLOSE TRANSACT-FILE  // source: CBTRN01C.cbl:453
        _tranfileStatus = FileStatus.Ok;

        if (_tranfileStatus == FileStatus.Ok)             // TRANFILE-STATUS = '00'  // source: CBTRN01C.cbl:454
            _applResult = 0;                              // source: CBTRN01C.cbl:455
        else
            _applResult = 12;                             // source: CBTRN01C.cbl:457

        if (ApplAok) { /* CONTINUE */ }                   // source: CBTRN01C.cbl:459-460
        else
        {
            Display("ERROR CLOSING TRANSACTION FILE");    // source: CBTRN01C.cbl:462
            _ioStatus = _tranfileStatus;                  // MOVE TRANFILE-STATUS TO IO-STATUS  // source: CBTRN01C.cbl:463
            DisplayIoStatusZ();                           // source: CBTRN01C.cbl:464
            AbendProgramZ();                              // source: CBTRN01C.cbl:465
        }
        // EXIT  // source: CBTRN01C.cbl:467
    }

    // *---------------------------------------------------------------*
    /// <summary>
    /// Z-ABEND-PROGRAM — DISPLAY 'ABENDING PROGRAM'; MOVE 0 TO TIMING; MOVE 999 TO ABCODE;
    /// CALL 'CEE3ABD' USING ABCODE, TIMING. Maps to a 999 abend (no graceful return). // source: CBTRN01C.cbl:469-473
    /// </summary>
    private void AbendProgramZ()
    {
        Display("ABENDING PROGRAM"); // source: CBTRN01C.cbl:470
        // MOVE 0 TO TIMING; MOVE 999 TO ABCODE; CALL 'CEE3ABD' USING ABCODE, TIMING.  // source: CBTRN01C.cbl:471-473
        throw new AbendException("999", $"CBTRN01C abend (CEE3ABD); FILE STATUS '{_ioStatus}'.");
    }

    // *****************************************************************
    /// <summary>
    /// Z-DISPLAY-IO-STATUS — formats the 2-byte file status into a 4-char "NNNN" string and DISPLAYs
    /// <c>'FILE STATUS IS: NNNN' IO-STATUS-04</c>. // source: CBTRN01C.cbl:476-489
    /// </summary>
    private void DisplayIoStatusZ()
    {
        // IO-STATUS-04 = IO-STATUS-0401 PIC 9 + IO-STATUS-0403 PIC 999  // source: CBTRN01C.cbl:138-140
        string s = _ioStatus.Length >= 2 ? _ioStatus[..2] : _ioStatus.PadRight(2);
        char ioStat1 = s[0];
        char ioStat2 = s[1];

        string ioStatus04;

        // IF IO-STATUS NOT NUMERIC OR IO-STAT1 = '9'  // source: CBTRN01C.cbl:477-478
        if (!IsNumeric(s) || ioStat1 == '9')
        {
            // MOVE IO-STAT1 TO IO-STATUS-04(1:1)  // source: CBTRN01C.cbl:479
            char pos1 = ioStat1;

            // MOVE 0 TO TWO-BYTES-BINARY; MOVE IO-STAT2 TO TWO-BYTES-RIGHT;
            // MOVE TWO-BYTES-BINARY TO IO-STATUS-0403.  // source: CBTRN01C.cbl:480-482
            // FAITHFUL BUG #7: TWO-BYTES-BINARY is a PIC 9(4) BINARY (big-endian halfword) redefined as two
            // bytes; only the low (right) byte is set from IO-STAT2, so reading the halfword yields that
            // character's host code point (e.g. EBCDIC '0' (0xF0) -> 240). Reproduce big-endian.
            int twoBytesBinary = HostEncoding.For(_host).GetBytes(ioStat2.ToString())[0]; // value == low byte
            int ioStatus0403 = twoBytesBinary % 1000;            // store into PIC 999 (3 digits)
            ioStatus04 = pos1.ToString() + ioStatus0403.ToString("D3"); // source: CBTRN01C.cbl:479-482
        }
        else
        {
            // MOVE '0000' TO IO-STATUS-04; MOVE IO-STATUS TO IO-STATUS-04(3:2).  // source: CBTRN01C.cbl:485-486
            ioStatus04 = "00" + s;
        }

        // DISPLAY 'FILE STATUS IS: NNNN' IO-STATUS-04  // source: CBTRN01C.cbl:483,487
        Display("FILE STATUS IS: NNNN" + ioStatus04);
        // EXIT  // source: CBTRN01C.cbl:489
    }

    // =================================================================================================
    // Helpers
    // =================================================================================================

    /// <summary>
    /// Reproduces <c>DISPLAY DALYTRAN-RECORD</c>: the 350-byte CVTRA06Y logical image rendered as host
    /// text — every elementary field in copybook order then a 20-byte FILLER of spaces. Numeric
    /// (USAGE DISPLAY) fields are zoned (CAT-CD/MERCHANT-ID unsigned; AMT signed with the sign overpunched
    /// on the last digit, no decimal point); alphanumerics are left-justified, space-padded.
    /// // source: CBTRN01C.cbl:168; cpy/CVTRA06Y.cpy:4-18
    /// </summary>
    private string DisplayDalytranRecord()
    {
        DailyTransaction t = _dalytranRecord;
        var w = new RecordWriter(_host);
        w.Alpha(t.TranId, 16);                   // DALYTRAN-ID            PIC X(16)
        w.Alpha(t.TypeCd, 2);                    // DALYTRAN-TYPE-CD       PIC X(02)
        w.Zoned(t.CatCd, 4, 0, signed: false);   // DALYTRAN-CAT-CD        PIC 9(04)
        w.Alpha(t.Source, 10);                   // DALYTRAN-SOURCE        PIC X(10)
        w.Alpha(t.Desc, 100);                    // DALYTRAN-DESC          PIC X(100)
        w.Zoned(t.Amt, 11, 2, signed: true);     // DALYTRAN-AMT           PIC S9(09)V99
        w.Zoned(t.MerchantId, 9, 0, signed: false); // DALYTRAN-MERCHANT-ID PIC 9(09)
        w.Alpha(t.MerchantName, 50);             // DALYTRAN-MERCHANT-NAME PIC X(50)
        w.Alpha(t.MerchantCity, 50);             // DALYTRAN-MERCHANT-CITY PIC X(50)
        w.Alpha(t.MerchantZip, 10);              // DALYTRAN-MERCHANT-ZIP  PIC X(10)
        w.Alpha(t.CardNum, 16);                  // DALYTRAN-CARD-NUM      PIC X(16)
        w.Alpha(t.OrigTs, 26);                   // DALYTRAN-ORIG-TS       PIC X(26)
        w.Alpha(t.ProcTs, 26);                   // DALYTRAN-PROC-TS       PIC X(26)
        w.Alpha("", 20);                         // FILLER                 PIC X(20)
        return HostEncoding.For(_host).GetString(w.ToArray(350));
    }

    /// <summary>DISPLAY -&gt; SYSOUT: collect the line and (optionally) append it to the flat report file.</summary>
    private void Display(string line)
    {
        _sysout.Add(line);
        _writer?.WriteLine(line);
    }

    /// <summary>COBOL alphanumeric MOVE / DISPLAY of a PIC X(width): left-justify, space-pad / right-truncate.</summary>
    private static string Alpha(string? text, int width)
    {
        text ??= "";
        return text.Length >= width ? text[..width] : text.PadRight(width, ' ');
    }

    /// <summary>
    /// Render a numeric value as COBOL DISPLAY of an unsigned PIC 9(width) would: zero-padded digit
    /// string, low-order digits kept on overflow (matches the zoned codec's silent overflow).
    /// </summary>
    private string Zoned(long value, int width) => Zoned((decimal)value, width, 0, signed: false);

    /// <summary>
    /// Render a numeric value as COBOL DISPLAY of a (possibly signed) zoned PIC would: the codec encodes
    /// truncate-toward-zero, silent high-order overflow, and the trailing sign overpunch; the bytes are
    /// then host-decoded to text. No decimal point is emitted (implied V).
    /// </summary>
    private string Zoned(decimal value, int totalDigits, int scale, bool signed)
    {
        var field = new byte[totalDigits];
        ZonedDecimalCodec.Encode(value, field, totalDigits, scale, signed, _host);
        return HostEncoding.For(_host).GetString(field);
    }

    /// <summary>COBOL "class NUMERIC" test for a 2-char IO-STATUS (every char is a decimal digit).</summary>
    private static bool IsNumeric(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (char ch in s)
            if (ch < '0' || ch > '9') return false;
        return true;
    }

    /// <summary>Builds a fixed-width record image (zoned / alphanumeric fields) for DISPLAY DALYTRAN-RECORD.</summary>
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

        public void Alpha(string? text, int width)
        {
            text ??= "";
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
}
