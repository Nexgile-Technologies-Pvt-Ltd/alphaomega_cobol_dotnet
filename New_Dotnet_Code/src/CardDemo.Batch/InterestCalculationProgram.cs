using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;

namespace CardDemo.Batch;

/// <summary>
/// Faithful relational re-port of the batch program <c>CBACT04C</c> (monthly interest calculator). It reads
/// the <b>TRAN_CAT_BAL</b> table sequentially in composite-key order; on each account break it rewrites the
/// previous account's balance with the accumulated interest (resetting its cycle credit/debit) and reloads
/// the account master + card cross-reference; for the current (group, type, cat) it looks up the
/// disclosure-group interest rate (falling back to the <c>DEFAULT</c> group when the specific one is
/// missing); and when the rate is non-zero it computes monthly interest <c>(TRAN-CAT-BAL * DIS-INT-RATE) /
/// 1200</c> (truncated, never rounded), accumulates it, and inserts one interest <b>TRANSACTION</b> row.
/// </summary>
/// <remarks>
/// <para>Ported paragraph-by-paragraph from <c>app/cbl/CBACT04C.cbl</c> per <c>_design/specs/CBACT04C.md</c>;
/// the six VSAM/QSAM files map to relational tables: TCATBAL-FILE -&gt; <see cref="TranCatBalanceRepository"/>
/// (ordered forward cursor), XREF-FILE -&gt; <see cref="CardXrefRepository.ReadByAltKey"/> (alt key acct_id),
/// ACCOUNT-FILE -&gt; <see cref="AccountRepository"/> read/update, DISCGRP-FILE -&gt;
/// <see cref="DisclosureGroupRepository.ReadByKey"/>, TRANSACT-FILE -&gt; <see cref="TransactionRepository"/>
/// truncate-then-insert.</para>
/// <para>FAITHFUL BUGS reproduced (see spec §6):
/// <list type="number">
/// <item>The main <c>PERFORM UNTIL END-OF-FILE='Y'</c> tests before each iteration, so the post-EOF
/// <c>ELSE PERFORM 1050-UPDATE-ACCOUNT</c> (line 220) is structurally unreachable: the LAST account's
/// balance is never updated (its interest transactions are still written). // source: CBACT04C.cbl:188-221</item>
/// <item>Mislabeled DISCGRP open error message ('ERROR OPENING DALY REJECTS FILE'). // source: CBACT04C.cbl:281</item>
/// <item><c>MOVE '05' TO TRAN-CAT-CD</c> (9(4)) yields 0005; <c>MOVE '01' TO TRAN-TYPE-CD</c> yields '01'. // source: CBACT04C.cbl:482-483</item>
/// <item>DB2 timestamp millis = hundredths + literal '0000'. // source: CBACT04C.cbl:621-622</item>
/// <item><c>1400-COMPUTE-FEES</c> is a no-op. // source: CBACT04C.cbl:516-520</item>
/// <item>Interest is TRUNCATED toward zero at 2 dp (no ROUNDED). // source: CBACT04C.cbl:464-465</item>
/// </list></para>
/// </remarks>
public sealed class InterestCalculationProgram
{
    private const int IntDigits = 9;   // WS-MONTHLY-INT / WS-TOTAL-INT / TRAN-CAT-BAL S9(09)V99
    private const int BalDigits = 10;  // ACCT-CURR-BAL S9(10)V99
    private const int MoneyScale = 2;

    // ---- The five "files" (relational tables) -------------------------------------------------------
    private TranCatBalanceRepository _tcatBal = null!; // TCATBAL-FILE  (OPEN INPUT, sequential)
    private CardXrefRepository _xref = null!;           // XREF-FILE     (OPEN INPUT, alt key acct_id)
    private AccountRepository _account = null!;         // ACCOUNT-FILE  (OPEN I-O, random)
    private DisclosureGroupRepository _discGrp = null!; // DISCGRP-FILE  (OPEN INPUT, random)
    private TransactionRepository _transact = null!;    // TRANSACT-FILE (OPEN OUTPUT)

    private IClock _clock = null!;
    private string _parmDate = "";

