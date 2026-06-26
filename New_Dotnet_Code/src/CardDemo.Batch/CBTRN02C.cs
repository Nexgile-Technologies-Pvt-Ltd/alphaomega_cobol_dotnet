using CardDemo.Cobol.Runtime;
using CardDemo.Data;
using CardDemo.Domain;

namespace CardDemo.Batch;

/// <summary>
/// Faithful relational re-port of the batch program <c>CBTRN02C</c> (daily transaction posting). It reads
/// the <b>DAILY_TRANSACTION</b> table sequentially (the QSAM DALYTRAN input), and for every record (a)
/// validates it — card cross-reference lookup, then account load + credit-limit / expiration checks; (b) if
/// valid, posts it: updates the category balance (creating the row when absent), updates the account
/// current balance + cycle credit/debit, and inserts the transaction into the <b>TRANSACTION</b> table; (c)
/// if invalid, increments the reject counter and writes the original 350-byte daily-tran image plus an
/// 80-byte validation trailer (reason code + description) to the daily rejects file. At end of run it
/// reports processed/rejected counts and sets RETURN-CODE 4 if any record was rejected.
/// </summary>
/// <remarks>
/// <para>Ported paragraph-by-paragraph from <c>app/cbl/CBTRN02C.cbl</c> (method names mirror the COBOL
/// paragraph names; each carries a <c>// source: CBTRN02C.cbl:NNN</c> citation, and statement order /
/// PERFORM flow is preserved). Per <c>_design/ARCHITECTURE.md</c> the six VSAM/QSAM files map as follows:
/// <list type="bullet">
///   <item>DALYTRAN-FILE (OPEN INPUT, sequential) -&gt; <see cref="DailyTransactionRepository"/> browse cursor.</item>
///   <item>XREF-FILE (OPEN INPUT, random) -&gt; <see cref="CardXrefRepository.ReadByKey"/> ('00'/'23').</item>
///   <item>ACCOUNT-FILE (OPEN I-O, random) -&gt; <see cref="AccountRepository.ReadByKey"/> + <see cref="AccountRepository.Update"/>.</item>
///   <item>TCATBAL-FILE (OPEN I-O, random) -&gt; <see cref="TranCatBalanceRepository"/> read / insert / update (create-on-'23').</item>
///   <item>TRANSACT-FILE (OPEN OUTPUT) -&gt; <see cref="TransactionRepository"/> truncate-then-insert.</item>
///   <item>DALYREJS-FILE (OPEN OUTPUT, sequential) -&gt; a flat 430-byte fixed-width reject dataset.</item>
/// </list></para>
/// <para>Money math uses <see cref="Decimals"/> (truncate toward zero, silent high-order overflow, never
/// rounded), matching COBOL COMPUTE/ADD with no ROUNDED and no ON SIZE ERROR.</para>
/// <para>FAITHFUL BUGS reproduced verbatim (see <c>_design/specs/CBTRN02C.md</c> §6 / <c>faithful-bugs.md</c>):
/// <list type="number">
/// <item>Validation reason 102 (overlimit) can be overwritten by 103 (expired): the two checks are
/// independent sequential IFs with no ELSE/short-circuit, so when a record is both over-limit and after
/// expiration only 103 is reported. // source: CBTRN02C.cbl:407-420</item>
/// <item>Posting side effects happen before the account-REWRITE INVALID-KEY check: TCATBAL is updated and
/// the transaction is still WRITTEN even if the account UPDATE fails (reason 109 is set but never
/// re-checked, so no reject is written and the count is not bumped). // source: CBTRN02C.cbl:424-444,545-559</item>
/// <item>A negative DALYTRAN-AMT is ADDed (not negated) into ACCT-CURR-CYC-DEBIT, so the debit bucket moves
/// negative. // source: CBTRN02C.cbl:548-552</item>
/// <item>The credit-limit test uses cycle credit/debit (ACCT-CURR-CYC-CREDIT - ACCT-CURR-CYC-DEBIT +
/// DALYTRAN-AMT), not ACCT-CURR-BAL. // source: CBTRN02C.cbl:403-405</item>
/// <item>WS-TEMP-BAL is S9(9)V99 while the cycle fields are S9(10)V99, so the COMPUTE result is truncated
/// to 9 integer digits (toward zero, silent overflow). // source: CBTRN02C.cbl:187,403-405</item>
/// <item>9300-DALYREJS-CLOSE displays XREFFILE-STATUS instead of DALYREJS-STATUS on a close error
/// (copy/paste). // source: CBTRN02C.cbl:637-652</item>
/// <item>TRAN-PROC-TS is always the current-run DB2 timestamp, discarding DALYTRAN-PROC-TS. // source: CBTRN02C.cbl:437-438</item>
/// </list></para>
/// </remarks>
public sealed class Cbtrn02c
{
    // PIC widths/scales reused throughout the posting math.
    private const int DalyAmtDigits = 9;   // DALYTRAN-AMT / TRAN-CAT-BAL S9(09)V99 -> 9 integer + 2 frac
    private const int CycleDigits = 10;     // ACCT-CURR-BAL / cycle fields S9(10)V99 -> 10 integer + 2 frac
    private const int TempBalDigits = 9;    // WS-TEMP-BAL S9(09)V99 -> 9 integer + 2 frac (truncation bug)
    private const int MoneyScale = 2;

    // ---- The six "files" (relational tables + one flat reject dataset) -------------------------------
    private DailyTransactionRepository _dalyTran = null!;  // DALYTRAN-FILE  (OPEN INPUT, sequential)
    private TransactionRepository _transact = null!;       // TRANSACT-FILE  (OPEN OUTPUT)
    private CardXrefRepository _xref = null!;              // XREF-FILE      (OPEN INPUT, random)
    private AccountRepository _account = null!;            // ACCOUNT-FILE   (OPEN I-O, random)
    private TranCatBalanceRepository _tcatBal = null!;     // TCATBAL-FILE   (OPEN I-O, random)
    private FixedFileWriter _dalyRejs = null!;             // DALYREJS-FILE  (OPEN OUTPUT, sequential)

    private IClock _clock = null!;
    private HostKind _host;

    // ---- WORKING-STORAGE (CBTRN02C lines 99-190) ----------------------------------------------------
    private string _dalytranStatus = "00"; // DALYTRAN-STATUS
    private string _tcatbalfStatus = "00"; // TCATBALF-STATUS
    private int _applResult;               // APPL-RESULT S9(9) COMP (88 APPL-AOK=0, APPL-EOF=16)
    private bool _endOfFile;               // END-OF-FILE X(1) VALUE 'N'

    // WS-VALIDATION-TRAILER (lines 180-182): reason 9(4) + desc X(76).
    private int _wsValidationFailReason;            // WS-VALIDATION-FAIL-REASON 9(4)
    private string _wsValidationFailReasonDesc = new(' ', 76); // WS-VALIDATION-FAIL-REASON-DESC X(76)

