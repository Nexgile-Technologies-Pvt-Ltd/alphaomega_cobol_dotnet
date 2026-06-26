using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;
using CardDemo.Tooling;

namespace CardDemo.Import;

/// <summary>
/// Serializes a typed <see cref="CardDemo.Domain"/> entity back to the canonical fixed-width record
/// image its mainframe dataset uses — the exact inverse of <see cref="MasterImporter"/>. Fields are
/// written in copybook order through the runtime codecs (zoned/DISPLAY numeric, host-encoded text);
/// FILLER and any field not set become spaces. The produced image is byte-identical to the source
/// dataset record, which is what powers the schema round-trip verification harness in ARCHITECTURE.md.
/// </summary>
/// <remarks>
/// <para>The record layout is derived from the copybook by <see cref="CopybookParser"/> (never
/// hand-transcribed). A blank record is built with COBOL <c>INITIALIZE</c> semantics — alphanumeric
/// items (and FILLER) become spaces, numeric items become zero — then each elementary field is filled
/// from the entity. Trailing-space-padded string properties round-trip exactly because the domain
/// entities store the full fixed width (per the relational re-architecture).</para>
/// <para><paramref name="host"/> selects EBCDIC (CP037, the authoritative dataset encoding) or ASCII
/// (ISO-8859-1 twin). The sign of a signed zoned field is carried per the chosen host's convention by
/// <see cref="ZonedDecimalCodec"/>.</para>
/// </remarks>
public sealed class RecordSerializer
{
    private readonly RecordLayouts _layouts;

    /// <summary>Creates a serializer that resolves copybook layouts from the given directory.</summary>
    public RecordSerializer(string copybookDirectory) => _layouts = new RecordLayouts(copybookDirectory);

    /// <summary>Creates a serializer over a pre-built layout cache.</summary>
    public RecordSerializer(RecordLayouts layouts) => _layouts = layouts;

    /// <summary>The copybook layouts this serializer uses (shared with <see cref="MasterImporter"/>).</summary>
    public RecordLayouts Layouts => _layouts;

    /// <summary>Serializes an <see cref="Account"/> to its 300-byte CVACT01Y record image.</summary>
    public byte[] Serialize(Account a, HostKind host)
    {
        FixedRecord r = Blank(CardDemoFiles.Account.Copybook, host)
            .SetNumber("ACCT-ID", a.AcctId)
            .SetText("ACCT-ACTIVE-STATUS", a.ActiveStatus)
            .SetNumber("ACCT-CURR-BAL", a.CurrBal)
            .SetNumber("ACCT-CREDIT-LIMIT", a.CreditLimit)
            .SetNumber("ACCT-CASH-CREDIT-LIMIT", a.CashCreditLimit)
            .SetText("ACCT-OPEN-DATE", a.OpenDate)
            .SetText("ACCT-EXPIRAION-DATE", a.ExpirationDate)
            .SetText("ACCT-REISSUE-DATE", a.ReissueDate)
            .SetNumber("ACCT-CURR-CYC-CREDIT", a.CurrCycCredit)
            .SetNumber("ACCT-CURR-CYC-DEBIT", a.CurrCycDebit)
            .SetText("ACCT-ADDR-ZIP", a.AddrZip)
            .SetText("ACCT-GROUP-ID", a.GroupId);
        return r.ToBytes(host);
    }

    /// <summary>Serializes a <see cref="Card"/> to its 150-byte CVACT02Y record image.</summary>
    public byte[] Serialize(Card a, HostKind host)
    {
        FixedRecord r = Blank(CardDemoFiles.Card.Copybook, host)
            .SetText("CARD-NUM", a.CardNum)
            .SetNumber("CARD-ACCT-ID", a.AcctId)
            .SetNumber("CARD-CVV-CD", a.CvvCd)
            .SetText("CARD-EMBOSSED-NAME", a.EmbossedName)
            .SetText("CARD-EXPIRAION-DATE", a.ExpirationDate)
            .SetText("CARD-ACTIVE-STATUS", a.ActiveStatus);
        return r.ToBytes(host);
    }

    /// <summary>Serializes a <see cref="CardXref"/> to its 50-byte CVACT03Y record image.</summary>
    public byte[] Serialize(CardXref a, HostKind host)
    {
        FixedRecord r = Blank(CardDemoFiles.CardXref.Copybook, host)
            .SetText("XREF-CARD-NUM", a.XrefCardNum)
            .SetNumber("XREF-CUST-ID", a.CustId)
            .SetNumber("XREF-ACCT-ID", a.AcctId);
        return r.ToBytes(host);
    }

