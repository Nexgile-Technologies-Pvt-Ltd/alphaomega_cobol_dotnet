using CardDemo.Cobol.Runtime;
using CardDemo.Data;

namespace CardDemo.Batch;

/// <summary>
/// Faithful port of the batch program <c>CBTRN02C</c> (daily transaction posting). It reads the daily
/// transaction file, validates each record (card cross-reference, account, credit limit, expiration),
/// and either posts it (update category balance + account balance, write the transaction) or writes it
/// to the rejects file with a validation trailer. RETURN-CODE is 4 if any record was rejected.
/// </summary>
/// <remarks>
/// Ported paragraph-by-paragraph from <c>app/cbl/CBTRN02C.cbl</c> (method names mirror paragraph names;
/// comments cite source lines). No PARM. Validation reason codes: 100 invalid card, 101 account not
/// found, 102 overlimit, 103 expired (102 then 103 can overwrite, matching the source).
/// </remarks>
public sealed class Cbtrn02cTransactionPoster(Cbtrn02cContext ctx)
{
    private readonly Cbtrn02cContext _ctx = ctx;
    private readonly List<string> _sysout = [];

    private long _wsTransactionCount;
    private long _wsRejectCount;
    private int _failReason;
    private string _failDesc = new(' ', 76);
    private bool _endOfFile;

    private byte[] _dalyImage = [];          // raw 350-byte daily transaction image (for the reject file)
    private FixedRecord? _dalyTran;          // DALYTRAN-RECORD (CVTRA06Y)
    private FixedRecord? _xref;              // CARD-XREF-RECORD (CVACT03Y)
    private FixedRecord? _account;           // ACCOUNT-RECORD   (CVACT01Y)
    private FixedRecord? _tcatBal;           // TRAN-CAT-BAL-RECORD (CVTRA01Y)
    private bool _createTcatRec;             // WS-CREATE-TRANCAT-REC

    public IReadOnlyList<string> Sysout => _sysout;

    /// <summary>Runs the program; returns the RETURN-CODE (4 if any record was rejected, else 0).</summary>
    public int Run()
    {
        _sysout.Add("START OF EXECUTION OF PROGRAM CBTRN02C");
        _ctx.DalyTran.OpenInput();   // 0000-DALYTRAN-OPEN
        _ctx.Transact.Clear();       // 0100-TRANFILE-OPEN (OUTPUT)
        // 0200 XREF, 0400 ACCT, 0500 TCATBAL are random/I-O: nothing to position.
        _ctx.DalyRejs.OpenOutput();  // 0300-DALYREJS-OPEN (OUTPUT)

        // PERFORM UNTIL END-OF-FILE = 'Y' (lines 202-219).
        while (!_endOfFile)
        {
            if (!_endOfFile)
            {
                Get1000DalyTranNext();
                if (!_endOfFile)
                {
                    _wsTransactionCount++;
                    _failReason = 0;                     // MOVE 0 TO WS-VALIDATION-FAIL-REASON
                    _failDesc = new string(' ', 76);     // MOVE SPACES TO ...-DESC
                    Validate1500Tran();
                    if (_failReason == 0) Post2000Transaction();
                    else { _wsRejectCount++; WriteReject2500(); }
                }
            }
        }

        _sysout.Add("TRANSACTIONS PROCESSED :" + _wsTransactionCount.ToString("D9"));
        _sysout.Add("TRANSACTIONS REJECTED  :" + _wsRejectCount.ToString("D9"));
        int returnCode = _wsRejectCount > 0 ? 4 : 0;
        _sysout.Add("END OF EXECUTION OF PROGRAM CBTRN02C");
        return returnCode;
    }