    // WS-COUNTERS (lines 184-187).
    private long _wsTransactionCount;      // WS-TRANSACTION-COUNT 9(9)
    private long _wsRejectCount;           // WS-REJECT-COUNT 9(9)

    // WS-FLAGS (lines 189-190).
    private bool _wsCreateTrancatRec;      // WS-CREATE-TRANCAT-REC X(1) VALUE 'N'

    // The records READ ... INTO populate (null before a successful read).
    private DailyTransaction _dalytranRecord = null!; // DALYTRAN-RECORD (CVTRA06Y)
    private CardXref? _cardXrefRecord;                 // CARD-XREF-RECORD (CVACT03Y)
    private Account? _accountRecord;                   // ACCOUNT-RECORD (CVACT01Y)
    private TranCatBalance? _tranCatBalRecord;         // TRAN-CAT-BAL-RECORD (CVTRA01Y)

    // TRAN-RECORD (CVTRA05Y) assembled in 2000-POST-TRANSACTION before the writes.
    private Transaction _tranRecord = null!;

    private readonly List<string> _sysout = [];

    /// <summary>88 APPL-AOK VALUE 0. // source: CBTRN02C.cbl:143</summary>
    private bool ApplAok => _applResult == 0;

    /// <summary>88 APPL-EOF VALUE 16. // source: CBTRN02C.cbl:144</summary>
    private bool ApplEof => _applResult == 16;

    /// <summary>The SYSOUT (DISPLAY) lines produced by the run, in order.</summary>
    public IReadOnlyList<string> Sysout => _sysout;

    /// <summary>Transactions read from the daily file (WS-TRANSACTION-COUNT at end of run).</summary>
    public long TransactionsProcessed => _wsTransactionCount;

    /// <summary>Transactions rejected (WS-REJECT-COUNT at end of run).</summary>
    public long TransactionsRejected => _wsRejectCount;

    /// <summary>
    /// Runs CBTRN02C over the relational <paramref name="db"/>, writing rejects to
    /// <paramref name="dalyRejsPath"/> (a 430-byte fixed-width dataset). Returns the process RETURN-CODE
    /// (4 if any record was rejected, otherwise 0). // source: CBTRN02C.cbl:229-231
    /// </summary>
    /// <param name="db">The relational database holding all six logical files as tables.</param>
    /// <param name="dalyRejsPath">DALYREJS output dataset path (RECFM=F LRECL=430).</param>
    /// <param name="clock">Clock for the DB2 processing timestamp (defaults to wall clock).</param>
    /// <param name="host">Host encoding for the reject dataset (defaults to EBCDIC, the mainframe form).</param>
    public int Run(RelationalDb db, string dalyRejsPath, IClock? clock = null, HostKind host = HostKind.Ebcdic)
        => Run(
            new DailyTransactionRepository(db),
            new TransactionRepository(db),
            new CardXrefRepository(db),
            new AccountRepository(db),
            new TranCatBalanceRepository(db),
            dalyRejsPath, clock, host);

    /// <summary>Convenience overload taking a <see cref="BatchSupport"/> (uses its repositories).</summary>
    public int Run(BatchSupport support, string dalyRejsPath, IClock? clock = null, HostKind host = HostKind.Ebcdic)
        => Run(
            support.DailyTransaction,
            support.Transaction,
            support.CardXref,
            support.Account,
            support.TranCatBalance,
            dalyRejsPath, clock, host);

    /// <summary>Runs CBTRN02C over already-resolved repositories and a reject output path.</summary>
    public int Run(
        DailyTransactionRepository dalyTran,
        TransactionRepository transact,
        CardXrefRepository xref,
        AccountRepository account,
        TranCatBalanceRepository tcatBal,
        string dalyRejsPath,
        IClock? clock = null,
        HostKind host = HostKind.Ebcdic)
    {
        _dalyTran = dalyTran;
        _transact = transact;
        _xref = xref;
        _account = account;
        _tcatBal = tcatBal;
        _clock = clock ?? SystemClock.Instance;
        _host = host;

        // OPEN OUTPUT DALYREJS (DISP=NEW per the JCL GDG +1): start from an empty dataset.
        DeleteIfExists(dalyRejsPath);
        _dalyRejs = BatchSupport.OpenWriter(dalyRejsPath, host);

        try
        {
            return Execute();
        }
        finally
        {
            _dalyRejs.Flush();
            _dalyRejs.Dispose();
        }
    }

    // =================================================================================================
    // MAIN (unnamed PROCEDURE DIVISION body) // source: CBTRN02C.cbl:193-234
    // =================================================================================================
    private int Execute()
    {
        _sysout.Add("START OF EXECUTION OF PROGRAM CBTRN02C");   // source: CBTRN02C.cbl:194
        DalytranOpen0000();                                      // source: CBTRN02C.cbl:195
        TranfileOpen0100();                                      // source: CBTRN02C.cbl:196
        XreffileOpen0200();                                      // source: CBTRN02C.cbl:197
        DalyrejsOpen0300();                                      // source: CBTRN02C.cbl:198
        AcctfileOpen0400();                                      // source: CBTRN02C.cbl:199
        TcatbalfOpen0500();                                      // source: CBTRN02C.cbl:200

        // PERFORM UNTIL END-OF-FILE = 'Y' // source: CBTRN02C.cbl:202-219
        while (!_endOfFile)
        {
            if (!_endOfFile)                                     // IF END-OF-FILE = 'N' // source: CBTRN02C.cbl:203
            {
                DalytranGetNext1000();                          // source: CBTRN02C.cbl:204
                if (!_endOfFile)                                // IF END-OF-FILE = 'N' // source: CBTRN02C.cbl:205
                {
                    _wsTransactionCount++;                      // ADD 1 TO WS-TRANSACTION-COUNT // source: CBTRN02C.cbl:206
                    _wsValidationFailReason = 0;               // MOVE 0 TO WS-VALIDATION-FAIL-REASON // source: CBTRN02C.cbl:208
                    _wsValidationFailReasonDesc = new string(' ', 76); // MOVE SPACES TO ...-DESC // source: CBTRN02C.cbl:209
                    ValidateTran1500();                        // source: CBTRN02C.cbl:210

                    if (_wsValidationFailReason == 0)          // source: CBTRN02C.cbl:211
                    {
                        PostTransaction2000();                 // source: CBTRN02C.cbl:212
                    }
                    else
                    {
                        _wsRejectCount++;                      // ADD 1 TO WS-REJECT-COUNT // source: CBTRN02C.cbl:214
                        WriteRejectRec2500();                  // source: CBTRN02C.cbl:215
                    }
                }
            }
        }

        DalytranClose9000();                                    // source: CBTRN02C.cbl:221
        TranfileClose9100();                                    // source: CBTRN02C.cbl:222
        XreffileClose9200();                                    // source: CBTRN02C.cbl:223
        DalyrejsClose9300();                                    // source: CBTRN02C.cbl:224
        AcctfileClose9400();                                    // source: CBTRN02C.cbl:225
        TcatbalfClose9500();                                    // source: CBTRN02C.cbl:226

        _sysout.Add("TRANSACTIONS PROCESSED :" + _wsTransactionCount.ToString("D9")); // source: CBTRN02C.cbl:227
        _sysout.Add("TRANSACTIONS REJECTED  :" + _wsRejectCount.ToString("D9"));      // source: CBTRN02C.cbl:228

        int returnCode = 0;
        if (_wsRejectCount > 0)                                 // source: CBTRN02C.cbl:229
            returnCode = 4;                                     // MOVE 4 TO RETURN-CODE // source: CBTRN02C.cbl:230

        _sysout.Add("END OF EXECUTION OF PROGRAM CBTRN02C");    // source: CBTRN02C.cbl:232
        return returnCode;                                      // GOBACK // source: CBTRN02C.cbl:234
    }