    /// <summary>Serializes a <see cref="Customer"/> to its 500-byte CVCUS01Y record image.</summary>
    public byte[] Serialize(Customer a, HostKind host)
    {
        FixedRecord r = Blank(CardDemoFiles.Customer.Copybook, host)
            .SetNumber("CUST-ID", a.CustId)
            .SetText("CUST-FIRST-NAME", a.FirstName)
            .SetText("CUST-MIDDLE-NAME", a.MiddleName)
            .SetText("CUST-LAST-NAME", a.LastName)
            .SetText("CUST-ADDR-LINE-1", a.AddrLine1)
            .SetText("CUST-ADDR-LINE-2", a.AddrLine2)
            .SetText("CUST-ADDR-LINE-3", a.AddrLine3)
            .SetText("CUST-ADDR-STATE-CD", a.AddrStateCd)
            .SetText("CUST-ADDR-COUNTRY-CD", a.AddrCountryCd)
            .SetText("CUST-ADDR-ZIP", a.AddrZip)
            .SetText("CUST-PHONE-NUM-1", a.PhoneNum1)
            .SetText("CUST-PHONE-NUM-2", a.PhoneNum2)
            .SetNumber("CUST-SSN", a.Ssn)
            .SetText("CUST-GOVT-ISSUED-ID", a.GovtIssuedId)
            .SetText("CUST-DOB-YYYY-MM-DD", a.DobYyyyMmDd)
            .SetText("CUST-EFT-ACCOUNT-ID", a.EftAccountId)
            .SetText("CUST-PRI-CARD-HOLDER-IND", a.PriCardHolderInd)
            .SetNumber("CUST-FICO-CREDIT-SCORE", a.FicoCreditScore);
        return r.ToBytes(host);
    }

    /// <summary>Serializes a <see cref="TranCatBalance"/> to its 50-byte CVTRA01Y record image.</summary>
    public byte[] Serialize(TranCatBalance a, HostKind host)
    {
        FixedRecord r = Blank(CardDemoFiles.TranCatBal.Copybook, host, fillerFill: '0')
            .SetNumber("TRANCAT-ACCT-ID", a.AcctId)
            .SetText("TRANCAT-TYPE-CD", a.TypeCd)
            .SetNumber("TRANCAT-CD", a.CatCd)
            .SetNumber("TRAN-CAT-BAL", a.TranCatBal);
        return r.ToBytes(host);
    }

    /// <summary>Serializes a <see cref="DisclosureGroup"/> to its 50-byte CVTRA02Y record image.</summary>
    public byte[] Serialize(DisclosureGroup a, HostKind host)
    {
        FixedRecord r = Blank(CardDemoFiles.DiscGroup.Copybook, host, fillerFill: '0')
            .SetText("DIS-ACCT-GROUP-ID", a.AcctGroupId)
            .SetText("DIS-TRAN-TYPE-CD", a.TranTypeCd)
            .SetNumber("DIS-TRAN-CAT-CD", a.TranCatCd)
            .SetNumber("DIS-INT-RATE", a.IntRate);
        return r.ToBytes(host);
    }

    /// <summary>Serializes a <see cref="TranType"/> to its 60-byte CVTRA03Y record image.</summary>
    public byte[] Serialize(TranType a, HostKind host)
    {
        FixedRecord r = Blank(CardDemoFiles.TranType.Copybook, host, fillerFill: '0')
            .SetText("TRAN-TYPE", a.TranTypeCode)
            .SetText("TRAN-TYPE-DESC", a.TranTypeDesc);
        return r.ToBytes(host);
    }

    /// <summary>Serializes a <see cref="TranCategory"/> to its 60-byte CVTRA04Y record image.</summary>
    public byte[] Serialize(TranCategory a, HostKind host)
    {
        FixedRecord r = Blank(CardDemoFiles.TranCategory.Copybook, host, fillerFill: '0')
            .SetText("TRAN-TYPE-CD", a.TranTypeCd)
            .SetNumber("TRAN-CAT-CD", a.TranCatCd)
            .SetText("TRAN-CAT-TYPE-DESC", a.TranCatTypeDesc);
        return r.ToBytes(host);
    }

