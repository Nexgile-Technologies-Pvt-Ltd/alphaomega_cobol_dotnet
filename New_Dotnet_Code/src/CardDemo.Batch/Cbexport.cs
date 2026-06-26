using CardDemo.Cobol.Runtime;
using CardDemo.Data;

namespace CardDemo.Batch;

/// <summary>
/// Faithful port of <c>CBEXPORT</c> — reads the customer, account, xref, transaction and card files in
/// key order and writes a multi-record export file (<c>CVEXPORT</c>, with COMP/COMP-3 fields) for
/// branch migration, one record type per source file.
/// </summary>
/// <remarks>
/// Ported from <c>app/cbl/CBEXPORT.cbl</c>. Each export record is INITIALIZE-d then populated, leaving
/// the trailing FILLER at LOW-VALUES (0x00). The 26-byte EXPORT-TIMESTAMP is clock-derived (masked in
/// the golden master); EXPORT-SEQUENCE-NUM is a deterministic counter.
/// </remarks>
public sealed class Cbexport(Cbexport.Context ctx)
{
    private readonly Context _ctx = ctx;
    private readonly List<string> _sysout = [];
    private long _seq;

    public IReadOnlyList<string> Sysout => _sysout;

    public IReadOnlyList<string> Run()
    {
        _sysout.Add("CBEXPORT: Starting Customer Data Export");
        ExportFile(_ctx.Customer, _ctx.CustomerLayout, _ctx.CustomerVariant, "C", CustomerMap);
        ExportFile(_ctx.Account, _ctx.AccountLayout, _ctx.AccountVariant, "A", AccountMap);
        ExportFile(_ctx.Xref, _ctx.XrefLayout, _ctx.XrefVariant, "X", XrefMap);
        ExportFile(_ctx.Transaction, _ctx.TransactionLayout, _ctx.TransactionVariant, "T", TransactionMap);
        ExportFile(_ctx.Card, _ctx.CardLayout, _ctx.CardVariant, "D", CardMap);
        _sysout.Add("CBEXPORT: Export completed");
        return _sysout;
    }

    private void ExportFile(VsamFile input, RecordLayout srcLayout, RecordLayout variant, string recType, (string Src, string Dst)[] map)
    {
        input.StartBrowse();
        while (input.ReadNext(out byte[]? image) == FileStatus.Ok)
        {
            FixedRecord src = FixedRecord.Parse(srcLayout, image!, _ctx.Host);
            // INITIALIZE EXPORT-RECORD sets the primary EXPORT-RECORD-DATA X(460) to spaces, so the
            // variant FILLER ends up spaces (verified against the shipped EXPORT.DATA.PS).
            FixedRecord exp = FixedRecord.CreateInitialized(variant, _ctx.Host);
            exp.SetText("EXPORT-REC-TYPE", recType);
            exp.SetText("EXPORT-TIMESTAMP", _ctx.Timestamp);
            _seq++;
            exp.SetNumber("EXPORT-SEQUENCE-NUM", _seq);
            exp.SetText("EXPORT-BRANCH-ID", "0001");
            exp.SetText("EXPORT-REGION-CODE", "NORTH");

            foreach ((string s, string d) in map)
            {
                FieldDef df = Field(variant, d);
                if (df.Category == CobolCategory.Alphanumeric) exp.SetText(d, src.GetText(s));
                else exp.SetNumber(d, src.GetNumber(s));
            }

            _ctx.Export.Write(exp.ToBytes());
        }
        input.EndBrowse();
    }

    private static FieldDef Field(RecordLayout layout, string name)
    {
        foreach (FieldDef f in layout.Fields) if (f.Name == name) return f;
        throw new KeyNotFoundException($"Export field '{name}' not found.");
    }

    private static readonly (string, string)[] CustomerMap =
    {
        ("CUST-ID", "EXP-CUST-ID"), ("CUST-FIRST-NAME", "EXP-CUST-FIRST-NAME"),
        ("CUST-MIDDLE-NAME", "EXP-CUST-MIDDLE-NAME"), ("CUST-LAST-NAME", "EXP-CUST-LAST-NAME"),
        ("CUST-ADDR-LINE-1", "EXP-CUST-ADDR-LINE_1"), ("CUST-ADDR-LINE-2", "EXP-CUST-ADDR-LINE_2"),
        ("CUST-ADDR-LINE-3", "EXP-CUST-ADDR-LINE_3"), ("CUST-ADDR-STATE-CD", "EXP-CUST-ADDR-STATE-CD"),
        ("CUST-ADDR-COUNTRY-CD", "EXP-CUST-ADDR-COUNTRY-CD"), ("CUST-ADDR-ZIP", "EXP-CUST-ADDR-ZIP"),
        ("CUST-PHONE-NUM-1", "EXP-CUST-PHONE-NUM_1"), ("CUST-PHONE-NUM-2", "EXP-CUST-PHONE-NUM_2"),
        ("CUST-SSN", "EXP-CUST-SSN"), ("CUST-GOVT-ISSUED-ID", "EXP-CUST-GOVT-ISSUED-ID"),
        ("CUST-DOB-YYYY-MM-DD", "EXP-CUST-DOB-YYYY-MM-DD"), ("CUST-EFT-ACCOUNT-ID", "EXP-CUST-EFT-ACCOUNT-ID"),
        ("CUST-PRI-CARD-HOLDER-IND", "EXP-CUST-PRI-CARD-HOLDER-IND"),
        ("CUST-FICO-CREDIT-SCORE", "EXP-CUST-FICO-CREDIT-SCORE"),
    };

