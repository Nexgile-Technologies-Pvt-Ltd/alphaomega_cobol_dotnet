using CardDemo.Cobol.Runtime;
using CardDemo.Data;

namespace CardDemo.Batch;

/// <summary>
/// Faithful port of <c>CBTRN03C</c> — prints the transaction detail report. It reads posted
/// transactions sequentially, filters by the date-range parameters, looks up the card cross-reference,
/// transaction type and category, and writes a paginated 133-column report with page, account and grand
/// totals.
/// </summary>
/// <remarks>
/// Ported from <c>app/cbl/CBTRN03C.cbl</c> and the report layout <c>CVTRA07Y.cpy</c>. Faithful quirks:
/// the date filter uses <c>NEXT SENTENCE</c>, which exits the whole read loop on the first out-of-range
/// transaction; a new page is started every <c>WS-PAGE-SIZE</c>=20 report lines via
/// <c>MOD(line-counter, 20)=0</c>; account totals break when the card number changes.
/// </remarks>
public sealed class Cbtrn03cReporter(Cbtrn03cContext ctx)
{
    private const string Minus = "-ZZZ,ZZZ,ZZZ.ZZ";
    private const string Plus = "+ZZZ,ZZZ,ZZZ.ZZ";

    private readonly Cbtrn03cContext _ctx = ctx;
    private readonly List<string> _sysout = [];

    private bool _eof;
    private bool _firstTime = true;
    private long _lineCounter;
    private const long PageSize = 20;
    private decimal _pageTotal;
    private decimal _accountTotal;
    private decimal _grandTotal;
    private string _currCardNum = new(' ', 16);

    private FixedRecord? _tran;
    private FixedRecord? _xref;
    private FixedRecord? _tranType;
    private FixedRecord? _tranCatg;

    public IReadOnlyList<string> Sysout => _sysout;

    public IReadOnlyList<string> Run()
    {
        _sysout.Add("START OF EXECUTION OF PROGRAM CBTRN03C");
        _ctx.Transact.OpenInput();
        _ctx.Report.OpenOutput();

        // 0550-DATEPARM-READ
        _sysout.Add("Reporting from " + _ctx.StartDate + " to " + _ctx.EndDate);

        // Main loop (lines 170-206).
        while (!_eof)
        {
            Get1000TranNext();
            string procDate = _tran!.GetText("TRAN-PROC-TS")[..10];
            if (!(string.CompareOrdinal(procDate, _ctx.StartDate) >= 0 &&
                  string.CompareOrdinal(procDate, _ctx.EndDate) <= 0))
                break; // NEXT SENTENCE -> leaves the read loop

            if (!_eof)
            {
                _sysout.Add(Decode(_tran));                          // DISPLAY TRAN-RECORD
                string cardNum = _tran.GetText("TRAN-CARD-NUM");
                if (_currCardNum != cardNum)
                {
                    if (!_firstTime) WriteAccountTotals1120();
                    _currCardNum = cardNum;
                    LookupXref1500A(cardNum);
                }
                LookupTranType1500B(_tran.GetText("TRAN-TYPE-CD"));
                LookupTranCatg1500C(_tran.GetText("TRAN-TYPE-CD"), _tran.GetNumber("TRAN-CAT-CD"));
                WriteTransactionReport1100();
            }
            else
            {
                _pageTotal += _tran.GetNumber("TRAN-AMT");
                _accountTotal += _tran.GetNumber("TRAN-AMT");
                WritePageTotals1110();
                WriteGrandTotals1110();
            }
        }

        _sysout.Add("END OF EXECUTION OF PROGRAM CBTRN03C");
        return _sysout;
    }

    private void Get1000TranNext()
    {
        string status = _ctx.Transact.Read(out byte[]? image);
        if (status == FileStatus.Ok) _tran = FixedRecord.Parse(_ctx.TranLayout, image!, _ctx.Host);
        else if (status == FileStatus.EndOfFile) _eof = true;
        else { _sysout.Add("ERROR READING TRANSACTION FILE"); Abend(); }
    }

    // 1100-WRITE-TRANSACTION-REPORT (lines 274-290)
    private void WriteTransactionReport1100()
    {
        if (_firstTime)
        {
            _firstTime = false;
            WriteHeaders1120();
        }
        if (_lineCounter % PageSize == 0)
        {
            WritePageTotals1110();
            WriteHeaders1120();
        }
        _pageTotal += _tran!.GetNumber("TRAN-AMT");
        _accountTotal += _tran.GetNumber("TRAN-AMT");
        WriteDetail1120();
    }

    // 1110-WRITE-PAGE-TOTALS (lines 293-304)
    private void WritePageTotals1110()
    {
        WriteReport(Pad("Page Total".PadRight(11) + new string('.', 86) + Edit(_pageTotal, Plus)));
        _grandTotal += _pageTotal;
        _pageTotal = 0m;
        _lineCounter++;
        WriteReport(Header2());
        _lineCounter++;
    }