    // ---- WORKING-STORAGE (CBACT04C lines 166-173) ---------------------------------------------------
    private string _lastAcctNum = "~UNSET~";   // WS-LAST-ACCT-NUM PIC X(11) VALUE SPACES (sentinel -> 1st rec breaks)
    private decimal _monthlyInterest;          // WS-MONTHLY-INT  S9(09)V99
    private decimal _totalInterest;            // WS-TOTAL-INT    S9(09)V99
    private bool _firstTime = true;            // WS-FIRST-TIME   'Y'
    private long _recordCount;                 // WS-RECORD-COUNT 9(09)
    private long _tranIdSuffix;                // WS-TRANID-SUFFIX 9(06)
    private bool _endOfFile;                   // END-OF-FILE 'N'/'Y'

    // "Current" records the COBOL READ ... INTO populates.
    private TranCatBalance? _tranCatBal;       // TRAN-CAT-BAL-RECORD (CVTRA01Y)
    private Account? _accountRecord;           // ACCOUNT-RECORD      (CVACT01Y)
    private CardXref? _xrefRecord;             // CARD-XREF-RECORD    (CVACT03Y)
    private DisclosureGroup? _disGroup;        // DIS-GROUP-RECORD    (CVTRA02Y)

    private readonly List<string> _sysout = [];

    /// <summary>Console (DISPLAY) lines produced by the run, in order.</summary>
    public IReadOnlyList<string> Sysout => _sysout;

    /// <summary>WS-RECORD-COUNT at end of run (TCATBAL records processed).</summary>
    public long RecordCount => _recordCount;

    /// <summary>Interest transactions written (WS-TRANID-SUFFIX at end of run).</summary>
    public long InterestTransactionsWritten => _tranIdSuffix;

    /// <summary>
    /// Runs CBACT04C over the relational <paramref name="db"/>. <paramref name="parmDate"/> is the 10-char
    /// PARM date (e.g. "2022071800") used as the high-order 10 bytes of every generated TRAN-ID. Returns the
    /// process RETURN-CODE (CBACT04C leaves it 0).
    /// </summary>
    public int Run(RelationalDb db, string parmDate, IClock? clock = null)
        => Run(
            new TranCatBalanceRepository(db),
            new CardXrefRepository(db),
            new AccountRepository(db),
            new DisclosureGroupRepository(db),
            new TransactionRepository(db),
            parmDate, clock);

    /// <summary>Convenience overload taking a <see cref="BatchSupport"/> (uses its repositories).</summary>
    public int Run(BatchSupport support, string parmDate, IClock? clock = null)
        => Run(
            support.TranCatBalance, support.CardXref, support.Account,
            support.DisclosureGroup, support.Transaction, parmDate, clock);

    /// <summary>Runs CBACT04C over already-resolved repositories.</summary>
    public int Run(
        TranCatBalanceRepository tcatBal,
        CardXrefRepository xref,
        AccountRepository account,
        DisclosureGroupRepository discGrp,
        TransactionRepository transact,
        string parmDate,
        IClock? clock = null)
    {
        _tcatBal = tcatBal;
        _xref = xref;
        _account = account;
        _discGrp = discGrp;
        _transact = transact;
        _parmDate = (parmDate ?? "").Length >= 10 ? parmDate![..10] : (parmDate ?? "").PadRight(10);
        _clock = clock ?? SystemClock.Instance;

        _sysout.Add("START OF EXECUTION OF PROGRAM CBACT04C");   // source: CBACT04C.cbl:181
        OpenTranCatBalFile();                                    // source: CBACT04C.cbl:182
        OpenTransactionFile();                                   // source: CBACT04C.cbl:186 (truncate target)

        // PERFORM UNTIL END-OF-FILE = 'Y' (test-before; the ELSE flush at line 220 is unreachable — bug #1).
        while (!_endOfFile)                                      // source: CBACT04C.cbl:188
        {
            if (!_endOfFile)                                    // IF END-OF-FILE = 'N' // source: CBACT04C.cbl:189
            {
                ReadNextTranCatBal();                           // source: CBACT04C.cbl:190
                if (!_endOfFile)                                // IF END-OF-FILE = 'N' // source: CBACT04C.cbl:191
                {
                    _recordCount++;                             // ADD 1 TO WS-RECORD-COUNT // source: CBACT04C.cbl:192
                    _sysout.Add(DisplayTranCatBal(_tranCatBal!)); // DISPLAY TRAN-CAT-BAL-RECORD // source: CBACT04C.cbl:193

                    string acctKey = _tranCatBal!.AcctId.ToString("D11");
                    if (acctKey != _lastAcctNum)               // IF TRANCAT-ACCT-ID NOT= WS-LAST-ACCT-NUM // source: CBACT04C.cbl:194
                    {
                        if (!_firstTime) UpdateAccount();       // source: CBACT04C.cbl:195-196
                        else _firstTime = false;                // source: CBACT04C.cbl:198
                        _totalInterest = 0m;                    // source: CBACT04C.cbl:200
                        _lastAcctNum = acctKey;                 // source: CBACT04C.cbl:201
                        GetAccountData(_tranCatBal.AcctId);     // source: CBACT04C.cbl:202-203
                        GetXrefData(_tranCatBal.AcctId);        // source: CBACT04C.cbl:204-205
                    }

                    GetInterestRate();                         // source: CBACT04C.cbl:210-213
                    if (_disGroup!.IntRate != 0m)              // IF DIS-INT-RATE NOT = 0 // source: CBACT04C.cbl:214
                    {
                        ComputeInterest();                     // source: CBACT04C.cbl:215
                        ComputeFees();                         // source: CBACT04C.cbl:216 (no-op)
                    }
                }
            }
            else
            {
                UpdateAccount();                               // CBACT04C.cbl:220 — unreachable (bug #1)
            }
        }

        CloseTranCatBalFile();                                  // source: CBACT04C.cbl:224
        _sysout.Add("END OF EXECUTION OF PROGRAM CBACT04C");    // source: CBACT04C.cbl:230
        return 0;                                               // GOBACK
    }

