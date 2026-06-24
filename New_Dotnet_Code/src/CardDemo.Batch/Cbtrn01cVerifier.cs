using CardDemo.Cobol.Runtime;
using CardDemo.Data;

namespace CardDemo.Batch;

/// <summary>
/// Faithful port of <c>CBTRN01C</c> — reads the daily transaction file and, for each record, verifies
/// the card via the cross-reference and reads the account, reporting the results to SYSOUT (DISPLAY).
/// (The customer, card, and transaction files are opened but never read.)
/// </summary>
/// <remarks>
/// Ported from <c>app/cbl/CBTRN01C.cbl</c>. Faithful quirk preserved: the lookup block (lines 170-184)
/// is outside the inner end-of-file guard, so on the EOF iteration it runs once more against the LAST
/// daily transaction (READ-INTO on EOF leaves the record unchanged) — the final record is verified twice.
/// </remarks>
public sealed class Cbtrn01cVerifier(Cbtrn01cContext ctx)
{
    private readonly Cbtrn01cContext _ctx = ctx;
    private readonly List<string> _sysout = [];

    private bool _eof;
    private int _xrefReadStatus;
    private int _acctReadStatus;
    private FixedRecord? _dalyTran;
    private FixedRecord? _xref;

    public IReadOnlyList<string> Sysout => _sysout;

    public IReadOnlyList<string> Run()
    {
        _sysout.Add("START OF EXECUTION OF PROGRAM CBTRN01C");
        _ctx.DalyTran.OpenInput();

        // PERFORM UNTIL END-OF-DAILY-TRANS-FILE = 'Y' (lines 164-186).
        while (!_eof)
        {
            Get1000DalyTranNext();
            if (!_eof) _sysout.Add(Decode(_dalyTran!));               // DISPLAY DALYTRAN-RECORD (line 168)

            // Lines 170-184 run regardless of EOF (outside the inner guard); on EOF they re-process the
            // last record.
            _xrefReadStatus = 0;
            Lookup2000Xref(_dalyTran!.GetText("DALYTRAN-CARD-NUM"));
            if (_xrefReadStatus == 0)
            {
                _acctReadStatus = 0;
                string acctId = AcctIdText(_xref!, "XREF-ACCT-ID");
                Read3000Account(acctId);
                if (_acctReadStatus != 0)
                    _sysout.Add("ACCOUNT " + acctId + " NOT FOUND");
            }
            else
            {
                _sysout.Add("CARD NUMBER " + _dalyTran.GetText("DALYTRAN-CARD-NUM") +
                            " COULD NOT BE VERIFIED. SKIPPING TRANSACTION ID-" + _dalyTran.GetText("DALYTRAN-ID"));
            }
        }

        _sysout.Add("END OF EXECUTION OF PROGRAM CBTRN01C");
        return _sysout;
    }

    // --- 1000-DALYTRAN-GET-NEXT (lines 202-225) --------------------------------------------------
    private void Get1000DalyTranNext()
    {
        string status = _ctx.DalyTran.Read(out byte[]? image);
        if (status == FileStatus.Ok) _dalyTran = FixedRecord.Parse(_ctx.DalyTranLayout, image!, _ctx.Host);
        else if (status == FileStatus.EndOfFile) _eof = true;
        else { _sysout.Add("ERROR READING DAILY TRANSACTION FILE"); Abend(); }
        // On EOF, _dalyTran retains the last successfully-read record (READ INTO is not performed).
    }

    // --- 2000-LOOKUP-XREF (lines 227-239) --------------------------------------------------------
    private void Lookup2000Xref(string cardNum)
    {
        byte[] key = HostEncoding.For(_ctx.Host).GetBytes(cardNum);
        if (_ctx.Xref.Read(key, out byte[]? image) != FileStatus.Ok)
        {
            _sysout.Add("INVALID CARD NUMBER FOR XREF");
            _xrefReadStatus = 4;
            return;
        }
        _xref = FixedRecord.Parse(_ctx.XrefLayout, image!, _ctx.Host);
        _sysout.Add("SUCCESSFUL READ OF XREF");
        _sysout.Add("CARD NUMBER: " + _xref.GetText("XREF-CARD-NUM"));
        _sysout.Add("ACCOUNT ID : " + AcctIdText(_xref, "XREF-ACCT-ID"));
        _sysout.Add("CUSTOMER ID: " + ((long)_xref.GetNumber("XREF-CUST-ID")).ToString("D9"));
    }

    // --- 3000-READ-ACCOUNT (lines 241-250) -------------------------------------------------------
    private void Read3000Account(string acctId)
    {
        var key = new byte[11];
        ZonedDecimalCodec.Encode(decimal.Parse(acctId), key, 11, 0, false, _ctx.Host);
        if (_ctx.Account.Read(key, out _) != FileStatus.Ok)
        {
            _sysout.Add("INVALID ACCOUNT NUMBER FOUND");
            _acctReadStatus = 4;
        }
        else _sysout.Add("SUCCESSFUL READ OF ACCOUNT FILE");
    }

    private static string AcctIdText(FixedRecord rec, string field) => ((long)rec.GetNumber(field)).ToString("D11");
    private string Decode(FixedRecord rec) => HostEncoding.For(_ctx.Host).GetString(rec.ToBytes());
    private void Abend() { _sysout.Add("ABENDING PROGRAM"); throw new AbendException("999", "CBTRN01C abend."); }
}

/// <summary>Inputs for <see cref="Cbtrn01cVerifier"/>.</summary>
public sealed record Cbtrn01cContext(
    SequentialFile DalyTran,
    VsamFile Xref,
    VsamFile Account,
    RecordLayout DalyTranLayout,
    RecordLayout XrefLayout,
    HostKind Host);