    // --- 1000-DALYTRAN-GET-NEXT (lines 345-369) --------------------------------------------------
    private void Get1000DalyTranNext()
    {
        string status = _ctx.DalyTran.Read(out byte[]? image);
        if (status == FileStatus.Ok)
        {
            _dalyImage = image!;
            _dalyTran = FixedRecord.Parse(_ctx.DalyTranLayout, image!, _ctx.Host);
        }
        else if (status == FileStatus.EndOfFile) _endOfFile = true;
        else { _sysout.Add("ERROR READING DALYTRAN FILE"); Abend9999(status); }
    }

    // --- 1500-VALIDATE-TRAN (lines 370-378) ------------------------------------------------------
    private void Validate1500Tran()
    {
        LookupA1500Xref();
        if (_failReason == 0) LookupB1500Acct();
    }

    // --- 1500-A-LOOKUP-XREF (lines 380-392) — read XREF by card number (primary key) --------------
    private void LookupA1500Xref()
    {
        byte[] key = HostEncoding.For(_ctx.Host).GetBytes(_dalyTran!.GetText("DALYTRAN-CARD-NUM"));
        if (_ctx.Xref.Read(key, out byte[]? image) != FileStatus.Ok)
        {
            _failReason = 100;
            _failDesc = "INVALID CARD NUMBER FOUND";
        }
        else _xref = FixedRecord.Parse(_ctx.XrefLayout, image!, _ctx.Host);
    }

    // --- 1500-B-LOOKUP-ACCT (lines 393-422) ------------------------------------------------------
    private void LookupB1500Acct()
    {
        string acctId = AcctIdText(_xref!, "XREF-ACCT-ID");
        if (_ctx.Account.Read(ZonedKey(acctId), out byte[]? image) != FileStatus.Ok)
        {
            _failReason = 101;
            _failDesc = "ACCOUNT RECORD NOT FOUND";
            return;
        }
        _account = FixedRecord.Parse(_ctx.AccountLayout, image!, _ctx.Host);

        decimal amt = _dalyTran!.GetNumber("DALYTRAN-AMT");
        // COMPUTE WS-TEMP-BAL = ACCT-CURR-CYC-CREDIT - ACCT-CURR-CYC-DEBIT + DALYTRAN-AMT
        decimal tempBal = Decimals.Store(
            _account.GetNumber("ACCT-CURR-CYC-CREDIT") - _account.GetNumber("ACCT-CURR-CYC-DEBIT") + amt,
            integerDigits: 9, scale: 2, signed: true);

        if (_account.GetNumber("ACCT-CREDIT-LIMIT") < tempBal)
        {
            _failReason = 102;
            _failDesc = "OVERLIMIT TRANSACTION";
        }
        // Alphanumeric date compare: ACCT-EXPIRAION-DATE >= DALYTRAN-ORIG-TS(1:10)
        string expir = _account.GetText("ACCT-EXPIRAION-DATE");
        string origDate = _dalyTran.GetText("DALYTRAN-ORIG-TS")[..10];
        if (string.CompareOrdinal(expir, origDate) < 0)
        {
            _failReason = 103;
            _failDesc = "TRANSACTION RECEIVED AFTER ACCT EXPIRATION";
        }
    }

    // --- 2000-POST-TRANSACTION (lines 424-444) ---------------------------------------------------
    private void Post2000Transaction()
    {
        FixedRecord tran = FixedRecord.CreateBlank(_ctx.TranLayout, _ctx.Host);
        Copy(tran, "TRAN-ID", "DALYTRAN-ID");
        Copy(tran, "TRAN-TYPE-CD", "DALYTRAN-TYPE-CD");
        CopyNum(tran, "TRAN-CAT-CD", "DALYTRAN-CAT-CD");
        Copy(tran, "TRAN-SOURCE", "DALYTRAN-SOURCE");
        Copy(tran, "TRAN-DESC", "DALYTRAN-DESC");
        CopyNum(tran, "TRAN-AMT", "DALYTRAN-AMT");
        CopyNum(tran, "TRAN-MERCHANT-ID", "DALYTRAN-MERCHANT-ID");
        Copy(tran, "TRAN-MERCHANT-NAME", "DALYTRAN-MERCHANT-NAME");
        Copy(tran, "TRAN-MERCHANT-CITY", "DALYTRAN-MERCHANT-CITY");
        Copy(tran, "TRAN-MERCHANT-ZIP", "DALYTRAN-MERCHANT-ZIP");
        Copy(tran, "TRAN-CARD-NUM", "DALYTRAN-CARD-NUM");
        Copy(tran, "TRAN-ORIG-TS", "DALYTRAN-ORIG-TS");
        tran.SetText("TRAN-PROC-TS", Db2FormatTimestamp(_ctx.Clock.Now));

        UpdateTcatBal2700();
        UpdateAccount2800();
        WriteTransaction2900(tran);
    }