    // -------------------------------------------------------------------------------------------------
    // 0000-DALYTRAN-OPEN // source: CBTRN02C.cbl:236-252
    private void DalytranOpen0000()
    {
        _applResult = 8;                                        // source: CBTRN02C.cbl:237
        // OPEN INPUT DALYTRAN-FILE -> position the sequential read cursor at the first row (tran_id order).
        _dalyTran.StartBrowse();
        _dalytranStatus = FileStatus.Ok;                        // a present table always opens '00'.
        if (_dalytranStatus == FileStatus.Ok) _applResult = 0; else _applResult = 12; // source: CBTRN02C.cbl:239-243
        if (ApplAok) { /* CONTINUE */ }                         // source: CBTRN02C.cbl:244-245
        else
        {
            _sysout.Add("ERROR OPENING DALYTRAN");              // source: CBTRN02C.cbl:247
            DisplayIoStatus9910(_dalytranStatus);              // source: CBTRN02C.cbl:248-249
            AbendProgram9999();                                // source: CBTRN02C.cbl:250
        }
    }

    // -------------------------------------------------------------------------------------------------
    // 0100-TRANFILE-OPEN // source: CBTRN02C.cbl:254-270
    private void TranfileOpen0100()
    {
        _applResult = 8;                                        // source: CBTRN02C.cbl:255
        // OPEN OUTPUT TRANSACT-FILE -> truncate-then-load: the transaction master is rebuilt each run.
        foreach (Transaction t in _transact.ReadAll().ToList())
            _transact.Delete(t.TranId);
        string status = FileStatus.Ok;
        if (status == FileStatus.Ok) _applResult = 0; else _applResult = 12; // source: CBTRN02C.cbl:257-261
        if (ApplAok) { /* CONTINUE */ }                         // source: CBTRN02C.cbl:262-263
        else
        {
            _sysout.Add("ERROR OPENING TRANSACTION FILE");      // source: CBTRN02C.cbl:265
            DisplayIoStatus9910(status);                       // source: CBTRN02C.cbl:266-267
            AbendProgram9999();                                // source: CBTRN02C.cbl:268
        }
    }

    // -------------------------------------------------------------------------------------------------
    // 0200-XREFFILE-OPEN // source: CBTRN02C.cbl:272-289
    private void XreffileOpen0200()
    {
        _applResult = 8;                                        // source: CBTRN02C.cbl:274
        string status = FileStatus.Ok;                          // OPEN INPUT XREF-FILE (random) — always '00'.
        if (status == FileStatus.Ok) _applResult = 0; else _applResult = 12; // source: CBTRN02C.cbl:276-280
        if (ApplAok) { /* CONTINUE */ }                         // source: CBTRN02C.cbl:281-282
        else
        {
            _sysout.Add("ERROR OPENING CROSS REF FILE");        // source: CBTRN02C.cbl:284
            DisplayIoStatus9910(status);                       // source: CBTRN02C.cbl:285-286
            AbendProgram9999();                                // source: CBTRN02C.cbl:287
        }
    }

    // -------------------------------------------------------------------------------------------------
    // 0300-DALYREJS-OPEN // source: CBTRN02C.cbl:291-307
    private void DalyrejsOpen0300()
    {
        _applResult = 8;                                        // source: CBTRN02C.cbl:292
        // OPEN OUTPUT DALYREJS-FILE — the writer was created over an emptied dataset in Run().
        string status = FileStatus.Ok;
        if (status == FileStatus.Ok) _applResult = 0; else _applResult = 12; // source: CBTRN02C.cbl:294-298
        if (ApplAok) { /* CONTINUE */ }                         // source: CBTRN02C.cbl:299-300
        else
        {
            _sysout.Add("ERROR OPENING DALY REJECTS FILE");     // source: CBTRN02C.cbl:302
            DisplayIoStatus9910(status);                       // source: CBTRN02C.cbl:303-304
            AbendProgram9999();                                // source: CBTRN02C.cbl:305
        }
    }

    // -------------------------------------------------------------------------------------------------
    // 0400-ACCTFILE-OPEN // source: CBTRN02C.cbl:309-325
    private void AcctfileOpen0400()
    {
        _applResult = 8;                                        // source: CBTRN02C.cbl:310
        string status = FileStatus.Ok;                          // OPEN I-O ACCOUNT-FILE (random) — '00'.
        if (status == FileStatus.Ok) _applResult = 0; else _applResult = 12; // source: CBTRN02C.cbl:312-316
        if (ApplAok) { /* CONTINUE */ }                         // source: CBTRN02C.cbl:317-318
        else
        {
            _sysout.Add("ERROR OPENING ACCOUNT MASTER FILE");   // source: CBTRN02C.cbl:320
            DisplayIoStatus9910(status);                       // source: CBTRN02C.cbl:321-322
            AbendProgram9999();                                // source: CBTRN02C.cbl:323
        }
    }

    // -------------------------------------------------------------------------------------------------
    // 0500-TCATBALF-OPEN // source: CBTRN02C.cbl:327-343
    private void TcatbalfOpen0500()
    {
        _applResult = 8;                                        // source: CBTRN02C.cbl:328
        _tcatbalfStatus = FileStatus.Ok;                        // OPEN I-O TCATBAL-FILE (random) — '00'.
        if (_tcatbalfStatus == FileStatus.Ok) _applResult = 0; else _applResult = 12; // source: CBTRN02C.cbl:330-334
        if (ApplAok) { /* CONTINUE */ }                         // source: CBTRN02C.cbl:335-336
        else
        {
            _sysout.Add("ERROR OPENING TRANSACTION BALANCE FILE"); // source: CBTRN02C.cbl:338
            DisplayIoStatus9910(_tcatbalfStatus);              // source: CBTRN02C.cbl:339-340
            AbendProgram9999();                                // source: CBTRN02C.cbl:341
        }
    }

