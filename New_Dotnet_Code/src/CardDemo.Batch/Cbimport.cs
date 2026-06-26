using CardDemo.Cobol.Runtime;
using CardDemo.Data;

namespace CardDemo.Batch;

/// <summary>
/// Faithful port of <c>CBIMPORT</c> — reads the multi-record export file and splits it into the
/// separate normalized master files (customer, account, xref, transaction, card), one output per
/// record type, writing unrecognized records to an error file.
/// </summary>
/// <remarks>
/// Ported from <c>app/cbl/CBIMPORT.cbl</c> (the reverse of CBEXPORT). Each target record is INITIALIZE-d
/// (group items: named fields cleared, FILLER left at LOW-VALUES 0x00) then populated from the export
/// variant fields.
/// </remarks>
public sealed class Cbimport(Cbimport.Context ctx)
{
    private readonly Context _ctx = ctx;
    private readonly List<string> _sysout = [];

    public IReadOnlyList<string> Sysout => _sysout;

    public IReadOnlyList<string> Run()
    {
        _sysout.Add("CBIMPORT: Starting Customer Data Import");
        _ctx.Export.OpenInput();
        _ctx.Customer.OpenOutput();
        _ctx.Account.OpenOutput();
        _ctx.Xref.OpenOutput();
        _ctx.Transaction.OpenOutput();
        _ctx.Card.OpenOutput();
        _ctx.Error.OpenOutput();

        while (_ctx.Export.Read(out byte[]? image) == FileStatus.Ok)
        {
            string recType = HostEncoding.For(_ctx.Host).GetString(image!.AsSpan(0, 1));
            switch (recType)
            {
                case "C": Split(image, _ctx.CustomerVariant, _ctx.CustomerLayout, _ctx.Customer, CustomerMap); break;
                case "A": Split(image, _ctx.AccountVariant, _ctx.AccountLayout, _ctx.Account, AccountMap); break;
                case "X": Split(image, _ctx.XrefVariant, _ctx.XrefLayout, _ctx.Xref, XrefMap); break;
                case "T": Split(image, _ctx.TransactionVariant, _ctx.TransactionLayout, _ctx.Transaction, TransactionMap); break;
                case "D": Split(image, _ctx.CardVariant, _ctx.CardLayout, _ctx.Card, CardMap); break;
                default: WriteError(image); break;
            }
        }

        _sysout.Add("CBIMPORT: Import completed");
        return _sysout;
    }

    private void Split(byte[] image, RecordLayout variant, RecordLayout targetLayout, SequentialFile output, (string Exp, string Tgt)[] map)
    {
        FixedRecord exp = FixedRecord.Parse(variant, image, _ctx.Host);
        FixedRecord tgt = FixedRecord.CreateBlank(targetLayout, _ctx.Host); // INITIALIZE; group FILLER stays 0x00
        foreach ((string e, string t) in map)
        {
            FieldDef td = Field(targetLayout, t);
            if (td.Category == CobolCategory.Alphanumeric) tgt.SetText(t, exp.GetText(e));
            else tgt.SetNumber(t, exp.GetNumber(e));
        }
        output.Write(tgt.ToBytes());
    }

    private void WriteError(byte[] image)
    {
        // WS-ERROR-RECORD: ts(26) '|' type(1) '|' seq(7) '|' msg(50) filler(43). The timestamp is
        // clock-derived; only exercised for unrecognized record types.
        var rec = new byte[132];
        rec.AsSpan().Fill((byte)' ');
        HostEncoding.For(_ctx.Host).GetBytes(_ctx.Timestamp.PadRight(26)[..26]).CopyTo(rec, 0);
        rec[26] = (byte)'|';
        rec[27] = image[0];
        rec[28] = (byte)'|';
        // seq from EXPORT-SEQUENCE-NUM (COMP, offset 27..30) -> 7-digit display
        long seq = (long)BinaryCodec.Decode(image.AsSpan(27, 4), 0, false);
        HostEncoding.For(_ctx.Host).GetBytes(seq.ToString("D7")).CopyTo(rec, 29);
        rec[36] = (byte)'|';
        HostEncoding.For(_ctx.Host).GetBytes("Unknown record type encountered".PadRight(50)).CopyTo(rec, 37);
        _ctx.Error.Write(rec);
    }

    private static FieldDef Field(RecordLayout layout, string name)
    {
        foreach (FieldDef f in layout.Fields) if (f.Name == name) return f;
        throw new KeyNotFoundException($"Import field '{name}' not found.");
    }

