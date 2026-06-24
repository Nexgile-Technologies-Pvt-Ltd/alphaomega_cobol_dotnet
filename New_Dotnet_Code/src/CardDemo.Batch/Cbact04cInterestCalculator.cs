using CardDemo.Cobol.Runtime;
using CardDemo.Data;

namespace CardDemo.Batch;

/// <summary>
/// Faithful port of the batch program <c>CBACT04C</c> (interest calculator). It reads transaction
/// category balances in key order, looks up each account's disclosure-group interest rate, computes
/// monthly interest <c>(TRAN-CAT-BAL * DIS-INT-RATE) / 1200</c> (truncated, no rounding), writes an
/// interest transaction per category, and accumulates interest into the account balance.
/// </summary>
/// <remarks>
/// Ported paragraph-by-paragraph from <c>app/cbl/CBACT04C.cbl</c> (method names mirror paragraph
/// names; comments cite source lines). Notable faithful behaviour preserved exactly:
/// <list type="bullet">
/// <item><description>The main <c>PERFORM UNTIL</c> tests before each iteration, so the <c>ELSE</c>
/// branch that would post the final account's accumulated interest (CBACT04C line 220) is structurally
/// unreachable — the <em>last</em> account's balance is never updated (its interest transactions are
/// still written). Reproduced, not fixed.</description></item>
/// <item><description><c>1400-COMPUTE-FEES</c> is an unimplemented no-op (line 518).</description></item>
/// </list>
/// </remarks>
public sealed class Cbact04cInterestCalculator(Cbact04cContext ctx)
{
    private readonly Cbact04cContext _ctx = ctx;
    private readonly List<string> _sysout = [];

    // WORKING-STORAGE (CBACT04C lines 166-173)
    private string _wsLastAcctNum = new(' ', 11);  // WS-LAST-ACCT-NUM PIC X(11) VALUE SPACES
    private decimal _wsMonthlyInt;                  // WS-MONTHLY-INT  PIC S9(09)V99
    private decimal _wsTotalInt;                    // WS-TOTAL-INT    PIC S9(09)V99
    private bool _wsFirstTime = true;              // WS-FIRST-TIME   PIC X VALUE 'Y'
    private long _wsRecordCount;                    // WS-RECORD-COUNT
    private long _wsTranidSuffix;                   // WS-TRANID-SUFFIX
    private bool _endOfFile;                        // END-OF-FILE = 'Y'/'N'

    // "Current" records (the WS copies the COBOL READ ... INTO populates).
    private FixedRecord? _tranCatBal;  // TRAN-CAT-BAL-RECORD (CVTRA01Y)
    private FixedRecord? _account;     // ACCOUNT-RECORD      (CVACT01Y)
    private FixedRecord? _xref;        // CARD-XREF-RECORD    (CVACT03Y)
    private FixedRecord? _disGroup;    // DIS-GROUP-RECORD    (CVTRA02Y)

    /// <summary>Console (DISPLAY) lines produced by the run, in order.</summary>
    public IReadOnlyList<string> Sysout => _sysout;

