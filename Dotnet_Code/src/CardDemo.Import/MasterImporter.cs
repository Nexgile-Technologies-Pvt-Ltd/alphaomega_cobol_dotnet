using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;
using CardDemo.Tooling;

namespace CardDemo.Import;

/// <summary>
/// One-time seeder for the relational re-architecture: reads each base-master EBCDIC dataset from
/// <c>app/data/EBCDIC</c>, decodes every fixed-width record with its copybook layout into typed field
/// values, maps those to the matching <see cref="CardDemo.Domain"/> entity, and inserts the entity
/// through the per-table <see cref="CardDemo.Data"/> repositories.
/// </summary>
/// <remarks>
/// <para>Decoding reuses the runtime codecs end to end: <see cref="FixedRecord.Parse"/> slices the
/// record per the parsed <see cref="RecordLayout"/> and decodes PIC X via <see cref="HostEncoding"/> and
/// PIC 9 / S9 via <see cref="ZonedDecimalCodec"/>. The source datasets are IBM EBCDIC (CP037), so the
/// importer always reads with <see cref="HostKind.Ebcdic"/>.</para>
/// <para>Files imported (copybook -> entity -> table): ACCTDATA -> Account, CARDDATA -> Card,
/// CARDXREF -> CardXref, CUSTDATA -> Customer, TCATBALF -> TranCatBalance, DISCGRP -> DisclosureGroup,
/// TRANTYPE -> TranType, TRANCATG -> TranCategory, USRSEC -> UserSecurity, DALYTRAN -> DailyTransaction.
/// TRANSACT has no seed file (the posting job builds it), so it is not imported.</para>
/// </remarks>
public sealed class MasterImporter
{
    private const HostKind SourceHost = HostKind.Ebcdic;

    private readonly string _ebcdicDir;
    private readonly RecordLayouts _layouts;

    /// <summary>
    /// Creates an importer.
    /// </summary>
    /// <param name="ebcdicDataDirectory">Directory holding the <c>AWS.M2.CARDDEMO.*.PS</c> EBCDIC datasets.</param>
    /// <param name="copybookDirectory">Directory holding the <c>.cpy</c> copybooks.</param>
    public MasterImporter(string ebcdicDataDirectory, string copybookDirectory)
    {
        _ebcdicDir = ebcdicDataDirectory;
        _layouts = new RecordLayouts(copybookDirectory);
    }

    /// <summary>Creates an importer over a pre-built layout cache (shared with a serializer).</summary>
    public MasterImporter(string ebcdicDataDirectory, RecordLayouts layouts)
    {
        _ebcdicDir = ebcdicDataDirectory;
        _layouts = layouts;
    }

    /// <summary>The copybook layouts this importer uses (shareable with <see cref="RecordSerializer"/>).</summary>
    public RecordLayouts Layouts => _layouts;

