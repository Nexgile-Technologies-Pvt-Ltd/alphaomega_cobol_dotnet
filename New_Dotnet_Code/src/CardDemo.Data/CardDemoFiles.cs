namespace CardDemo.Data;

/// <summary>
/// A VSAM (KSDS) file in the CardDemo catalog: its SQLite-backed definition plus the metadata needed
/// to bootstrap it from the source data and to validate its key offsets against the copybook.
/// </summary>
/// <param name="Definition">The runtime file definition (name, record length, key byte ranges).</param>
/// <param name="Copybook">Record copybook file name (e.g. "CVACT01Y.cpy").</param>
/// <param name="SourceDataFile">EBCDIC dataset file name to load, or null if the file is produced by a job.</param>
/// <param name="KeyFields">Copybook field name(s) spanning the primary key (for offset validation).</param>
/// <param name="AlternateKeyFields">Copybook field name(s) spanning the alternate key, if any.</param>
public sealed record VsamFileSpec(
    VsamFileDefinition Definition,
    string Copybook,
    string? SourceDataFile,
    string[] KeyFields,
    string[]? AlternateKeyFields = null);

/// <summary>A QSAM sequential file in the catalog.</summary>
public sealed record SequentialFileSpec(
    string Name,
    int RecordLength,
    string Copybook,
    string? SourceDataFile);

/// <summary>
/// The CardDemo file catalog: every base-application VSAM and sequential dataset, its record length,
/// key byte ranges, copybook, and source EBCDIC data file. Key offsets here are validated against the
/// copybooks by the parity test-suite (see <c>FileCatalogTests</c>), so they cannot silently drift.
/// </summary>
public static class CardDemoFiles
{
    public static readonly VsamFileSpec Account = new(
        new VsamFileDefinition("ACCTFILE", 300, new KeyDef(0, 11)),
        "CVACT01Y.cpy", "AWS.M2.CARDDEMO.ACCTDATA.PS", ["ACCT-ID"]);

    public static readonly VsamFileSpec Card = new(
        new VsamFileDefinition("CARDDAT", 150, new KeyDef(0, 16), new KeyDef(16, 11)),
        "CVACT02Y.cpy", "AWS.M2.CARDDEMO.CARDDATA.PS", ["CARD-NUM"], ["CARD-ACCT-ID"]);

    public static readonly VsamFileSpec CardXref = new(
        new VsamFileDefinition("CARDXREF", 50, new KeyDef(0, 16), new KeyDef(25, 11)),
        "CVACT03Y.cpy", "AWS.M2.CARDDEMO.CARDXREF.PS", ["XREF-CARD-NUM"], ["XREF-ACCT-ID"]);

    public static readonly VsamFileSpec Customer = new(
        new VsamFileDefinition("CUSTDAT", 500, new KeyDef(0, 9)),
        "CVCUS01Y.cpy", "AWS.M2.CARDDEMO.CUSTDATA.PS", ["CUST-ID"]);

    public static readonly VsamFileSpec TranCatBal = new(
        new VsamFileDefinition("TCATBALF", 50, new KeyDef(0, 17)),
        "CVTRA01Y.cpy", "AWS.M2.CARDDEMO.TCATBALF.PS", ["TRANCAT-ACCT-ID", "TRANCAT-CD"]);

    public static readonly VsamFileSpec DiscGroup = new(
        new VsamFileDefinition("DISCGRP", 50, new KeyDef(0, 16)),
        "CVTRA02Y.cpy", "AWS.M2.CARDDEMO.DISCGRP.PS", ["DIS-ACCT-GROUP-ID", "DIS-TRAN-CAT-CD"]);

    public static readonly VsamFileSpec TranType = new(
        new VsamFileDefinition("TRANTYPE", 60, new KeyDef(0, 2)),
        "CVTRA03Y.cpy", "AWS.M2.CARDDEMO.TRANTYPE.PS", ["TRAN-TYPE"]);

    public static readonly VsamFileSpec TranCategory = new(
        new VsamFileDefinition("TRANCATG", 60, new KeyDef(0, 6)),
        "CVTRA04Y.cpy", "AWS.M2.CARDDEMO.TRANCATG.PS", ["TRAN-TYPE-CD", "TRAN-CAT-CD"]);

    public static readonly VsamFileSpec UserSecurity = new(
        new VsamFileDefinition("USRSEC", 80, new KeyDef(0, 8)),
        "CSUSR01Y.cpy", "AWS.M2.CARDDEMO.USRSEC.PS", ["SEC-USR-ID"]);

    /// <summary>Online transaction master; built by the posting job, so it has no source data file.</summary>
    public static readonly VsamFileSpec Transaction = new(
        new VsamFileDefinition("TRANSACT", 350, new KeyDef(0, 16)),
        "CVTRA05Y.cpy", null, ["TRAN-ID"]);

    /// <summary>Daily input transactions for the posting job.</summary>
    public static readonly SequentialFileSpec DailyTransactions = new(
        "DALYTRAN", 350, "CVTRA06Y.cpy", "AWS.M2.CARDDEMO.DALYTRAN.PS");

    /// <summary>All keyed (VSAM KSDS) files.</summary>
    public static IReadOnlyList<VsamFileSpec> Vsam { get; } =
    [
        Account, Card, CardXref, Customer, TranCatBal,
        DiscGroup, TranType, TranCategory, UserSecurity, Transaction,
    ];

    /// <summary>All sequential (QSAM) files.</summary>
    public static IReadOnlyList<SequentialFileSpec> Sequential { get; } = [DailyTransactions];
}