    // -------------------------------------------------------------------------------------------------
    // 1000-DALYTRAN-GET-NEXT // source: CBTRN02C.cbl:345-369
    private void DalytranGetNext1000()
    {
        // READ DALYTRAN-FILE INTO DALYTRAN-RECORD (sequential next). // source: CBTRN02C.cbl:346
        _dalytranStatus = _dalyTran.ReadNext(out DailyTransaction? next);
        if (_dalytranStatus == FileStatus.Ok)                  // source: CBTRN02C.cbl:347
        {
            _dalytranRecord = next!;
            _applResult = 0;                                   // MOVE 0 TO APPL-RESULT // source: CBTRN02C.cbl:348
        }
        else if (_dalytranStatus == FileStatus.EndOfFile)      // '10' // source: CBTRN02C.cbl:351
        {
            _applResult = 16;                                  // MOVE 16 TO APPL-RESULT // source: CBTRN02C.cbl:352
        }
        else
        {
            _applResult = 12;                                  // MOVE 12 TO APPL-RESULT // source: CBTRN02C.cbl:354
        }

        if (ApplAok) { /* CONTINUE */ }                        // source: CBTRN02C.cbl:357-358
        else if (ApplEof)                                      // source: CBTRN02C.cbl:360
        {
            _endOfFile = true;                                 // MOVE 'Y' TO END-OF-FILE // source: CBTRN02C.cbl:361
        }
        else
        {
            _sysout.Add("ERROR READING DALYTRAN FILE");         // source: CBTRN02C.cbl:363
            DisplayIoStatus9910(_dalytranStatus);              // source: CBTRN02C.cbl:364-365
            AbendProgram9999();                                // source: CBTRN02C.cbl:366
        }
    }

    // -------------------------------------------------------------------------------------------------
    // 1500-VALIDATE-TRAN // source: CBTRN02C.cbl:370-378
    private void ValidateTran1500()
    {
        LookupXref1500A();                                     // source: CBTRN02C.cbl:371
        if (_wsValidationFailReason == 0)                      // source: CBTRN02C.cbl:372
            LookupAcct1500B();                                 // source: CBTRN02C.cbl:373
        // ELSE CONTINUE // source: CBTRN02C.cbl:374-375
        // * ADD MORE VALIDATIONS HERE // source: CBTRN02C.cbl:377
    }

    // -------------------------------------------------------------------------------------------------
    // 1500-A-LOOKUP-XREF // source: CBTRN02C.cbl:380-392
    private void LookupXref1500A()
    {
        // MOVE DALYTRAN-CARD-NUM TO FD-XREF-CARD-NUM; READ XREF-FILE INTO CARD-XREF-RECORD.
        // source: CBTRN02C.cbl:382-383
        string status = _xref.ReadByKey(_dalytranRecord.CardNum, out CardXref? xref);
        if (status != FileStatus.Ok)                           // INVALID KEY // source: CBTRN02C.cbl:384
        {
            _wsValidationFailReason = 100;                     // source: CBTRN02C.cbl:385
            _wsValidationFailReasonDesc = "INVALID CARD NUMBER FOUND"; // source: CBTRN02C.cbl:386-387
        }
        else
        {
            // NOT INVALID KEY -> CONTINUE // source: CBTRN02C.cbl:388-390
            _cardXrefRecord = xref;
        }
    }

    // -------------------------------------------------------------------------------------------------
    // 1500-B-LOOKUP-ACCT // source: CBTRN02C.cbl:393-422
    private void LookupAcct1500B()
    {
        // MOVE XREF-ACCT-ID TO FD-ACCT-ID; READ ACCOUNT-FILE INTO ACCOUNT-RECORD. // source: CBTRN02C.cbl:394-395
        string status = _account.ReadByKey(_cardXrefRecord!.AcctId, out Account? acct);
        if (status != FileStatus.Ok)                           // INVALID KEY // source: CBTRN02C.cbl:396
        {
            _wsValidationFailReason = 101;                     // source: CBTRN02C.cbl:397
            _wsValidationFailReasonDesc = "ACCOUNT RECORD NOT FOUND"; // source: CBTRN02C.cbl:398-399
            return;
        }

        // NOT INVALID KEY:
        _accountRecord = acct!;

        // COMPUTE WS-TEMP-BAL = ACCT-CURR-CYC-CREDIT - ACCT-CURR-CYC-DEBIT + DALYTRAN-AMT.
        // WS-TEMP-BAL is S9(9)V99 (FAITHFUL BUG #5: truncated to 9 integer digits, toward zero, silent
        // overflow); the credit-limit check uses cycle figures, NOT ACCT-CURR-BAL (FAITHFUL BUG #4).
        // source: CBTRN02C.cbl:403-405
        decimal wsTempBal = Decimals.Store(
            _accountRecord.CurrCycCredit - _accountRecord.CurrCycDebit + _dalytranRecord.Amt,
            TempBalDigits, MoneyScale, signed: true);

        // IF ACCT-CREDIT-LIMIT >= WS-TEMP-BAL CONTINUE ELSE reason 102. // source: CBTRN02C.cbl:407-413
        if (_accountRecord.CreditLimit >= wsTempBal)
        {
            // CONTINUE
        }
        else
        {
            _wsValidationFailReason = 102;                     // source: CBTRN02C.cbl:410
            _wsValidationFailReasonDesc = "OVERLIMIT TRANSACTION"; // source: CBTRN02C.cbl:411-412
        }

        // IF ACCT-EXPIRAION-DATE >= DALYTRAN-ORIG-TS (1:10) CONTINUE ELSE reason 103.
        // FAITHFUL BUG #1: this is an independent sequential IF (no ELSE on the 102 check), so an
        // over-limit AND expired record has 102 overwritten by 103. Ordinal (alphanumeric) date compare:
        // ACCT-EXPIRAION-DATE X(10) vs the first 10 chars of DALYTRAN-ORIG-TS X(26). // source: CBTRN02C.cbl:414-420
        string acctExpir = _accountRecord.ExpirationDate;
        string origDate = Ref(_dalytranRecord.OrigTs, 1, 10);
        if (string.CompareOrdinal(acctExpir, origDate) >= 0)
        {
            // CONTINUE
        }
        else
        {
            _wsValidationFailReason = 103;                     // source: CBTRN02C.cbl:417
            _wsValidationFailReasonDesc = "TRANSACTION RECEIVED AFTER ACCT EXPIRATION"; // source: CBTRN02C.cbl:418-419
        }
    }