    // --- 0000-TCATBALF-OPEN / 0400-TRANFILE-OPEN -----------------------------------------------------
    private void OpenTranCatBalFile() => _tcatBal.StartBrowse();   // OPEN INPUT (position the forward cursor) // COBOL paragraph: 0000-TCATBALF-OPEN

    private void OpenTransactionFile()                            // COBOL paragraph: 0400-TRANFILE-OPEN
    {
        // OPEN OUTPUT TRANSACT-FILE (DISP=NEW): rebuild the target from empty (truncate-then-insert).
        foreach (Transaction t in _transact.ReadAll().ToList())
            _transact.Delete(t.TranId);
    }

    // --- 1000-TCATBALF-GET-NEXT (lines 324-348) ------------------------------------------------------
    private void ReadNextTranCatBal()                            // COBOL paragraph: 1000-TCATBALF-GET-NEXT
    {
        string status = _tcatBal.ReadNext(out TranCatBalance? next);
        if (status == FileStatus.Ok) _tranCatBal = next;
        else if (status == FileStatus.EndOfFile) _endOfFile = true;
        else { _sysout.Add("ERROR READING TRANSACTION CATEGORY FILE"); AbendProgram(status); }
    }

    // --- 1050-UPDATE-ACCOUNT (lines 349-370) ---------------------------------------------------------
    private void UpdateAccount()                                 // COBOL paragraph: 1050-UPDATE-ACCOUNT
    {
        // ADD WS-TOTAL-INT TO ACCT-CURR-BAL (S9(10)V99, truncate/silent overflow); zero the cycle buckets.
        _accountRecord!.CurrBal = Decimals.Store(_accountRecord.CurrBal + _totalInterest, BalDigits, MoneyScale, true);
        _accountRecord.CurrCycCredit = 0m;
        _accountRecord.CurrCycDebit = 0m;
        string status = _account.Update(_accountRecord);
        if (status != FileStatus.Ok) { _sysout.Add("ERROR RE-WRITING ACCOUNT FILE"); AbendProgram(status); }
    }

    // --- 1100-GET-ACCT-DATA (lines 371-391) ----------------------------------------------------------
    private void GetAccountData(long acctId)                     // COBOL paragraph: 1100-GET-ACCT-DATA
    {
        string status = _account.ReadByKey(acctId, out Account? acct);
        if (status != FileStatus.Ok) _sysout.Add("ACCOUNT NOT FOUND: " + acctId.ToString("D11")); // INVALID KEY
        if (status == FileStatus.Ok) _accountRecord = acct;
        // 1100-GET-ACCT-DATA: INVALID KEY DISPLAYs 'ACCOUNT NOT FOUND' (above); then, because the file
        // status is not '00', the ELSE path also DISPLAYs 'ERROR READING ACCOUNT FILE' and abends. Faithful
        // to cbl:372-391 (this is NOT the numbered faithful-bug #2, which is the DISCGRP open-message label).
        else { _sysout.Add("ERROR READING ACCOUNT FILE"); AbendProgram(status); }
    }