    /// <summary>Runs the program; returns the process RETURN-CODE (CBACT04C leaves it 0).</summary>
    public int Run()
    {
        _sysout.Add("START OF EXECUTION OF PROGRAM CBACT04C");          // line 181
        Open0000TcatbalF();
        Open0100XrefFile();
        Open0200DiscGrp();
        Open0300AcctFile();
        Open0400TranFile();

        // PERFORM UNTIL END-OF-FILE = 'Y'  (lines 188-222). Test-before loop: the ELSE at line 220 is
        // unreachable, so the final account is intentionally never updated here.
        while (!_endOfFile)
        {
            if (!_endOfFile) // IF END-OF-FILE = 'N' (line 189) — always true on entry
            {
                Get1000TcatbalFNext();
                if (!_endOfFile) // IF END-OF-FILE = 'N' (line 191)
                {
                    _wsRecordCount++;                                    // line 192
                    _sysout.Add(DecodeTcatbalDisplay());                 // DISPLAY TRAN-CAT-BAL-RECORD (line 193)

                    string acctId = AcctIdText(_tranCatBal!, "TRANCAT-ACCT-ID");
                    if (acctId != _wsLastAcctNum)                        // line 194
                    {
                        if (!_wsFirstTime) Update1050Account();         // lines 195-196
                        else _wsFirstTime = false;                       // line 198
                        _wsTotalInt = 0m;                                // line 200
                        _wsLastAcctNum = acctId;                         // line 201
                        Get1100AcctData(acctId);                         // lines 202-203
                        Get1110XrefData(acctId);                         // lines 204-205
                    }

                    Get1200InterestRate();                              // lines 210-213
                    if (_disGroup!.GetNumber("DIS-INT-RATE") != 0m)     // line 214
                    {
                        Compute1300Interest();                          // line 215
                        Compute1400Fees();                              // line 216 (no-op)
                    }
                }
            }
            else
            {
                Update1050Account(); // CBACT04C line 220 — unreachable (preserved for fidelity)
            }
        }

        Close9000TcatbalF();
        Close9100XrefFile();
        Close9200DiscGrp();
        Close9300AcctFile();
        Close9400TranFile();
        _sysout.Add("END OF EXECUTION OF PROGRAM CBACT04C");            // line 230
        return 0;                                                       // GOBACK, RETURN-CODE defaults to 0
    }

    // --- 0000/0100/0200/0300/0400 file opens (lines 234-323) -------------------------------------
    // The SQLite-backed files are already created/loaded; OPEN succeeds. Sequential input positions a
    // browse; OUTPUT clears the target.
    private void Open0000TcatbalF() => _ctx.TcatBal.StartBrowse();
    private void Open0100XrefFile() { /* random access; nothing to position */ }
    private void Open0200DiscGrp() { /* random access */ }
    private void Open0300AcctFile() { /* random access, I-O */ }
    private void Open0400TranFile() => _ctx.Transact.OpenOutput();

    // --- 1000-TCATBALF-GET-NEXT (lines 325-348) --------------------------------------------------
    private void Get1000TcatbalFNext()
    {
        string status = _ctx.TcatBal.ReadNext(out byte[]? image);
        if (status == FileStatus.Ok)
        {
            _tranCatBal = FixedRecord.Parse(_ctx.TcatBalLayout, image!, _ctx.Host);
        }
        else if (status == FileStatus.EndOfFile)
        {
            _endOfFile = true;
        }
        else
        {
            _sysout.Add("ERROR READING TRANSACTION CATEGORY FILE");
            Abend9999(status);
        }
    }

    // --- 1050-UPDATE-ACCOUNT (lines 350-370) -----------------------------------------------------
    private void Update1050Account()
    {
        // ADD WS-TOTAL-INT TO ACCT-CURR-BAL; reset cycle credit/debit; REWRITE.
        decimal newBal = _account!.GetNumber("ACCT-CURR-BAL") + _wsTotalInt;
        _account.SetNumber("ACCT-CURR-BAL", newBal);
        _account.SetNumber("ACCT-CURR-CYC-CREDIT", 0m);
        _account.SetNumber("ACCT-CURR-CYC-DEBIT", 0m);

        string status = _ctx.Account.Rewrite(_account.ToBytes(_ctx.Host));
        if (status != FileStatus.Ok)
        {
            _sysout.Add("ERROR RE-WRITING ACCOUNT FILE");
            Abend9999(status);
        }
    }

    // --- 1100-GET-ACCT-DATA (lines 372-391) ------------------------------------------------------
    private void Get1100AcctData(string acctId)
    {
        byte[] key = ZonedKey(acctId);
        string status = _ctx.Account.Read(key, out byte[]? image);
        if (status != FileStatus.Ok) // INVALID KEY path then status check
            _sysout.Add("ACCOUNT NOT FOUND: " + acctId);
        if (status == FileStatus.Ok)
            _account = FixedRecord.Parse(_ctx.AccountLayout, image!, _ctx.Host);
        else
        {
            _sysout.Add("ERROR READING ACCOUNT FILE");
            Abend9999(status);
        }
    }