    // 1120-WRITE-ACCOUNT-TOTALS (lines 306-316)
    private void WriteAccountTotals1120()
    {
        WriteReport(Pad("Account Total".PadRight(13) + new string('.', 84) + Edit(_accountTotal, Plus)));
        _accountTotal = 0m;
        _lineCounter++;
        WriteReport(Header2());
        _lineCounter++;
    }

    // 1110-WRITE-GRAND-TOTALS (lines 318-322)
    private void WriteGrandTotals1110() =>
        WriteReport(Pad("Grand Total".PadRight(11) + new string('.', 86) + Edit(_grandTotal, Plus)));

    // 1120-WRITE-HEADERS (lines 324-341)
    private void WriteHeaders1120()
    {
        WriteReport(Pad("DALYREPT".PadRight(38) + "Daily Transaction Report".PadRight(41) +
                        "Date Range: " + _ctx.StartDate.PadRight(10) + " to " + _ctx.EndDate.PadRight(10)));
        _lineCounter++;
        WriteReport(new string(' ', 133));
        _lineCounter++;
        WriteReport(Header1());
        _lineCounter++;
        WriteReport(Header2());
        _lineCounter++;
    }

    // 1120-WRITE-DETAIL (lines 361-374)
    private void WriteDetail1120()
    {
        string acctId = ((long)_xref!.GetNumber("XREF-ACCT-ID")).ToString("D11");
        string typeDesc = _tranType!.GetText("TRAN-TYPE-DESC")[..15];
        string catDesc = _tranCatg!.GetText("TRAN-CAT-TYPE-DESC")[..29];
        string catCd = ((long)_tran!.GetNumber("TRAN-CAT-CD")).ToString("D4");

        string line =
            _tran.GetText("TRAN-ID").PadRight(16) + " " +
            acctId + " " +
            _tran.GetText("TRAN-TYPE-CD").PadRight(2) + "-" +
            typeDesc + " " +
            catCd + "-" +
            catDesc + " " +
            _tran.GetText("TRAN-SOURCE").PadRight(10) + "    " +
            Edit(_tran.GetNumber("TRAN-AMT"), Minus) + "  ";
        WriteReport(Pad(line));
        _lineCounter++;
    }

    // 1500-A/B/C lookups (lines 484-512). INVALID KEY abends.
    private void LookupXref1500A(string cardNum)
    {
        byte[] key = HostEncoding.For(_ctx.Host).GetBytes(cardNum);
        if (_ctx.Xref.Read(key, out byte[]? img) != FileStatus.Ok) { _sysout.Add("INVALID CARD NUMBER : " + cardNum); Abend(); }
        else _xref = FixedRecord.Parse(_ctx.XrefLayout, img!, _ctx.Host);
    }

    private void LookupTranType1500B(string typeCd)
    {
        byte[] key = HostEncoding.For(_ctx.Host).GetBytes(typeCd);
        if (_ctx.TranType.Read(key, out byte[]? img) != FileStatus.Ok) { _sysout.Add("INVALID TRANSACTION TYPE : " + typeCd); Abend(); }
        else _tranType = FixedRecord.Parse(_ctx.TranTypeLayout, img!, _ctx.Host);
    }

    private void LookupTranCatg1500C(string typeCd, decimal catCd)
    {
        byte[] key = FixedRecord.CreateBlank(_ctx.TranCatgLayout, _ctx.Host)
            .SetText("TRAN-TYPE-CD", typeCd)
            .SetNumber("TRAN-CAT-CD", catCd)
            .ToBytes()[..6];
        if (_ctx.TranCatg.Read(key, out byte[]? img) != FileStatus.Ok) { _sysout.Add("INVALID TRAN CATG KEY"); Abend(); }
        else _tranCatg = FixedRecord.Parse(_ctx.TranCatgLayout, img!, _ctx.Host);
    }

    private void WriteReport(string line133) => _ctx.Report.Write(HostEncoding.For(_ctx.Host).GetBytes(line133));
    private static string Header1() => Pad("Transaction ID".PadRight(17) + "Account ID".PadRight(12) +
        "Transaction Type".PadRight(19) + "Tran Category".PadRight(35) + "Tran Source".PadRight(14) + " " + "        Amount".PadRight(16));
    private static string Header2() => new('-', 133);
    private static string Pad(string s) => s.Length >= 133 ? s[..133] : s.PadRight(133);
    private static string Edit(decimal v, string pic) => CobolEditedNumeric.Format(v, pic);
    private string Decode(FixedRecord rec) => HostEncoding.For(_ctx.Host).GetString(rec.ToBytes());
    private void Abend() { _sysout.Add("ABENDING PROGRAM"); throw new AbendException("999", "CBTRN03C abend."); }
}

/// <summary>Inputs for <see cref="Cbtrn03cReporter"/>.</summary>
public sealed record Cbtrn03cContext(
    SequentialFile Transact,
    VsamFile Xref,
    VsamFile TranType,
    VsamFile TranCatg,
    SequentialFile Report,
    RecordLayout TranLayout,
    RecordLayout XrefLayout,
    RecordLayout TranTypeLayout,
    RecordLayout TranCatgLayout,
    string StartDate,
    string EndDate,
    HostKind Host);