    // -------------------------------------------------------------------------------------------------
    // 2000-POST-TRANSACTION // source: CBTRN02C.cbl:424-444
    private void PostTransaction2000()
    {
        // Build TRAN-RECORD from DALYTRAN-RECORD field-by-field. // source: CBTRN02C.cbl:425-436
        _tranRecord = new Transaction
        {
            TranId = _dalytranRecord.TranId,                   // MOVE DALYTRAN-ID TO TRAN-ID
            TypeCd = _dalytranRecord.TypeCd,                   // MOVE DALYTRAN-TYPE-CD TO TRAN-TYPE-CD
            CatCd = _dalytranRecord.CatCd,                     // MOVE DALYTRAN-CAT-CD TO TRAN-CAT-CD
            Source = _dalytranRecord.Source,                   // MOVE DALYTRAN-SOURCE TO TRAN-SOURCE
            Desc = _dalytranRecord.Desc,                       // MOVE DALYTRAN-DESC TO TRAN-DESC
            Amt = _dalytranRecord.Amt,                         // MOVE DALYTRAN-AMT TO TRAN-AMT
            MerchantId = _dalytranRecord.MerchantId,           // MOVE DALYTRAN-MERCHANT-ID TO TRAN-MERCHANT-ID
            MerchantName = _dalytranRecord.MerchantName,       // MOVE DALYTRAN-MERCHANT-NAME TO TRAN-MERCHANT-NAME
            MerchantCity = _dalytranRecord.MerchantCity,       // MOVE DALYTRAN-MERCHANT-CITY TO TRAN-MERCHANT-CITY
            MerchantZip = _dalytranRecord.MerchantZip,         // MOVE DALYTRAN-MERCHANT-ZIP TO TRAN-MERCHANT-ZIP
            CardNum = _dalytranRecord.CardNum,                 // MOVE DALYTRAN-CARD-NUM TO TRAN-CARD-NUM
            OrigTs = _dalytranRecord.OrigTs,                   // MOVE DALYTRAN-ORIG-TS TO TRAN-ORIG-TS
            // FAITHFUL BUG #7: TRAN-PROC-TS is the current run clock, NOT DALYTRAN-PROC-TS.
            ProcTs = GetDb2FormatTimestampZ(),                 // source: CBTRN02C.cbl:437-438
        };

        UpdateTcatbal2700();                                   // source: CBTRN02C.cbl:440
        UpdateAccountRec2800();                                // source: CBTRN02C.cbl:441
        WriteTransactionFile2900();                            // source: CBTRN02C.cbl:442
        // FAITHFUL BUG #2: WS-VALIDATION-FAIL-REASON (possibly 109 from 2800) is never re-checked here,
        // so the transaction is written regardless and no reject is produced. // source: CBTRN02C.cbl:424-444
    }

    // -------------------------------------------------------------------------------------------------
    // 2500-WRITE-REJECT-REC // source: CBTRN02C.cbl:446-465
    private void WriteRejectRec2500()
    {
        // MOVE DALYTRAN-RECORD TO REJECT-TRAN-DATA (X350); MOVE WS-VALIDATION-TRAILER TO VALIDATION-TRAILER
        // (X80 = reason 9(4) + desc X(76)). The full record is 430 bytes. // source: CBTRN02C.cbl:447-451
        var reject = new byte[430];
        SerializeDalytranRecord(_dalytranRecord).CopyTo(reject, 0);              // REJECT-TRAN-DATA (350)
        ZonedDecimalCodec.Encode(_wsValidationFailReason, reject.AsSpan(350, 4), 4, 0, false, _host); // 9(4)
        HostEncoding.For(_host).GetBytes(Alpha(_wsValidationFailReasonDesc, 76)).CopyTo(reject, 354);  // X(76)

        _applResult = 8;                                       // MOVE 8 TO APPL-RESULT // source: CBTRN02C.cbl:450
        // WRITE FD-REJS-RECORD FROM REJECT-RECORD. // source: CBTRN02C.cbl:451
        string status = WriteRejectImage(reject);
        if (status == FileStatus.Ok) _applResult = 0; else _applResult = 12; // source: CBTRN02C.cbl:452-456
        if (ApplAok) { /* CONTINUE */ }                        // source: CBTRN02C.cbl:457-458
        else
        {
            _sysout.Add("ERROR WRITING TO REJECTS FILE");       // source: CBTRN02C.cbl:460
            DisplayIoStatus9910(status);                       // source: CBTRN02C.cbl:461-462
            AbendProgram9999();                                // source: CBTRN02C.cbl:463
        }
    }

    // -------------------------------------------------------------------------------------------------
    // 2700-UPDATE-TCATBAL // source: CBTRN02C.cbl:467-501
    private void UpdateTcatbal2700()
    {
        // MOVE XREF-ACCT-ID / DALYTRAN-TYPE-CD / DALYTRAN-CAT-CD into FD-TRAN-CAT-KEY. // source: CBTRN02C.cbl:469-471
        long acctId = _cardXrefRecord!.AcctId;
        string typeCd = _dalytranRecord.TypeCd;
        int catCd = _dalytranRecord.CatCd;

        _wsCreateTrancatRec = false;                           // MOVE 'N' TO WS-CREATE-TRANCAT-REC // source: CBTRN02C.cbl:473

        // READ TCATBAL-FILE INTO TRAN-CAT-BAL-RECORD; INVALID KEY -> display + flag create. // source: CBTRN02C.cbl:474-479
        _tcatbalfStatus = _tcatBal.ReadByKey(acctId, typeCd, catCd, out TranCatBalance? bal);
        if (_tcatbalfStatus == FileStatus.RecordNotFound)      // INVALID KEY
        {
            _sysout.Add("TCATBAL record not found for key : " + FormatTranCatKey(acctId, typeCd, catCd) + ".. Creating.");
            _wsCreateTrancatRec = true;                        // MOVE 'Y' TO WS-CREATE-TRANCAT-REC // source: CBTRN02C.cbl:478
        }
        else
        {
            _tranCatBalRecord = bal;
        }

        // IF TCATBALF-STATUS = '00' OR '23' -> AOK ELSE 12 (the '23' is tolerated). // source: CBTRN02C.cbl:481-485
        if (_tcatbalfStatus == FileStatus.Ok || _tcatbalfStatus == FileStatus.RecordNotFound)
            _applResult = 0;
        else
            _applResult = 12;
        if (ApplAok) { /* CONTINUE */ }                        // source: CBTRN02C.cbl:486-487
        else
        {
            _sysout.Add("ERROR READING TRANSACTION BALANCE FILE"); // source: CBTRN02C.cbl:489
            DisplayIoStatus9910(_tcatbalfStatus);              // source: CBTRN02C.cbl:490-491
            AbendProgram9999();                                // source: CBTRN02C.cbl:492
        }

        if (_wsCreateTrancatRec)                               // source: CBTRN02C.cbl:495
            CreateTcatbalRec2700A(acctId, typeCd, catCd);      // source: CBTRN02C.cbl:496
        else
            UpdateTcatbalRec2700B();                           // source: CBTRN02C.cbl:498
    }