    // --- 1110-GET-XREF-DATA (lines 393-413) — read by ALTERNATE key (account id) ------------------
    private void Get1110XrefData(string acctId)
    {
        byte[] altKey = ZonedKey(acctId);
        string status = _ctx.Xref.ReadByAlternateKey(altKey, out byte[]? image);
        if (status != FileStatus.Ok)
            _sysout.Add("ACCOUNT NOT FOUND: " + acctId);
        if (status == FileStatus.Ok)
            _xref = FixedRecord.Parse(_ctx.XrefLayout, image!, _ctx.Host);
        else
        {
            _sysout.Add("ERROR READING XREF FILE");
            Abend9999(status);
        }
    }

    // --- 1200-GET-INTEREST-RATE (lines 415-440) --------------------------------------------------
    private void Get1200InterestRate()
    {
        string groupId = _account!.GetText("ACCT-GROUP-ID");
        string typeCd = _tranCatBal!.GetText("TRANCAT-TYPE-CD");
        decimal catCd = _tranCatBal.GetNumber("TRANCAT-CD");

        string status = ReadDiscGrp(groupId, typeCd, catCd);
        if (status != FileStatus.Ok && status != FileStatus.RecordNotFound)
        {
            _sysout.Add("ERROR READING DISCLOSURE GROUP FILE");
            Abend9999(status);
        }
        if (status == FileStatus.RecordNotFound) // '23' -> retry with the DEFAULT group
        {
            _sysout.Add("DISCLOSURE GROUP RECORD MISSING");
            _sysout.Add("TRY WITH DEFAULT GROUP CODE");
            GetA1200DefaultIntRate(typeCd, catCd);
        }
    }

    // --- 1200-A-GET-DEFAULT-INT-RATE (lines 443-460) ---------------------------------------------
    private void GetA1200DefaultIntRate(string typeCd, decimal catCd)
    {
        string status = ReadDiscGrp("DEFAULT", typeCd, catCd);
        if (status != FileStatus.Ok)
        {
            _sysout.Add("ERROR READING DEFAULT DISCLOSURE GROUP");
            Abend9999(status);
        }
    }

    private string ReadDiscGrp(string groupId, string typeCd, decimal catCd)
    {
        FixedRecord keyRec = FixedRecord.CreateBlank(_ctx.DiscGrpLayout)
            .SetText("DIS-ACCT-GROUP-ID", groupId)
            .SetText("DIS-TRAN-TYPE-CD", typeCd)
            .SetNumber("DIS-TRAN-CAT-CD", catCd);
        byte[] key = keyRec.ToBytes(_ctx.Host)[..16];

        string status = _ctx.DiscGrp.Read(key, out byte[]? image);
        if (status == FileStatus.Ok)
            _disGroup = FixedRecord.Parse(_ctx.DiscGrpLayout, image!, _ctx.Host);
        return status;
    }

    // --- 1300-COMPUTE-INTEREST (lines 462-470) ---------------------------------------------------
    private void Compute1300Interest()
    {
        decimal tranCatBal = _tranCatBal!.GetNumber("TRAN-CAT-BAL");   // S9(09)V99
        decimal rate = _disGroup!.GetNumber("DIS-INT-RATE");           // S9(04)V99
        // COMPUTE WS-MONTHLY-INT = (TRAN-CAT-BAL * DIS-INT-RATE) / 1200  — truncated, no ROUNDED.
        _wsMonthlyInt = Decimals.Store(tranCatBal * rate / 1200m, integerDigits: 9, scale: 2, signed: true);
        _wsTotalInt = Decimals.Store(_wsTotalInt + _wsMonthlyInt, integerDigits: 9, scale: 2, signed: true);
        WriteB1300Tx();
    }