    // (export field, target field) — the inverse of the CBEXPORT mappings.
    private static readonly (string, string)[] CustomerMap =
    {
        ("EXP-CUST-ID", "CUST-ID"), ("EXP-CUST-FIRST-NAME", "CUST-FIRST-NAME"),
        ("EXP-CUST-MIDDLE-NAME", "CUST-MIDDLE-NAME"), ("EXP-CUST-LAST-NAME", "CUST-LAST-NAME"),
        ("EXP-CUST-ADDR-LINE_1", "CUST-ADDR-LINE-1"), ("EXP-CUST-ADDR-LINE_2", "CUST-ADDR-LINE-2"),
        ("EXP-CUST-ADDR-LINE_3", "CUST-ADDR-LINE-3"), ("EXP-CUST-ADDR-STATE-CD", "CUST-ADDR-STATE-CD"),
        ("EXP-CUST-ADDR-COUNTRY-CD", "CUST-ADDR-COUNTRY-CD"), ("EXP-CUST-ADDR-ZIP", "CUST-ADDR-ZIP"),
        ("EXP-CUST-PHONE-NUM_1", "CUST-PHONE-NUM-1"), ("EXP-CUST-PHONE-NUM_2", "CUST-PHONE-NUM-2"),
        ("EXP-CUST-SSN", "CUST-SSN"), ("EXP-CUST-GOVT-ISSUED-ID", "CUST-GOVT-ISSUED-ID"),
        ("EXP-CUST-DOB-YYYY-MM-DD", "CUST-DOB-YYYY-MM-DD"), ("EXP-CUST-EFT-ACCOUNT-ID", "CUST-EFT-ACCOUNT-ID"),
        ("EXP-CUST-PRI-CARD-HOLDER-IND", "CUST-PRI-CARD-HOLDER-IND"),
        ("EXP-CUST-FICO-CREDIT-SCORE", "CUST-FICO-CREDIT-SCORE"),
    };

    private static readonly (string, string)[] AccountMap =
    {
        ("EXP-ACCT-ID", "ACCT-ID"), ("EXP-ACCT-ACTIVE-STATUS", "ACCT-ACTIVE-STATUS"),
        ("EXP-ACCT-CURR-BAL", "ACCT-CURR-BAL"), ("EXP-ACCT-CREDIT-LIMIT", "ACCT-CREDIT-LIMIT"),
        ("EXP-ACCT-CASH-CREDIT-LIMIT", "ACCT-CASH-CREDIT-LIMIT"), ("EXP-ACCT-OPEN-DATE", "ACCT-OPEN-DATE"),
        ("EXP-ACCT-EXPIRAION-DATE", "ACCT-EXPIRAION-DATE"), ("EXP-ACCT-REISSUE-DATE", "ACCT-REISSUE-DATE"),
        ("EXP-ACCT-CURR-CYC-CREDIT", "ACCT-CURR-CYC-CREDIT"), ("EXP-ACCT-CURR-CYC-DEBIT", "ACCT-CURR-CYC-DEBIT"),
        ("EXP-ACCT-ADDR-ZIP", "ACCT-ADDR-ZIP"), ("EXP-ACCT-GROUP-ID", "ACCT-GROUP-ID"),
    };

    private static readonly (string, string)[] XrefMap =
    {
        ("EXP-XREF-CARD-NUM", "XREF-CARD-NUM"), ("EXP-XREF-CUST-ID", "XREF-CUST-ID"),
        ("EXP-XREF-ACCT-ID", "XREF-ACCT-ID"),
    };

    private static readonly (string, string)[] TransactionMap =
    {
        ("EXP-TRAN-ID", "TRAN-ID"), ("EXP-TRAN-TYPE-CD", "TRAN-TYPE-CD"), ("EXP-TRAN-CAT-CD", "TRAN-CAT-CD"),
        ("EXP-TRAN-SOURCE", "TRAN-SOURCE"), ("EXP-TRAN-DESC", "TRAN-DESC"), ("EXP-TRAN-AMT", "TRAN-AMT"),
        ("EXP-TRAN-MERCHANT-ID", "TRAN-MERCHANT-ID"), ("EXP-TRAN-MERCHANT-NAME", "TRAN-MERCHANT-NAME"),
        ("EXP-TRAN-MERCHANT-CITY", "TRAN-MERCHANT-CITY"), ("EXP-TRAN-MERCHANT-ZIP", "TRAN-MERCHANT-ZIP"),
        ("EXP-TRAN-CARD-NUM", "TRAN-CARD-NUM"), ("EXP-TRAN-ORIG-TS", "TRAN-ORIG-TS"),
        ("EXP-TRAN-PROC-TS", "TRAN-PROC-TS"),
    };

    private static readonly (string, string)[] CardMap =
    {
        ("EXP-CARD-NUM", "CARD-NUM"), ("EXP-CARD-ACCT-ID", "CARD-ACCT-ID"), ("EXP-CARD-CVV-CD", "CARD-CVV-CD"),
        ("EXP-CARD-EMBOSSED-NAME", "CARD-EMBOSSED-NAME"), ("EXP-CARD-EXPIRAION-DATE", "CARD-EXPIRAION-DATE"),
        ("EXP-CARD-ACTIVE-STATUS", "CARD-ACTIVE-STATUS"),
    };

    /// <summary>Inputs for <see cref="Cbimport"/>: the export input, five target outputs + an error file, the source/variant layouts, the (clock) timestamp, and host.</summary>
    public sealed record Context(
        SequentialFile Export,
        SequentialFile Customer, SequentialFile Account, SequentialFile Xref, SequentialFile Transaction,
        SequentialFile Card, SequentialFile Error,
        RecordLayout CustomerLayout, RecordLayout AccountLayout, RecordLayout XrefLayout,
        RecordLayout TransactionLayout, RecordLayout CardLayout,
        RecordLayout CustomerVariant, RecordLayout AccountVariant, RecordLayout XrefVariant,
        RecordLayout TransactionVariant, RecordLayout CardVariant,
        string Timestamp, HostKind Host);
}