    private static readonly (string, string)[] AccountMap =
    {
        ("ACCT-ID", "EXP-ACCT-ID"), ("ACCT-ACTIVE-STATUS", "EXP-ACCT-ACTIVE-STATUS"),
        ("ACCT-CURR-BAL", "EXP-ACCT-CURR-BAL"), ("ACCT-CREDIT-LIMIT", "EXP-ACCT-CREDIT-LIMIT"),
        ("ACCT-CASH-CREDIT-LIMIT", "EXP-ACCT-CASH-CREDIT-LIMIT"), ("ACCT-OPEN-DATE", "EXP-ACCT-OPEN-DATE"),
        ("ACCT-EXPIRAION-DATE", "EXP-ACCT-EXPIRAION-DATE"), ("ACCT-REISSUE-DATE", "EXP-ACCT-REISSUE-DATE"),
        ("ACCT-CURR-CYC-CREDIT", "EXP-ACCT-CURR-CYC-CREDIT"), ("ACCT-CURR-CYC-DEBIT", "EXP-ACCT-CURR-CYC-DEBIT"),
        ("ACCT-ADDR-ZIP", "EXP-ACCT-ADDR-ZIP"), ("ACCT-GROUP-ID", "EXP-ACCT-GROUP-ID"),
    };

    private static readonly (string, string)[] XrefMap =
    {
        ("XREF-CARD-NUM", "EXP-XREF-CARD-NUM"), ("XREF-CUST-ID", "EXP-XREF-CUST-ID"),
        ("XREF-ACCT-ID", "EXP-XREF-ACCT-ID"),
    };

    private static readonly (string, string)[] TransactionMap =
    {
        ("TRAN-ID", "EXP-TRAN-ID"), ("TRAN-TYPE-CD", "EXP-TRAN-TYPE-CD"), ("TRAN-CAT-CD", "EXP-TRAN-CAT-CD"),
        ("TRAN-SOURCE", "EXP-TRAN-SOURCE"), ("TRAN-DESC", "EXP-TRAN-DESC"), ("TRAN-AMT", "EXP-TRAN-AMT"),
        ("TRAN-MERCHANT-ID", "EXP-TRAN-MERCHANT-ID"), ("TRAN-MERCHANT-NAME", "EXP-TRAN-MERCHANT-NAME"),
        ("TRAN-MERCHANT-CITY", "EXP-TRAN-MERCHANT-CITY"), ("TRAN-MERCHANT-ZIP", "EXP-TRAN-MERCHANT-ZIP"),
        ("TRAN-CARD-NUM", "EXP-TRAN-CARD-NUM"), ("TRAN-ORIG-TS", "EXP-TRAN-ORIG-TS"),
        ("TRAN-PROC-TS", "EXP-TRAN-PROC-TS"),
    };

    private static readonly (string, string)[] CardMap =
    {
        ("CARD-NUM", "EXP-CARD-NUM"), ("CARD-ACCT-ID", "EXP-CARD-ACCT-ID"), ("CARD-CVV-CD", "EXP-CARD-CVV-CD"),
        ("CARD-EMBOSSED-NAME", "EXP-CARD-EMBOSSED-NAME"), ("CARD-EXPIRAION-DATE", "EXP-CARD-EXPIRAION-DATE"),
        ("CARD-ACTIVE-STATUS", "EXP-CARD-ACTIVE-STATUS"),
    };

    /// <summary>Inputs for <see cref="Cbexport"/>: the five source files + layouts, the export output, the five EXPORT variant layouts, the (clock) timestamp, and host.</summary>
    public sealed record Context(
        VsamFile Customer, VsamFile Account, VsamFile Xref, VsamFile Transaction, VsamFile Card,
        SequentialFile Export,
        RecordLayout CustomerLayout, RecordLayout AccountLayout, RecordLayout XrefLayout,
        RecordLayout TransactionLayout, RecordLayout CardLayout,
        RecordLayout CustomerVariant, RecordLayout AccountVariant, RecordLayout XrefVariant,
        RecordLayout TransactionVariant, RecordLayout CardVariant,
        string Timestamp, HostKind Host);
}