    /// <summary>Serializes a <see cref="UserSecurity"/> to its 80-byte CSUSR01Y record image.</summary>
    public byte[] Serialize(UserSecurity a, HostKind host)
    {
        FixedRecord r = Blank(CardDemoFiles.UserSecurity.Copybook, host)
            .SetText("SEC-USR-ID", a.UsrId)
            .SetText("SEC-USR-FNAME", a.FirstName)
            .SetText("SEC-USR-LNAME", a.LastName)
            .SetText("SEC-USR-PWD", a.Pwd)
            .SetText("SEC-USR-TYPE", a.UsrType);
        return r.ToBytes(host);
    }

    /// <summary>Serializes a <see cref="DailyTransaction"/> to its 350-byte CVTRA06Y record image.</summary>
    public byte[] Serialize(DailyTransaction a, HostKind host)
    {
        FixedRecord r = Blank(CardDemoFiles.DailyTransactions.Copybook, host)
            .SetText("DALYTRAN-ID", a.TranId)
            .SetText("DALYTRAN-TYPE-CD", a.TypeCd)
            .SetNumber("DALYTRAN-CAT-CD", a.CatCd)
            .SetText("DALYTRAN-SOURCE", a.Source)
            .SetText("DALYTRAN-DESC", a.Desc)
            .SetNumber("DALYTRAN-AMT", a.Amt)
            .SetNumber("DALYTRAN-MERCHANT-ID", a.MerchantId)
            .SetText("DALYTRAN-MERCHANT-NAME", a.MerchantName)
            .SetText("DALYTRAN-MERCHANT-CITY", a.MerchantCity)
            .SetText("DALYTRAN-MERCHANT-ZIP", a.MerchantZip)
            .SetText("DALYTRAN-CARD-NUM", a.CardNum)
            .SetText("DALYTRAN-ORIG-TS", a.OrigTs)
            .SetText("DALYTRAN-PROC-TS", a.ProcTs);
        return r.ToBytes(host);
    }

    /// <summary>Serializes a <see cref="Transaction"/> to its 350-byte CVTRA05Y record image.</summary>
    public byte[] Serialize(Transaction a, HostKind host)
    {
        FixedRecord r = Blank(CardDemoFiles.Transaction.Copybook, host)
            .SetText("TRAN-ID", a.TranId)
            .SetText("TRAN-TYPE-CD", a.TypeCd)
            .SetNumber("TRAN-CAT-CD", a.CatCd)
            .SetText("TRAN-SOURCE", a.Source)
            .SetText("TRAN-DESC", a.Desc)
            .SetNumber("TRAN-AMT", a.Amt)
            .SetNumber("TRAN-MERCHANT-ID", a.MerchantId)
            .SetText("TRAN-MERCHANT-NAME", a.MerchantName)
            .SetText("TRAN-MERCHANT-CITY", a.MerchantCity)
            .SetText("TRAN-MERCHANT-ZIP", a.MerchantZip)
            .SetText("TRAN-CARD-NUM", a.CardNum)
            .SetText("TRAN-ORIG-TS", a.OrigTs)
            .SetText("TRAN-PROC-TS", a.ProcTs);
        return r.ToBytes(host);
    }

    /// <summary>
    /// A record initialized to COBOL <c>INITIALIZE</c> state in the requested host: alphanumeric items
    /// and FILLER are spaces, numeric items are zero. Each <c>Serialize</c> overload then fills its fields.
    /// </summary>
    /// <param name="copybook">Copybook file name whose layout to use.</param>
    /// <param name="host">Target host encoding.</param>
    /// <param name="fillerFill">
    /// Character every FILLER region is filled with. Defaults to a space, but the four reference-table
    /// datasets (TCATBALF, DISCGRP, TRANTYPE, TRANCATG) are generated with their trailing FILLER set to
    /// the digit '0' (EBCDIC 0xF0); reproducing that exactly is required for byte-identity with the
    /// source dataset and is therefore a faithful behaviour of the serializer, not a defect.
    /// </param>
    private FixedRecord Blank(string copybook, HostKind host, char fillerFill = ' ')
    {
        RecordLayout layout = _layouts.For(copybook);
        FixedRecord r = FixedRecord.CreateInitialized(layout, host);
        if (fillerFill != ' ')
            foreach (FieldDef f in layout.Fields)
                if (f.IsFiller)
                    r.SetText(f.Name, new string(fillerFill, f.Length));
        return r;
    }
}