    // --- 1110-GET-XREF-DATA (lines 392-413) — alt key (account id) -----------------------------------
    private void GetXrefData(long acctId)                        // COBOL paragraph: 1110-GET-XREF-DATA
    {
        string status = _xref.ReadByAltKey(acctId, out CardXref? xref);
        if (status != FileStatus.Ok) _sysout.Add("ACCOUNT NOT FOUND: " + acctId.ToString("D11")); // INVALID KEY
        if (status == FileStatus.Ok) _xrefRecord = xref;
        else { _sysout.Add("ERROR READING XREF FILE"); AbendProgram(status); }
    }

    // --- 1200-GET-INTEREST-RATE (lines 414-440) ------------------------------------------------------
    private void GetInterestRate()                              // COBOL paragraph: 1200-GET-INTEREST-RATE
    {
        string groupId = _accountRecord!.GroupId;
        string typeCd = _tranCatBal!.TypeCd;
        int catCd = _tranCatBal.CatCd;

        string status = _discGrp.ReadByKey(groupId, typeCd, catCd, out DisclosureGroup? grp);
        if (status != FileStatus.Ok && status != FileStatus.RecordNotFound)
        { _sysout.Add("ERROR READING DISCLOSURE GROUP FILE"); AbendProgram(status); }

        if (status == FileStatus.RecordNotFound) // '23' -> retry with the DEFAULT group
        {
            _sysout.Add("DISCLOSURE GROUP RECORD MISSING");
            _sysout.Add("TRY WITH DEFAULT GROUP CODE");
            GetDefaultIntRate(typeCd, catCd);
        }
        else
        {
            _disGroup = grp;
        }
    }

    // --- 1200-A-GET-DEFAULT-INT-RATE (lines 442-460) -------------------------------------------------
    private void GetDefaultIntRate(string typeCd, int catCd)     // COBOL paragraph: 1200-A-GET-DEFAULT-INT-RATE
    {
        // MOVE 'DEFAULT' TO FD-DIS-ACCT-GROUP-ID — the field is X(10), so the literal is space-padded to 10.
        string status = _discGrp.ReadByKey("DEFAULT".PadRight(10), typeCd, catCd, out DisclosureGroup? grp);
        if (status != FileStatus.Ok) { _sysout.Add("ERROR READING DEFAULT DISCLOSURE GROUP"); AbendProgram(status); }
        _disGroup = grp;
    }

    // --- 1300-COMPUTE-INTEREST (lines 461-470) -------------------------------------------------------
    private void ComputeInterest()                              // COBOL paragraph: 1300-COMPUTE-INTEREST
    {
        decimal bal = _tranCatBal!.TranCatBal;   // S9(09)V99
        decimal rate = _disGroup!.IntRate;       // S9(04)V99
        // COMPUTE WS-MONTHLY-INT = (TRAN-CAT-BAL * DIS-INT-RATE) / 1200 — truncate toward zero, no ROUNDED.
        _monthlyInterest = Decimals.Store(bal * rate / 1200m, IntDigits, MoneyScale, true);
        _totalInterest = Decimals.Store(_totalInterest + _monthlyInterest, IntDigits, MoneyScale, true);
        WriteTransaction();
    }