    // -------------------------------------------------------------------------------------------------
    // 2700-A-CREATE-TCATBAL-REC // source: CBTRN02C.cbl:503-524
    private void CreateTcatbalRec2700A(long acctId, string typeCd, int catCd)
    {
        // INITIALIZE TRAN-CAT-BAL-RECORD (all elementary fields to zero/spaces). // source: CBTRN02C.cbl:504
        var rec = new TranCatBalance
        {
            AcctId = acctId,                                   // MOVE XREF-ACCT-ID TO TRANCAT-ACCT-ID // source: CBTRN02C.cbl:505
            TypeCd = typeCd,                                   // MOVE DALYTRAN-TYPE-CD TO TRANCAT-TYPE-CD // source: CBTRN02C.cbl:506
            CatCd = catCd,                                     // MOVE DALYTRAN-CAT-CD TO TRANCAT-CD // source: CBTRN02C.cbl:507
            // ADD DALYTRAN-AMT TO TRAN-CAT-BAL (from 0) -> = DALYTRAN-AMT, stored S9(9)V99. // source: CBTRN02C.cbl:508
            TranCatBal = Decimals.Store(0m + _dalytranRecord.Amt, DalyAmtDigits, MoneyScale, signed: true),
        };
        _tranCatBalRecord = rec;

        // WRITE FD-TRAN-CAT-BAL-RECORD FROM TRAN-CAT-BAL-RECORD. // source: CBTRN02C.cbl:510
        _tcatbalfStatus = _tcatBal.Insert(rec);
        if (_tcatbalfStatus == FileStatus.Ok) _applResult = 0; else _applResult = 12; // source: CBTRN02C.cbl:512-516
        if (ApplAok) { /* CONTINUE */ }                        // source: CBTRN02C.cbl:517-518
        else
        {
            _sysout.Add("ERROR WRITING TRANSACTION BALANCE FILE"); // source: CBTRN02C.cbl:520
            DisplayIoStatus9910(_tcatbalfStatus);              // source: CBTRN02C.cbl:521-522
            AbendProgram9999();                                // source: CBTRN02C.cbl:523
        }
    }

    // -------------------------------------------------------------------------------------------------
    // 2700-B-UPDATE-TCATBAL-REC // source: CBTRN02C.cbl:526-542
    private void UpdateTcatbalRec2700B()
    {
        // ADD DALYTRAN-AMT TO TRAN-CAT-BAL (accumulate into existing S9(9)V99 balance, silent overflow).
        // source: CBTRN02C.cbl:527
        _tranCatBalRecord!.TranCatBal = Decimals.Store(
            _tranCatBalRecord.TranCatBal + _dalytranRecord.Amt, DalyAmtDigits, MoneyScale, signed: true);

        // REWRITE FD-TRAN-CAT-BAL-RECORD FROM TRAN-CAT-BAL-RECORD. // source: CBTRN02C.cbl:528
        _tcatbalfStatus = _tcatBal.Update(_tranCatBalRecord);
        if (_tcatbalfStatus == FileStatus.Ok) _applResult = 0; else _applResult = 12; // source: CBTRN02C.cbl:530-534
        if (ApplAok) { /* CONTINUE */ }                        // source: CBTRN02C.cbl:535-536
        else
        {
            _sysout.Add("ERROR REWRITING TRANSACTION BALANCE FILE"); // source: CBTRN02C.cbl:538
            DisplayIoStatus9910(_tcatbalfStatus);              // source: CBTRN02C.cbl:539-540
            AbendProgram9999();                                // source: CBTRN02C.cbl:541
        }
    }

    // -------------------------------------------------------------------------------------------------
    // 2800-UPDATE-ACCOUNT-REC // source: CBTRN02C.cbl:545-560
    private void UpdateAccountRec2800()
    {
        decimal amt = _dalytranRecord.Amt;

        // ADD DALYTRAN-AMT TO ACCT-CURR-BAL (S9(10)V99). // source: CBTRN02C.cbl:547
        _accountRecord!.CurrBal = Decimals.Store(_accountRecord.CurrBal + amt, CycleDigits, MoneyScale, signed: true);

        // IF DALYTRAN-AMT >= 0 -> add to ACCT-CURR-CYC-CREDIT ELSE add to ACCT-CURR-CYC-DEBIT.
        // FAITHFUL BUG #3: the negative amount is ADDed (not negated) into the debit bucket. // source: CBTRN02C.cbl:548-552
        if (amt >= 0m)
            _accountRecord.CurrCycCredit = Decimals.Store(_accountRecord.CurrCycCredit + amt, CycleDigits, MoneyScale, signed: true);
        else
            _accountRecord.CurrCycDebit = Decimals.Store(_accountRecord.CurrCycDebit + amt, CycleDigits, MoneyScale, signed: true);

        // REWRITE FD-ACCTFILE-REC FROM ACCOUNT-RECORD; INVALID KEY -> reason 109. // source: CBTRN02C.cbl:554-559
        string status = _account.Update(_accountRecord);
        if (status != FileStatus.Ok)                           // INVALID KEY
        {
            _wsValidationFailReason = 109;                     // source: CBTRN02C.cbl:556
            _wsValidationFailReasonDesc = "ACCOUNT RECORD NOT FOUND"; // source: CBTRN02C.cbl:557-558
        }
    }

    // -------------------------------------------------------------------------------------------------
    // 2900-WRITE-TRANSACTION-FILE // source: CBTRN02C.cbl:562-579
    private void WriteTransactionFile2900()
    {
        _applResult = 8;                                       // MOVE 8 TO APPL-RESULT // source: CBTRN02C.cbl:563
        // WRITE FD-TRANFILE-REC FROM TRAN-RECORD. // source: CBTRN02C.cbl:564
        string status = _transact.Insert(_tranRecord);
        if (status == FileStatus.Ok) _applResult = 0; else _applResult = 12; // source: CBTRN02C.cbl:566-570
        if (ApplAok) { /* CONTINUE */ }                        // source: CBTRN02C.cbl:571-572
        else
        {
            _sysout.Add("ERROR WRITING TO TRANSACTION FILE");   // source: CBTRN02C.cbl:574
            DisplayIoStatus9910(status);                       // source: CBTRN02C.cbl:575-576
            AbendProgram9999();                                // source: CBTRN02C.cbl:577
        }
    }