    /// <summary>
    /// Imports every base master that has a seed file into <paramref name="db"/>, returning the per-file
    /// row counts. Runs inside a single transaction so a failure leaves the database unchanged.
    /// </summary>
    public ImportResult ImportAll(RelationalDb db)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        db.InTransaction(_ =>
        {
            counts["ACCOUNT"] = ImportAccounts(db);
            counts["CARD"] = ImportCards(db);
            counts["CARD_XREF"] = ImportCardXrefs(db);
            counts["CUSTOMER"] = ImportCustomers(db);
            counts["TRAN_CAT_BAL"] = ImportTranCatBalances(db);
            counts["DISCLOSURE_GROUP"] = ImportDisclosureGroups(db);
            counts["TRAN_TYPE"] = ImportTranTypes(db);
            counts["TRAN_CATEGORY"] = ImportTranCategories(db);
            counts["USER_SECURITY"] = ImportUserSecurity(db);
            counts["DAILY_TRANSACTION"] = ImportDailyTransactions(db);
        });
        return new ImportResult(counts);
    }

    // ---- Per-file importers ------------------------------------------------------------------------

    /// <summary>ACCTDATA (CVACT01Y/300) -> ACCOUNT.</summary>
    public int ImportAccounts(RelationalDb db)
    {
        var repo = new AccountRepository(db);
        return Import(CardDemoFiles.Account.SourceDataFile!, CardDemoFiles.Account.Copybook, r => repo.Insert(MapAccount(r)));
    }

    /// <summary>CARDDATA (CVACT02Y/150) -> CARD.</summary>
    public int ImportCards(RelationalDb db)
    {
        var repo = new CardRepository(db);
        return Import(CardDemoFiles.Card.SourceDataFile!, CardDemoFiles.Card.Copybook, r => repo.Insert(MapCard(r)));
    }

    /// <summary>CARDXREF (CVACT03Y/50) -> CARD_XREF.</summary>
    public int ImportCardXrefs(RelationalDb db)
    {
        var repo = new CardXrefRepository(db);
        return Import(CardDemoFiles.CardXref.SourceDataFile!, CardDemoFiles.CardXref.Copybook, r => repo.Insert(MapCardXref(r)));
    }

    /// <summary>CUSTDATA (CVCUS01Y/500) -> CUSTOMER.</summary>
    public int ImportCustomers(RelationalDb db)
    {
        var repo = new CustomerRepository(db);
        return Import(CardDemoFiles.Customer.SourceDataFile!, CardDemoFiles.Customer.Copybook, r => repo.Insert(MapCustomer(r)));
    }

    /// <summary>TCATBALF (CVTRA01Y/50) -> TRAN_CAT_BAL.</summary>
    public int ImportTranCatBalances(RelationalDb db)
    {
        var repo = new TranCatBalanceRepository(db);
        return Import(CardDemoFiles.TranCatBal.SourceDataFile!, CardDemoFiles.TranCatBal.Copybook, r => repo.Insert(MapTranCatBalance(r)));
    }

    /// <summary>DISCGRP (CVTRA02Y/50) -> DISCLOSURE_GROUP.</summary>
    public int ImportDisclosureGroups(RelationalDb db)
    {
        var repo = new DisclosureGroupRepository(db);
        return Import(CardDemoFiles.DiscGroup.SourceDataFile!, CardDemoFiles.DiscGroup.Copybook, r => repo.Insert(MapDisclosureGroup(r)));
    }

    /// <summary>TRANTYPE (CVTRA03Y/60) -> TRAN_TYPE.</summary>
    public int ImportTranTypes(RelationalDb db)
    {
        var repo = new TranTypeRepository(db);
        return Import(CardDemoFiles.TranType.SourceDataFile!, CardDemoFiles.TranType.Copybook, r => repo.Insert(MapTranType(r)));
    }

    /// <summary>TRANCATG (CVTRA04Y/60) -> TRAN_CATEGORY.</summary>
    public int ImportTranCategories(RelationalDb db)
    {
        var repo = new TranCategoryRepository(db);
        return Import(CardDemoFiles.TranCategory.SourceDataFile!, CardDemoFiles.TranCategory.Copybook, r => repo.Insert(MapTranCategory(r)));
    }

    /// <summary>USRSEC (CSUSR01Y/80) -> USER_SECURITY.</summary>
    public int ImportUserSecurity(RelationalDb db)
    {
        var repo = new UserSecurityRepository(db);
        return Import(CardDemoFiles.UserSecurity.SourceDataFile!, CardDemoFiles.UserSecurity.Copybook, r => repo.Insert(MapUserSecurity(r)));
    }

    /// <summary>DALYTRAN (CVTRA06Y/350) -> DAILY_TRANSACTION.</summary>
    public int ImportDailyTransactions(RelationalDb db)
    {
        var repo = new DailyTransactionRepository(db);
        return Import(CardDemoFiles.DailyTransactions.SourceDataFile!, CardDemoFiles.DailyTransactions.Copybook, r => repo.Insert(MapDailyTransaction(r)));
    }

    // ---- Field -> entity mappers (decode order matches the copybook) -------------------------------

    private static Account MapAccount(FixedRecord r) => new()
    {
        AcctId = (long)r.GetNumber("ACCT-ID"),
        ActiveStatus = r.GetText("ACCT-ACTIVE-STATUS"),
        CurrBal = r.GetNumber("ACCT-CURR-BAL"),
        CreditLimit = r.GetNumber("ACCT-CREDIT-LIMIT"),
        CashCreditLimit = r.GetNumber("ACCT-CASH-CREDIT-LIMIT"),
        OpenDate = r.GetText("ACCT-OPEN-DATE"),
        ExpirationDate = r.GetText("ACCT-EXPIRAION-DATE"),
        ReissueDate = r.GetText("ACCT-REISSUE-DATE"),
        CurrCycCredit = r.GetNumber("ACCT-CURR-CYC-CREDIT"),
        CurrCycDebit = r.GetNumber("ACCT-CURR-CYC-DEBIT"),
        AddrZip = r.GetText("ACCT-ADDR-ZIP"),
        GroupId = r.GetText("ACCT-GROUP-ID"),
    };

    private static Card MapCard(FixedRecord r) => new()
    {
        CardNum = r.GetText("CARD-NUM"),
        AcctId = (long)r.GetNumber("CARD-ACCT-ID"),
        CvvCd = (int)r.GetNumber("CARD-CVV-CD"),
        EmbossedName = r.GetText("CARD-EMBOSSED-NAME"),
        ExpirationDate = r.GetText("CARD-EXPIRAION-DATE"),
        ActiveStatus = r.GetText("CARD-ACTIVE-STATUS"),
    };

    private static CardXref MapCardXref(FixedRecord r) => new()
    {
        XrefCardNum = r.GetText("XREF-CARD-NUM"),
        CustId = (long)r.GetNumber("XREF-CUST-ID"),
        AcctId = (long)r.GetNumber("XREF-ACCT-ID"),
    };

    private static Customer MapCustomer(FixedRecord r) => new()
    {
        CustId = (long)r.GetNumber("CUST-ID"),
        FirstName = r.GetText("CUST-FIRST-NAME"),
        MiddleName = r.GetText("CUST-MIDDLE-NAME"),
        LastName = r.GetText("CUST-LAST-NAME"),
        AddrLine1 = r.GetText("CUST-ADDR-LINE-1"),
        AddrLine2 = r.GetText("CUST-ADDR-LINE-2"),
        AddrLine3 = r.GetText("CUST-ADDR-LINE-3"),
        AddrStateCd = r.GetText("CUST-ADDR-STATE-CD"),
        AddrCountryCd = r.GetText("CUST-ADDR-COUNTRY-CD"),
        AddrZip = r.GetText("CUST-ADDR-ZIP"),
        PhoneNum1 = r.GetText("CUST-PHONE-NUM-1"),
        PhoneNum2 = r.GetText("CUST-PHONE-NUM-2"),
        Ssn = (long)r.GetNumber("CUST-SSN"),
        GovtIssuedId = r.GetText("CUST-GOVT-ISSUED-ID"),
        DobYyyyMmDd = r.GetText("CUST-DOB-YYYY-MM-DD"),
        EftAccountId = r.GetText("CUST-EFT-ACCOUNT-ID"),
        PriCardHolderInd = r.GetText("CUST-PRI-CARD-HOLDER-IND"),
        FicoCreditScore = (int)r.GetNumber("CUST-FICO-CREDIT-SCORE"),
    };

    private static TranCatBalance MapTranCatBalance(FixedRecord r) => new()
    {
        AcctId = (long)r.GetNumber("TRANCAT-ACCT-ID"),
        TypeCd = r.GetText("TRANCAT-TYPE-CD"),
        CatCd = (int)r.GetNumber("TRANCAT-CD"),
        TranCatBal = r.GetNumber("TRAN-CAT-BAL"),
    };

    private static DisclosureGroup MapDisclosureGroup(FixedRecord r) => new()
    {
        AcctGroupId = r.GetText("DIS-ACCT-GROUP-ID"),
        TranTypeCd = r.GetText("DIS-TRAN-TYPE-CD"),
        TranCatCd = (int)r.GetNumber("DIS-TRAN-CAT-CD"),
        IntRate = r.GetNumber("DIS-INT-RATE"),
    };

    private static TranType MapTranType(FixedRecord r) => new()
    {
        TranTypeCode = r.GetText("TRAN-TYPE"),
        TranTypeDesc = r.GetText("TRAN-TYPE-DESC"),
    };

    private static TranCategory MapTranCategory(FixedRecord r) => new()
    {
        TranTypeCd = r.GetText("TRAN-TYPE-CD"),
        TranCatCd = (int)r.GetNumber("TRAN-CAT-CD"),
        TranCatTypeDesc = r.GetText("TRAN-CAT-TYPE-DESC"),
    };

    private static UserSecurity MapUserSecurity(FixedRecord r) => new()
    {
        UsrId = r.GetText("SEC-USR-ID"),
        FirstName = r.GetText("SEC-USR-FNAME"),
        LastName = r.GetText("SEC-USR-LNAME"),
        Pwd = r.GetText("SEC-USR-PWD"),
        UsrType = r.GetText("SEC-USR-TYPE"),
    };

    private static DailyTransaction MapDailyTransaction(FixedRecord r) => new()
    {
        TranId = r.GetText("DALYTRAN-ID"),
        TypeCd = r.GetText("DALYTRAN-TYPE-CD"),
        CatCd = (int)r.GetNumber("DALYTRAN-CAT-CD"),
        Source = r.GetText("DALYTRAN-SOURCE"),
        Desc = r.GetText("DALYTRAN-DESC"),
        Amt = r.GetNumber("DALYTRAN-AMT"),
        MerchantId = (long)r.GetNumber("DALYTRAN-MERCHANT-ID"),
        MerchantName = r.GetText("DALYTRAN-MERCHANT-NAME"),
        MerchantCity = r.GetText("DALYTRAN-MERCHANT-CITY"),
        MerchantZip = r.GetText("DALYTRAN-MERCHANT-ZIP"),
        CardNum = r.GetText("DALYTRAN-CARD-NUM"),
        OrigTs = r.GetText("DALYTRAN-ORIG-TS"),
        ProcTs = r.GetText("DALYTRAN-PROC-TS"),
    };

    // ---- Core read/decode/insert loop --------------------------------------------------------------

    /// <summary>
    /// Reads <paramref name="dataFile"/> from the EBCDIC directory, slices it into fixed-length records
    /// per the <paramref name="copybook"/> layout, decodes each record, and applies <paramref name="insert"/>
    /// (which maps + inserts via a repository). Returns the number of records inserted. A non-'00' status
    /// from the repository (e.g. a duplicate key) fails the whole import.
    /// </summary>
    private int Import(string dataFile, string copybook, Func<FixedRecord, string> insert)
    {
        RecordLayout layout = _layouts.For(copybook);
        byte[] data = File.ReadAllBytes(Path.Combine(_ebcdicDir, dataFile));
        int reclen = layout.Length;
        if (data.Length % reclen != 0)
            throw new InvalidDataException(
                $"{dataFile}: dataset length {data.Length} is not a multiple of record length {reclen}.");

        int count = data.Length / reclen;
        for (int i = 0; i < count; i++)
        {
            var image = new ReadOnlySpan<byte>(data, i * reclen, reclen);
            FixedRecord record = FixedRecord.Parse(layout, image, SourceHost);
            string status = insert(record);
            if (status != FileStatus.Ok)
                throw new InvalidDataException(
                    $"{dataFile}: insert of record {i} failed with FILE STATUS '{status}'.");
        }
        return count;
    }
}

/// <summary>The per-table row counts produced by <see cref="MasterImporter.ImportAll"/>.</summary>
public sealed class ImportResult
{
    /// <summary>Rows inserted, keyed by table name.</summary>
    public IReadOnlyDictionary<string, int> RowCounts { get; }

    /// <summary>Total rows inserted across all imported tables.</summary>
    public int Total => RowCounts.Values.Sum();

    internal ImportResult(IReadOnlyDictionary<string, int> rowCounts) => RowCounts = rowCounts;

    /// <summary>Rows inserted into the named table.</summary>
    public int Count(string table) => RowCounts.TryGetValue(table, out int n) ? n : 0;
}