    // --- 1300-B-WRITE-TX (lines 473-515) ---------------------------------------------------------
    private void WriteB1300Tx()
    {
        _wsTranidSuffix++;                                              // ADD 1 TO WS-TRANID-SUFFIX
        FixedRecord tran = FixedRecord.CreateBlank(_ctx.TranLayout);

        // STRING PARM-DATE, WS-TRANID-SUFFIX DELIMITED BY SIZE INTO TRAN-ID
        tran.SetText("TRAN-ID", _ctx.ParmDate.PadRight(10).Substring(0, 10) + _wsTranidSuffix.ToString("D6"));
        tran.SetText("TRAN-TYPE-CD", "01");
        tran.SetNumber("TRAN-CAT-CD", 5m); // MOVE '05' to 9(4) -> unsigned integer 5 (0005)
        tran.SetText("TRAN-SOURCE", "System");

        string acctId = AcctIdText(_account!, "ACCT-ID");
        tran.SetText("TRAN-DESC", "Int. for a/c " + acctId);            // STRING into X(100), rest spaces
        tran.SetNumber("TRAN-AMT", _wsMonthlyInt);
        tran.SetNumber("TRAN-MERCHANT-ID", 0m);
        tran.SetText("TRAN-MERCHANT-NAME", "");
        tran.SetText("TRAN-MERCHANT-CITY", "");
        tran.SetText("TRAN-MERCHANT-ZIP", "");
        tran.SetText("TRAN-CARD-NUM", _xref!.GetText("XREF-CARD-NUM"));

        string ts = Db2FormatTimestamp(_ctx.Clock.Now);                // Z-GET-DB2-FORMAT-TIMESTAMP
        tran.SetText("TRAN-ORIG-TS", ts);
        tran.SetText("TRAN-PROC-TS", ts);

        string status = _ctx.Transact.Write(tran.ToBytes(_ctx.Host));
        if (status != FileStatus.Ok)
        {
            _sysout.Add("ERROR WRITING TRANSACTION RECORD");
            Abend9999(status);
        }
    }

    private static void Compute1400Fees() { /* 1400-COMPUTE-FEES: to be implemented (no-op) */ }

    // --- 9000-9400 closes (lines 522-611): SQLite files need no explicit close --------------------
    private void Close9000TcatbalF() => _ctx.TcatBal.EndBrowse();
    private void Close9100XrefFile() { }
    private void Close9200DiscGrp() { }
    private void Close9300AcctFile() { }
    private void Close9400TranFile() { }

    // --- Z-GET-DB2-FORMAT-TIMESTAMP (lines 613-626) ----------------------------------------------
    // Builds "YYYY-MM-DD-HH.MM.SS.hh0000" from FUNCTION CURRENT-DATE (hh = hundredths of a second).
    private static string Db2FormatTimestamp(DateTime now)
    {
        int hundredths = now.Millisecond / 10;
        return $"{now:yyyy-MM-dd-HH.mm.ss}.{hundredths:D2}0000";
    }

    // --- 9999-ABEND-PROGRAM (lines 628-632) ------------------------------------------------------
    private void Abend9999(string status)
    {
        _sysout.Add("ABENDING PROGRAM");
        throw new AbendException("999", $"CBACT04C abend; FILE STATUS '{status}'.");
    }

    // Helpers --------------------------------------------------------------------------------------

    /// <summary>The character representation of an unsigned numeric account-id field (zero-padded digits).</summary>
    private static string AcctIdText(FixedRecord rec, string field) => ((long)rec.GetNumber(field)).ToString("D11");

    /// <summary>Builds an 11-byte zoned (DISPLAY) key for an account id in the configured host encoding.</summary>
    private byte[] ZonedKey(string acctId)
    {
        var key = new byte[11];
        ZonedDecimalCodec.Encode(decimal.Parse(acctId), key, totalDigits: 11, scale: 0, signed: false, _ctx.Host);
        return key;
    }

    private string DecodeTcatbalDisplay() =>
        HostEncoding.For(_ctx.Host).GetString(_tranCatBal!.ToBytes(_ctx.Host));
}

/// <summary>Inputs for <see cref="Cbact04cInterestCalculator"/>: the file accessors, record layouts, clock, host, and PARM date.</summary>
public sealed record Cbact04cContext(
    VsamFile TcatBal,
    VsamFile Xref,
    VsamFile Account,
    VsamFile DiscGrp,
    SequentialFile Transact,
    RecordLayout TcatBalLayout,
    RecordLayout XrefLayout,
    RecordLayout AccountLayout,
    RecordLayout DiscGrpLayout,
    RecordLayout TranLayout,
    IClock Clock,
    HostKind Host,
    string ParmDate);