    // -------------------------------------------------------------------------------------------------
    // 9000-DALYTRAN-CLOSE // source: CBTRN02C.cbl:582-598
    private void DalytranClose9000()
    {
        _applResult = 8;                                       // source: CBTRN02C.cbl:583
        _dalyTran.EndBrowse();                                 // CLOSE DALYTRAN-FILE
        _dalytranStatus = FileStatus.Ok;
        if (_dalytranStatus == FileStatus.Ok) _applResult = 0; else _applResult = 12; // source: CBTRN02C.cbl:585-589
        if (ApplAok) { /* CONTINUE */ }                        // source: CBTRN02C.cbl:590-591
        else
        {
            _sysout.Add("ERROR CLOSING DALYTRAN FILE");         // source: CBTRN02C.cbl:593
            DisplayIoStatus9910(_dalytranStatus);              // source: CBTRN02C.cbl:594-595
            AbendProgram9999();                                // source: CBTRN02C.cbl:596
        }
    }

    // -------------------------------------------------------------------------------------------------
    // 9100-TRANFILE-CLOSE // source: CBTRN02C.cbl:600-616
    private void TranfileClose9100()
    {
        _applResult = 8;                                       // source: CBTRN02C.cbl:601
        string status = FileStatus.Ok;                          // CLOSE TRANSACT-FILE
        if (status == FileStatus.Ok) _applResult = 0; else _applResult = 12; // source: CBTRN02C.cbl:603-607
        if (ApplAok) { /* CONTINUE */ }                        // source: CBTRN02C.cbl:608-609
        else
        {
            _sysout.Add("ERROR CLOSING TRANSACTION FILE");      // source: CBTRN02C.cbl:611
            DisplayIoStatus9910(status);                       // source: CBTRN02C.cbl:612-613
            AbendProgram9999();                                // source: CBTRN02C.cbl:614
        }
    }

    // -------------------------------------------------------------------------------------------------
    // 9200-XREFFILE-CLOSE // source: CBTRN02C.cbl:619-635
    private void XreffileClose9200()
    {
        _applResult = 8;                                       // source: CBTRN02C.cbl:620
        string status = FileStatus.Ok;                          // CLOSE XREF-FILE
        if (status == FileStatus.Ok) _applResult = 0; else _applResult = 12; // source: CBTRN02C.cbl:622-626
        if (ApplAok) { /* CONTINUE */ }                        // source: CBTRN02C.cbl:627-628
        else
        {
            _sysout.Add("ERROR CLOSING CROSS REF FILE");        // source: CBTRN02C.cbl:630
            DisplayIoStatus9910(status);                       // source: CBTRN02C.cbl:631-632
            AbendProgram9999();                                // source: CBTRN02C.cbl:633
        }
    }

    // -------------------------------------------------------------------------------------------------
    // 9300-DALYREJS-CLOSE // source: CBTRN02C.cbl:637-653
    private void DalyrejsClose9300()
    {
        _applResult = 8;                                       // source: CBTRN02C.cbl:638
        string dalyrejsStatus = FileStatus.Ok;                  // CLOSE DALYREJS-FILE
        if (dalyrejsStatus == FileStatus.Ok) _applResult = 0; else _applResult = 12; // source: CBTRN02C.cbl:640-644
        if (ApplAok) { /* CONTINUE */ }                        // source: CBTRN02C.cbl:645-646
        else
        {
            _sysout.Add("ERROR CLOSING DAILY REJECTS FILE");    // source: CBTRN02C.cbl:648
            // FAITHFUL BUG #6: displays XREFFILE-STATUS instead of DALYREJS-STATUS (copy/paste).
            DisplayIoStatus9910(FileStatus.Ok /* XREFFILE-STATUS */); // source: CBTRN02C.cbl:649-650
            AbendProgram9999();                                // source: CBTRN02C.cbl:651
        }
    }

    // -------------------------------------------------------------------------------------------------
    // 9400-ACCTFILE-CLOSE // source: CBTRN02C.cbl:655-671
    private void AcctfileClose9400()
    {
        _applResult = 8;                                       // source: CBTRN02C.cbl:656
        string status = FileStatus.Ok;                          // CLOSE ACCOUNT-FILE
        if (status == FileStatus.Ok) _applResult = 0; else _applResult = 12; // source: CBTRN02C.cbl:658-662
        if (ApplAok) { /* CONTINUE */ }                        // source: CBTRN02C.cbl:663-664
        else
        {
            _sysout.Add("ERROR CLOSING ACCOUNT FILE");          // source: CBTRN02C.cbl:666
            DisplayIoStatus9910(status);                       // source: CBTRN02C.cbl:667-668
            AbendProgram9999();                                // source: CBTRN02C.cbl:669
        }
    }

    // -------------------------------------------------------------------------------------------------
    // 9500-TCATBALF-CLOSE // source: CBTRN02C.cbl:674-690
    private void TcatbalfClose9500()
    {
        _applResult = 8;                                       // source: CBTRN02C.cbl:675
        _tcatbalfStatus = FileStatus.Ok;                        // CLOSE TCATBAL-FILE
        if (_tcatbalfStatus == FileStatus.Ok) _applResult = 0; else _applResult = 12; // source: CBTRN02C.cbl:677-681
        if (ApplAok) { /* CONTINUE */ }                        // source: CBTRN02C.cbl:682-683
        else
        {
            _sysout.Add("ERROR CLOSING TRANSACTION BALANCE FILE"); // source: CBTRN02C.cbl:685
            DisplayIoStatus9910(_tcatbalfStatus);              // source: CBTRN02C.cbl:686-687
            AbendProgram9999();                                // source: CBTRN02C.cbl:688
        }
    }

    // -------------------------------------------------------------------------------------------------
    // Z-GET-DB2-FORMAT-TIMESTAMP // source: CBTRN02C.cbl:692-705
    // Builds 'YYYY-MM-DD-HH.MM.SS.mmmm0000' from FUNCTION CURRENT-DATE. DB2-MIL is 9(02) (2 digits of
    // hundredths), DB2-REST is hardcoded '0000'.
    private string GetDb2FormatTimestampZ()
    {
        DateTime now = _clock.Now;
        int hundredths = now.Millisecond / 10;                 // COB-MIL = hundredths of a second (2 digits)
        return $"{now:yyyy-MM-dd-HH.mm.ss}.{hundredths:D2}0000";
    }

    // -------------------------------------------------------------------------------------------------
    // 9999-ABEND-PROGRAM // source: CBTRN02C.cbl:707-711
    private void AbendProgram9999()
    {
        _sysout.Add("ABENDING PROGRAM");                       // source: CBTRN02C.cbl:708
        // MOVE 0 TO TIMING; MOVE 999 TO ABCODE; CALL 'CEE3ABD' USING ABCODE, TIMING.
        throw new AbendException("999", "CBTRN02C abend (CEE3ABD).");
    }