    // --- 1300-B-WRITE-TX (lines 472-515) -------------------------------------------------------------
    private void WriteTransaction()                             // COBOL paragraph: 1300-B-WRITE-TX
    {
        _tranIdSuffix++;                                         // ADD 1 TO WS-TRANID-SUFFIX
        // STRING PARM-DATE, WS-TRANID-SUFFIX (9(6)) DELIMITED BY SIZE INTO TRAN-ID X(16).
        string tranId = (_parmDate + (_tranIdSuffix % 1_000_000L).ToString("D6"));
        string acctDigits = _accountRecord!.AcctId.ToString("D11");

        var tran = new Transaction
        {
            TranId = tranId.Length >= 16 ? tranId[..16] : tranId.PadRight(16),
            TypeCd = "01",                                       // MOVE '01' TO TRAN-TYPE-CD
            CatCd = 5,                                           // MOVE '05' TO 9(4) -> 0005 (bug #3)
            Source = "System".PadRight(10),                     // MOVE 'System' TO TRAN-SOURCE X(10)
            Desc = ("Int. for a/c " + acctDigits).PadRight(100), // STRING 'Int. for a/c ', ACCT-ID
            Amt = _monthlyInterest,                              // MOVE WS-MONTHLY-INT TO TRAN-AMT
            MerchantId = 0,
            MerchantName = new string(' ', 50),
            MerchantCity = new string(' ', 50),
            MerchantZip = new string(' ', 10),
            CardNum = _xrefRecord!.XrefCardNum,                 // MOVE XREF-CARD-NUM TO TRAN-CARD-NUM
        };
        string ts = Db2FormatTimestamp(_clock.Now);
        tran.OrigTs = ts;
        tran.ProcTs = ts;

        string status = _transact.Insert(tran);
        if (status != FileStatus.Ok) { _sysout.Add("ERROR WRITING TRANSACTION RECORD"); AbendProgram(status); }
    }

    private static void ComputeFees() { /* 1400-COMPUTE-FEES: no-op (bug #5) */ } // COBOL paragraph: 1400-COMPUTE-FEES

    private void CloseTranCatBalFile() => _tcatBal.EndBrowse();  // COBOL paragraph: 9000-TCATBALF-CLOSE

    // --- Z-GET-DB2-FORMAT-TIMESTAMP (lines 613-626): YYYY-MM-DD-HH.MM.SS.hh0000 (bug #4) --------------
    private static string Db2FormatTimestamp(DateTime now)       // COBOL paragraph: Z-GET-DB2-FORMAT-TIMESTAMP
    {
        int hundredths = now.Millisecond / 10;
        return $"{now:yyyy-MM-dd-HH.mm.ss}.{hundredths:D2}0000";
    }

    // --- 9999-ABEND-PROGRAM (lines 627-632) ----------------------------------------------------------
    private void AbendProgram(string status)                    // COBOL paragraph: 9999-ABEND-PROGRAM
    {
        _sysout.Add("ABENDING PROGRAM");
        throw new AbendException("999", $"CBACT04C abend; FILE STATUS '{status}'.");
    }

    /// <summary>Reproduces DISPLAY TRAN-CAT-BAL-RECORD: the 50-byte CVTRA01Y image rendered as text.</summary>
    private static string DisplayTranCatBal(TranCatBalance b)
    {
        string acct = b.AcctId.ToString("D11");
        string type = (b.TypeCd ?? "").PadRight(2)[..2];
        string cat = b.CatCd.ToString("D4");
        string bal = Zoned(b.TranCatBal, 9, 2);   // S9(09)V99 zoned, 11 digit chars
        return acct + type + cat + bal + new string(' ', 22);  // + FILLER X(22)
    }

    /// <summary>
    /// Renders a <c>PIC S9(int)V9(scale)</c> zoned field as <c>DISPLAY</c> of the raw record would: exactly
    /// (int+scale) digit characters. A NEGATIVE value carries the IBM EBCDIC sign overpunch on its low-order
    /// digit (zone 0xD), which renders in the SYSOUT listing as <c>'}'</c> (digit 0) or <c>'J'..'R'</c>
    /// (digits 1..9); a positive/zero value stores an unsigned zone (0xF) and prints as plain digits, exactly
    /// as the all-positive shipped TCATBALF data does.
    /// </summary>
    private static string Zoned(decimal value, int intDigits, int scale)
    {
        decimal stored = Decimals.Store(value, intDigits, scale, true);
        long unscaled = (long)decimal.Truncate(Math.Abs(stored) * Decimals.Pow10(scale));
        string digits = unscaled.ToString("D" + (intDigits + scale));
        if (stored < 0m)
        {
            const string neg = "}JKLMNOPQR"; // EBCDIC 0xD0..0xD9 overpunch for negative digits 0..9
            digits = digits[..^1] + neg[digits[^1] - '0'];
        }
        return digits;
    }
}