    // --- 2500-WRITE-REJECT-REC (lines 446-465) ---------------------------------------------------
    private void WriteReject2500()
    {
        var reject = new byte[430];
        _dalyImage.CopyTo(reject, 0);                              // REJECT-TRAN-DATA = DALYTRAN-RECORD
        ZonedDecimalCodec.Encode(_failReason, reject.AsSpan(350, 4), 4, 0, false, _ctx.Host);
        HostEncoding.For(_ctx.Host).GetBytes(_failDesc.PadRight(76)[..76]).CopyTo(reject, 354);
        if (_ctx.DalyRejs.Write(reject) != FileStatus.Ok) { _sysout.Add("ERROR WRITING TO REJECTS FILE"); Abend9999("12"); }
    }

    // --- 2700-UPDATE-TCATBAL (lines 467-501) -----------------------------------------------------
    private void UpdateTcatBal2700()
    {
        string acctId = AcctIdText(_xref!, "XREF-ACCT-ID");
        string typeCd = _dalyTran!.GetText("DALYTRAN-TYPE-CD");
        decimal catCd = _dalyTran.GetNumber("DALYTRAN-CAT-CD");
        byte[] key = TcatBalKey(acctId, typeCd, catCd);

        _createTcatRec = false;
        string status = _ctx.TcatBal.Read(key, out byte[]? image);
        if (status == FileStatus.RecordNotFound) _createTcatRec = true;     // INVALID KEY -> create
        else if (status == FileStatus.Ok) _tcatBal = FixedRecord.Parse(_ctx.TcatBalLayout, image!, _ctx.Host);
        else { _sysout.Add("ERROR READING TRANSACTION BALANCE FILE"); Abend9999(status); }

        if (_createTcatRec) CreateA2700TcatBalRec(acctId, typeCd, catCd);
        else UpdateB2700TcatBalRec();
    }

    // --- 2700-A-CREATE-TCATBAL-REC (lines 503-524) -----------------------------------------------
    private void CreateA2700TcatBalRec(string acctId, string typeCd, decimal catCd)
    {
        FixedRecord rec = FixedRecord.CreateInitialized(_ctx.TcatBalLayout, _ctx.Host); // INITIALIZE
        rec.SetNumber("TRANCAT-ACCT-ID", decimal.Parse(acctId));
        rec.SetText("TRANCAT-TYPE-CD", typeCd);
        rec.SetNumber("TRANCAT-CD", catCd);
        rec.SetNumber("TRAN-CAT-BAL", _dalyTran!.GetNumber("DALYTRAN-AMT")); // 0 + amt
        if (_ctx.TcatBal.Write(rec.ToBytes()) != FileStatus.Ok) { _sysout.Add("ERROR WRITING TRANSACTION BALANCE FILE"); Abend9999("12"); }
    }