    // -------------------------------------------------------------------------------------------------
    // 9910-DISPLAY-IO-STATUS // source: CBTRN02C.cbl:714-727
    // Renders the 2-byte file status as a 4-digit "NNNN" number. On the non-numeric / IO-STAT1='9' branch
    // the second byte is read as the low byte of a big-endian halfword (so the char's code point, e.g.
    // '0'->240, in the host encoding).
    private void DisplayIoStatus9910(string ioStatus)
    {
        string s = ioStatus.Length >= 2 ? ioStatus[..2] : ioStatus.PadRight(2);
        char stat1 = s[0], stat2 = s[1];
        bool numeric = char.IsDigit(stat1) && char.IsDigit(stat2);

        string ioStatus04;
        if (!numeric || stat1 == '9')                          // source: CBTRN02C.cbl:715-716
        {
            // MOVE IO-STAT1 TO IO-STATUS-04(1:1); zero TWO-BYTES-BINARY; MOVE IO-STAT2 TO TWO-BYTES-RIGHT;
            // MOVE TWO-BYTES-BINARY TO IO-STATUS-0403. // source: CBTRN02C.cbl:717-720
            int low = HostEncoding.For(_host).GetBytes(stat2.ToString())[0];
            ioStatus04 = stat1.ToString() + (low % 1000).ToString("D3");
        }
        else
        {
            // MOVE '0000' TO IO-STATUS-04; MOVE IO-STATUS TO IO-STATUS-04(3:2). // source: CBTRN02C.cbl:723-724
            ioStatus04 = "00" + s;
        }
        _sysout.Add("FILE STATUS IS: NNNN" + ioStatus04);      // source: CBTRN02C.cbl:721,725
    }

    // =================================================================================================
    // Helpers
    // =================================================================================================

    /// <summary>
    /// Serializes a DAILY_TRANSACTION row into its 350-byte CVTRA06Y record image (the REJECT-TRAN-DATA
    /// portion of the reject record). Field order/widths per CVTRA06Y; numerics are zoned (USAGE DISPLAY),
    /// the amount is signed S9(9)V99, the rest unsigned; trailing FILLER X(20) is spaces.
    /// </summary>
    private byte[] SerializeDalytranRecord(DailyTransaction d)
    {
        var image = new byte[350];
        FillSpaces(image);
        int pos = 0;
        pos += PutAlpha(image, pos, d.TranId, 16);             // DALYTRAN-ID            X(16)
        pos += PutAlpha(image, pos, d.TypeCd, 2);              // DALYTRAN-TYPE-CD       X(2)
        pos += PutZoned(image, pos, d.CatCd, 4, 0, false);     // DALYTRAN-CAT-CD        9(4)
        pos += PutAlpha(image, pos, d.Source, 10);             // DALYTRAN-SOURCE        X(10)
        pos += PutAlpha(image, pos, d.Desc, 100);              // DALYTRAN-DESC          X(100)
        pos += PutZoned(image, pos, d.Amt, DalyAmtDigits + MoneyScale, MoneyScale, true); // DALYTRAN-AMT S9(9)V99
        pos += PutZoned(image, pos, d.MerchantId, 9, 0, false);// DALYTRAN-MERCHANT-ID   9(9)
        pos += PutAlpha(image, pos, d.MerchantName, 50);       // DALYTRAN-MERCHANT-NAME X(50)
        pos += PutAlpha(image, pos, d.MerchantCity, 50);       // DALYTRAN-MERCHANT-CITY X(50)
        pos += PutAlpha(image, pos, d.MerchantZip, 10);        // DALYTRAN-MERCHANT-ZIP  X(10)
        pos += PutAlpha(image, pos, d.CardNum, 16);            // DALYTRAN-CARD-NUM      X(16)
        pos += PutAlpha(image, pos, d.OrigTs, 26);             // DALYTRAN-ORIG-TS       X(26)
        pos += PutAlpha(image, pos, d.ProcTs, 26);             // DALYTRAN-PROC-TS       X(26)
        // FILLER X(20) already spaces.
        return image;
    }

    private int PutAlpha(byte[] image, int offset, string text, int width)
    {
        byte[] bytes = HostEncoding.For(_host).GetBytes(Alpha(text, width));
        Array.Copy(bytes, 0, image, offset, width);
        return width;
    }

    private int PutZoned(byte[] image, int offset, decimal value, int totalDigits, int scale, bool signed)
    {
        ZonedDecimalCodec.Encode(value, image.AsSpan(offset, totalDigits), totalDigits, scale, signed, _host);
        return totalDigits;
    }

    private int PutZoned(byte[] image, int offset, long value, int totalDigits, int scale, bool signed)
        => PutZoned(image, offset, (decimal)value, totalDigits, scale, signed);

    private void FillSpaces(byte[] image)
    {
        byte space = HostEncoding.For(_host).GetBytes(" ")[0];
        Array.Fill(image, space);
    }

    /// <summary>WRITE FD-REJS-RECORD: appends the 430-byte reject image to the flat dataset. Returns '00'.</summary>
    private string WriteRejectImage(byte[] image)
    {
        _dalyRejs.WriteRecord(image, 430);
        return FileStatus.Ok;
    }

    /// <summary>
    /// Renders FD-TRAN-CAT-KEY for the "not found .. Creating" DISPLAY: FD-TRANCAT-ACCT-ID 9(11) zoned +
    /// FD-TRANCAT-TYPE-CD X(2) + FD-TRANCAT-CD 9(4) zoned (17 bytes). // source: CBTRN02C.cbl:93-96,476-477
    /// </summary>
    private static string FormatTranCatKey(long acctId, string typeCd, int catCd)
        => Zoned(acctId, 11) + Alpha(typeCd, 2) + Zoned(catCd, 4);

    /// <summary>COBOL reference modification s(start:len), 1-based start.</summary>
    private static string Ref(string s, int start, int len)
    {
        s ??= "";
        int i = start - 1;
        if (i >= s.Length) return new string(' ', len);
        string sub = s.Substring(i, Math.Min(len, s.Length - i));
        return sub.Length >= len ? sub : sub.PadRight(len, ' ');
    }

    /// <summary>PIC X(width): left-justified, space-padded, right-truncated.</summary>
    private static string Alpha(string text, int width)
    {
        text ??= "";
        return text.Length >= width ? text[..width] : text.PadRight(width, ' ');
    }

    /// <summary>PIC 9(width) USAGE DISPLAY: unsigned, zero-padded decimal digits (low-order on overflow).</summary>
    private static string Zoned(long value, int width)
    {
        string digits = Math.Abs(value).ToString();
        return digits.Length >= width ? digits[^width..] : digits.PadLeft(width, '0');
    }

    private static void DeleteIfExists(string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        if (File.Exists(path)) File.Delete(path);
    }
}