    // --- 2700-B-UPDATE-TCATBAL-REC (lines 526-542) -----------------------------------------------
    private void UpdateB2700TcatBalRec()
    {
        decimal newBal = _tcatBal!.GetNumber("TRAN-CAT-BAL") + _dalyTran!.GetNumber("DALYTRAN-AMT");
        _tcatBal.SetNumber("TRAN-CAT-BAL", newBal);
        if (_ctx.TcatBal.Rewrite(_tcatBal.ToBytes()) != FileStatus.Ok) { _sysout.Add("ERROR REWRITING TRANSACTION BALANCE FILE"); Abend9999("12"); }
    }

    // --- 2800-UPDATE-ACCOUNT-REC (lines 545-560) -------------------------------------------------
    private void UpdateAccount2800()
    {
        decimal amt = _dalyTran!.GetNumber("DALYTRAN-AMT");
        _account!.SetNumber("ACCT-CURR-BAL", _account.GetNumber("ACCT-CURR-BAL") + amt);
        if (amt >= 0m)
            _account.SetNumber("ACCT-CURR-CYC-CREDIT", _account.GetNumber("ACCT-CURR-CYC-CREDIT") + amt);
        else
            _account.SetNumber("ACCT-CURR-CYC-DEBIT", _account.GetNumber("ACCT-CURR-CYC-DEBIT") + amt);

        if (_ctx.Account.Rewrite(_account.ToBytes()) != FileStatus.Ok) // INVALID KEY -> reason 109
        {
            _failReason = 109;
            _failDesc = "ACCOUNT RECORD NOT FOUND";
        }
    }

    // --- 2900-WRITE-TRANSACTION-FILE (lines 562-579) ---------------------------------------------
    private void WriteTransaction2900(FixedRecord tran)
    {
        if (_ctx.Transact.Write(tran.ToBytes()) != FileStatus.Ok) { _sysout.Add("ERROR WRITING TO TRANSACTION FILE"); Abend9999("12"); }
    }

    // --- Z-GET-DB2-FORMAT-TIMESTAMP (lines 692-705) ----------------------------------------------
    private static string Db2FormatTimestamp(DateTime now)
    {
        int hundredths = now.Millisecond / 10;
        return $"{now:yyyy-MM-dd-HH.mm.ss}.{hundredths:D2}0000";
    }

    private void Abend9999(string status)
    {
        _sysout.Add("ABENDING PROGRAM");
        throw new AbendException("999", $"CBTRN02C abend; FILE STATUS '{status}'.");
    }

    private void Copy(FixedRecord dst, string dstField, string srcField) => dst.SetText(dstField, _dalyTran!.GetText(srcField));
    private void CopyNum(FixedRecord dst, string dstField, string srcField) => dst.SetNumber(dstField, _dalyTran!.GetNumber(srcField));

    private static string AcctIdText(FixedRecord rec, string field) => ((long)rec.GetNumber(field)).ToString("D11");

    private byte[] ZonedKey(string acctId)
    {
        var key = new byte[11];
        ZonedDecimalCodec.Encode(decimal.Parse(acctId), key, 11, 0, false, _ctx.Host);
        return key;
    }

    private byte[] TcatBalKey(string acctId, string typeCd, decimal catCd) =>
        FixedRecord.CreateBlank(_ctx.TcatBalLayout, _ctx.Host)
            .SetNumber("TRANCAT-ACCT-ID", decimal.Parse(acctId))
            .SetText("TRANCAT-TYPE-CD", typeCd)
            .SetNumber("TRANCAT-CD", catCd)
            .ToBytes()[..17];
}

/// <summary>Inputs for <see cref="Cbtrn02cTransactionPoster"/>: file accessors, record layouts, clock, and host.</summary>
public sealed record Cbtrn02cContext(
    SequentialFile DalyTran,
    VsamFile Transact,
    VsamFile Xref,
    SequentialFile DalyRejs,
    VsamFile Account,
    VsamFile TcatBal,
    RecordLayout DalyTranLayout,
    RecordLayout TranLayout,
    RecordLayout XrefLayout,
    RecordLayout AccountLayout,
    RecordLayout TcatBalLayout,
    IClock Clock,
    HostKind Host);
