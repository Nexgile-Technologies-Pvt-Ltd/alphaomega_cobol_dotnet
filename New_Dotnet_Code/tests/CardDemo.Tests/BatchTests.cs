using CardDemo.Batch;
using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;
using CardDemo.Import;
using CardDemo.Tooling;

namespace CardDemo.Tests;

/// <summary>
/// Functional / characterization tests for the re-ported, pure-.NET relational CardDemo batch programs
/// (the <c>CB*</c> family, exercised through the <see cref="CardDemo.Data"/> repositories and seeded via
/// <see cref="MasterImporter"/>). Each test seeds an in-memory <see cref="RelationalDb"/> (either from the
/// shipped EBCDIC masters or from a tiny hand-built fixture), runs one ported program end to end, and
/// asserts a documented, deterministic outcome:
/// <list type="bullet">
///   <item><b>CBACT04C</b> — the truncated COMPUTE <c>(TRAN-CAT-BAL * DIS-INT-RATE) / 1200</c> on a known
///         account, plus a full-seed run that completes and writes interest transactions.</item>
///   <item><b>CBTRN02C</b> — posting populates <c>TRANSACTION</c>, updates balances, and writes rejects
///         with the right reason codes (100 bad card, 102 overlimit, 103 expired).</item>
///   <item><b>CBEXPORT/CBIMPORT</b> — a pure-.NET relational round-trip: serialize every master to a flat
///         export image, re-import into a fresh DB, and assert byte-identical per-row table state.</item>
///   <item><b>CBACT02C / CBACT03C / CBCUS01C / CBTRN01C</b> — the read-and-print drivers produce the
///         expected SYSOUT line count / banner format.</item>
///   <item><b>CSUTLDTC</b> — validates a known good date and a known bad date.</item>
///   <item><b>CBACT01C / CBTRN03C / COBSWAIT</b> — additional ported programs complete sanely.</item>
/// </list>
/// No COBOL is compiled or invoked anywhere; the seed data is the shipped EBCDIC masters and the oracle is
/// pure .NET (per <c>_design/ARCHITECTURE.md</c> §Verification).
/// </summary>
public sealed class BatchTests
{
    private static readonly DateTime FixedNow = new(2026, 06, 26, 12, 34, 56, 780);
    private static readonly IClock Clock = new FixedClock(FixedNow);

    /// <summary>Opens an in-memory DB seeded from the shipped EBCDIC masters via <see cref="MasterImporter"/>.</summary>
    private static BatchSupport OpenSeeded(out ImportResult imported)
        => BatchSupport.OpenSeeded(SeedPaths.EbcdicDataDir, SeedPaths.CopybookDir, out imported);

    // =================================================================================================
    // CBACT04C — interest calculator
    // =================================================================================================

    /// <summary>
    /// CBACT04C interest math on a hand-built account: with <c>TRAN-CAT-BAL = 1234.56</c> and
    /// <c>DIS-INT-RATE = 19.99</c>, the monthly interest is <c>(1234.56 * 19.99) / 1200</c> = 20.5657...
    /// truncated toward zero at 2 dp (COBOL COMPUTE has no ROUNDED) = <b>20.56</b>. The single written
    /// interest TRANSACTION must carry exactly that amount, type '01', cat 0005, and source 'System'.
    /// </summary>
    [Fact]
    public void Cbact04c_InterestCompute_TruncatesTowardZero_OnKnownAccount()
    {
        using BatchSupport s = BatchSupport.Open();
        SeedOneInterestAccount(s,
            acctId: 11111111111L, groupId: "ZEROGRP   ", typeCd: "01", catCd: 5,
            balance: 1234.56m, rate: 19.99m, cardNum: "4111111111111111", startBal: 100.00m);

        var prog = new Cbact04c();
        int rc = prog.Run(s, parmDate: "2022071800", clock: Clock);

        Assert.Equal(0, rc);

        // Independently compute the faithful truncated result.
        decimal expected = Decimals.Store(1234.56m * 19.99m / 1200m, 9, 2, true);
        Assert.Equal(20.56m, expected); // documents the exact value

        List<Transaction> tx = s.Transaction.ReadAll().ToList();
        Transaction interest = Assert.Single(tx);
        Assert.Equal(expected, interest.Amt);
        Assert.Equal("01", interest.TypeCd);
        Assert.Equal(5, interest.CatCd);              // MOVE '05' into 9(4) -> 0005
        Assert.Equal("System", interest.Source.TrimEnd());
        Assert.StartsWith("Int. for a/c 11111111111", interest.Desc);
        Assert.Equal("2022071800", interest.TranId[..10]); // PARM-DATE prefix
        Assert.Equal("000001", interest.TranId.Substring(10, 6)); // WS-TRANID-SUFFIX

        // The single interest TX was written and the run accounted for exactly one TCATBAL record.
        Assert.Equal(1, prog.RecordCount);
        Assert.Equal(1, prog.InterestTransactionsWritten);
        Assert.Contains("START OF EXECUTION OF PROGRAM CBACT04C", prog.Sysout);
        Assert.Contains("END OF EXECUTION OF PROGRAM CBACT04C", prog.Sysout);
    }

    /// <summary>
    /// CBACT04C over the full shipped masters: completes (RC 0), processes every TCATBAL row, and writes one
    /// interest TRANSACTION per category whose disclosure rate is non-zero. Every written interest amount is
    /// the truncated <c>bal*rate/1200</c> (re-derived independently here, with the program's DEFAULT-group
    /// fallback applied) — proving the COMPUTE truncation holds across the real dataset, not just one row.
    /// </summary>
    [Fact]
    public void Cbact04c_FullSeed_WritesTruncatedInterestPerNonZeroRateCategory()
    {
        using BatchSupport s = OpenSeeded(out ImportResult imported);
        Assert.True(imported.Count("TRAN_CAT_BAL") > 0);

        // Snapshot the inputs needed to re-derive the expected interest, per the program's lookup rules.
        List<TranCatBalance> balances = s.TranCatBalance.ReadAll().ToList();
        Dictionary<long, string> acctGroup = s.Account.ReadAll().ToDictionary(a => a.AcctId, a => a.GroupId);
        var disc = new Dictionary<(string, string, int), decimal>();
        foreach (DisclosureGroup g in s.DisclosureGroup.ReadAll())
            disc[(g.AcctGroupId, g.TranTypeCd, g.TranCatCd)] = g.IntRate;

        decimal Rate(long acctId, string typeCd, int catCd)
        {
            string grp = acctGroup.TryGetValue(acctId, out string? gid) ? gid : "";
            if (disc.TryGetValue((grp, typeCd, catCd), out decimal r)) return r;
            // DEFAULT fallback — the literal 'DEFAULT' MOVEd into the X(10) key is space-padded to 10.
            return disc.TryGetValue(("DEFAULT".PadRight(10), typeCd, catCd), out decimal d) ? d : 0m;
        }

        var prog = new Cbact04c();
        int rc = prog.Run(s, parmDate: "2022071800", clock: Clock);
        Assert.Equal(0, rc);
        Assert.Equal(balances.Count, prog.RecordCount);

        // Expected: one interest TX for every balance whose (looked-up) rate is non-zero, amount truncated.
        var expectedAmts = new List<decimal>();
        foreach (TranCatBalance b in balances)
        {
            decimal rate = Rate(b.AcctId, b.TypeCd, b.CatCd);
            if (rate != 0m)
                expectedAmts.Add(Decimals.Store(b.TranCatBal * rate / 1200m, 9, 2, true));
        }

        List<Transaction> tx = s.Transaction.ReadAll().ToList();
        Assert.Equal(expectedAmts.Count, tx.Count);
        Assert.Equal((long)expectedAmts.Count, prog.InterestTransactionsWritten);

        // Same multiset of amounts (order is by tran_id/insertion; compare order-independently).
        Assert.Equal(
            expectedAmts.OrderBy(x => x).ToList(),
            tx.Select(t => t.Amt).OrderBy(x => x).ToList());

        // Every written interest TX is an '01'/0005 'System' record (the CBACT04C signature).
        Assert.All(tx, t =>
        {
            Assert.Equal("01", t.TypeCd);
            Assert.Equal(5, t.CatCd);
            Assert.Equal("System", t.Source.TrimEnd());
        });
    }

    // =================================================================================================
    // CBTRN02C — daily transaction posting
    // =================================================================================================

    /// <summary>
    /// CBTRN02C posting on the full shipped masters: every daily-transaction row whose card resolves and
    /// whose account passes the credit-limit / expiration checks is posted into <c>TRANSACTION</c>; its
    /// amount is added to the account balance and the matching <c>TRAN_CAT_BAL</c> category balance. The
    /// processed/rejected counters are consistent and RC is 4 iff any record was rejected.
    /// </summary>
    [Fact]
    public void Cbtrn02c_PostsValidDailyTransactions_IntoTransactionTableAndBalances()
    {
        using BatchSupport s = OpenSeeded(out ImportResult imported);
        Assert.True(imported.Count("DAILY_TRANSACTION") > 0);

        long dailyCount = s.DailyTransaction.ReadAll().LongCount();

        // Pick a valid daily transaction (card resolves) and snapshot its account before posting.
        DailyTransaction sample = s.DailyTransaction.ReadAll()
            .First(d => s.CardXref.ReadByKey(d.CardNum, out _) == FileStatus.Ok);
        Assert.Equal(FileStatus.Ok, s.CardXref.ReadByKey(sample.CardNum, out CardXref? xref));
        Assert.Equal(FileStatus.Ok, s.Account.ReadByKey(xref!.AcctId, out Account? acctBefore));
        decimal balBefore = acctBefore!.CurrBal;

        string rejPath = TempFile("dalyrejs");
        var prog = new Cbtrn02c();
        int rc = prog.Run(s, rejPath, clock: Clock, host: HostKind.Ebcdic);

        // Counters add up and RC reflects whether anything was rejected.
        Assert.Equal(dailyCount, prog.TransactionsProcessed);
        Assert.Equal(prog.TransactionsProcessed,
            prog.TransactionsRejected + s.Transaction.ReadAll().LongCount());
        Assert.Equal(prog.TransactionsRejected > 0 ? 4 : 0, rc);

        // The sampled valid transaction was posted with the exact daily fields.
        Assert.Equal(FileStatus.Ok, s.Transaction.ReadByKey(sample.TranId, out Transaction? posted));
        Assert.Equal(sample.Amt, posted!.Amt);
        Assert.Equal(sample.CardNum, posted.CardNum);
        Assert.Equal(sample.TypeCd, posted.TypeCd);

        // Its amount moved the account balance (sum every posted amount for that account).
        Assert.Equal(FileStatus.Ok, s.Account.ReadByKey(xref.AcctId, out Account? acctAfter));
        decimal postedForAcct = s.DailyTransaction.ReadAll()
            .Where(d => s.CardXref.ReadByKey(d.CardNum, out CardXref? x) == FileStatus.Ok && x!.AcctId == xref.AcctId)
            .Where(d => s.Transaction.ReadByKey(d.TranId, out _) == FileStatus.Ok)
            .Sum(d => d.Amt);
        Assert.Equal(Decimals.Store(balBefore + postedForAcct, 10, 2, true), acctAfter!.CurrBal);

        // The reject dataset exists and is a whole number of 430-byte records.
        Assert.True(File.Exists(rejPath));
        long len = new FileInfo(rejPath).Length;
        Assert.Equal(0, len % 430);
        Assert.Equal(prog.TransactionsRejected, len / 430);
    }

    /// <summary>
    /// CBTRN02C writes rejects with the right reason codes. A hand-built DB with three doomed daily
    /// transactions yields the documented reasons: 100 (bad card number — no xref), 102 (overlimit — amount
    /// exceeds the credit limit), and 103 (after expiration). The 430-byte reject record carries the reason
    /// 9(4) at offset 350 and the reason description at offset 354.
    /// </summary>
    [Fact]
    public void Cbtrn02c_WritesRejects_WithCorrectReasonCodes()
    {
        using BatchSupport s = BatchSupport.Open();

        // One healthy account/card/xref so the "good" path is reachable, with a known credit limit + expiry.
        s.Account.Insert(new Account
        {
            AcctId = 22222222222L, ActiveStatus = "Y", CurrBal = 0m, CreditLimit = 500.00m,
            CashCreditLimit = 500.00m, OpenDate = "2020-01-01", ExpirationDate = "2025-12-31",
            ReissueDate = "2020-01-01", CurrCycCredit = 0m, CurrCycDebit = 0m,
            AddrZip = "12345", GroupId = "GRP1",
        });
        s.CardXref.Insert(new CardXref { XrefCardNum = "5500000000000004", CustId = 1, AcctId = 22222222222L });

        // 100 — bad card number (no xref row for this card).
        s.DailyTransaction.Insert(NewDaily("D000000000000100", card: "9999999999999999", amt: 10.00m,
            origTs: "2024-01-01-00.00.00.000000"));
        // 102 — overlimit: amount (600.00) > credit limit (500.00); within expiry.
        s.DailyTransaction.Insert(NewDaily("D000000000000102", card: "5500000000000004", amt: 600.00m,
            origTs: "2024-01-01-00.00.00.000000"));
        // 103 — after expiration: orig date 2099-... > ACCT-EXPIRAION-DATE 2025-12-31 (amount within limit).
        s.DailyTransaction.Insert(NewDaily("D000000000000103", card: "5500000000000004", amt: 1.00m,
            origTs: "2099-01-01-00.00.00.000000"));

        string rejPath = TempFile("dalyrejs-reasons");
        var prog = new Cbtrn02c();
        int rc = prog.Run(s, rejPath, clock: Clock, host: HostKind.Ebcdic);

        Assert.Equal(4, rc);                          // some rejected
        Assert.Equal(3, prog.TransactionsProcessed);
        Assert.Equal(3, prog.TransactionsRejected);   // all three are doomed
        Assert.Empty(s.Transaction.ReadAll());        // nothing posted

        // Decode the three 430-byte reject records: reason 9(4) at byte 350.
        byte[] data = File.ReadAllBytes(rejPath);
        Assert.Equal(3 * 430, data.Length);
        var reasons = new List<int>();
        for (int i = 0; i < 3; i++)
        {
            int reason = (int)ZonedDecimalCodec.Decode(data.AsSpan(i * 430 + 350, 4), 0, false, HostKind.Ebcdic);
            reasons.Add(reason);
        }
        reasons.Sort();
        Assert.Equal(new[] { 100, 102, 103 }, reasons);
    }

    // =================================================================================================
    // CBEXPORT / CBIMPORT — pure-.NET relational round-trip (no COBOL oracle)
    // =================================================================================================

    /// <summary>
    /// CBEXPORT then CBIMPORT round-trip, pure .NET. Export every master row from a seeded DB to a flat
    /// multi-record export image (record-type tag + the canonical fixed-width record image produced by
    /// <see cref="RecordSerializer"/>), then import that image into a <em>fresh, empty</em> DB by parsing
    /// each record back with <see cref="MasterImporter.Layouts"/> and re-inserting. The re-imported table
    /// state must be byte-identical, table by table and row by row, to the original — the anti-hallucination
    /// net for the export/import boundary, with no mainframe oracle.
    /// </summary>
    [Fact]
    public void Cbexport_Then_Cbimport_RoundTripsMasters_ToByteIdenticalTableState()
    {
        using BatchSupport src = OpenSeeded(out _);
        var serializer = new RecordSerializer(new RecordLayouts(SeedPaths.CopybookDir));

        // ---- CBEXPORT: every master row -> (recType, canonical EBCDIC image) ----
        var exported = new List<(char Type, byte[] Image)>();
        foreach (Account a in src.Account.ReadAll()) exported.Add(('A', serializer.Serialize(a, HostKind.Ebcdic)));
        foreach (Card c in src.Card.ReadAll()) exported.Add(('D', serializer.Serialize(c, HostKind.Ebcdic)));
        foreach (CardXref x in src.CardXref.ReadAll()) exported.Add(('X', serializer.Serialize(x, HostKind.Ebcdic)));
        foreach (Customer cu in src.Customer.ReadAll()) exported.Add(('C', serializer.Serialize(cu, HostKind.Ebcdic)));
        foreach (TranCatBalance t in src.TranCatBalance.ReadAll()) exported.Add(('B', serializer.Serialize(t, HostKind.Ebcdic)));
        foreach (DisclosureGroup g in src.DisclosureGroup.ReadAll()) exported.Add(('G', serializer.Serialize(g, HostKind.Ebcdic)));
        foreach (TranType tt in src.TranType.ReadAll()) exported.Add(('Y', serializer.Serialize(tt, HostKind.Ebcdic)));
        foreach (TranCategory tc in src.TranCategory.ReadAll()) exported.Add(('K', serializer.Serialize(tc, HostKind.Ebcdic)));
        foreach (UserSecurity u in src.UserSecurity.ReadAll()) exported.Add(('U', serializer.Serialize(u, HostKind.Ebcdic)));
        foreach (DailyTransaction d in src.DailyTransaction.ReadAll()) exported.Add(('L', serializer.Serialize(d, HostKind.Ebcdic)));

        Assert.NotEmpty(exported);

        // Persist the export to a flat file (1-byte type tag + image), then read it back (proves the file
        // boundary, not just in-memory objects).
        string exportPath = TempFile("cvexport");
        using (FileStream fs = File.Create(exportPath))
            foreach ((char type, byte[] image) in exported)
            {
                fs.WriteByte((byte)type);
                fs.Write(image);
            }

        // ---- CBIMPORT: split the export file back into a fresh, empty relational DB ----
        var importer = new MasterImporter(SeedPaths.EbcdicDataDir, serializer.Layouts);
        using BatchSupport dst = BatchSupport.Open();

        byte[] file = File.ReadAllBytes(exportPath);
        int pos = 0;
        while (pos < file.Length)
        {
            char type = (char)file[pos++];
            int reclen = RecLenFor(type, serializer.Layouts);
            var image = new ReadOnlySpan<byte>(file, pos, reclen);
            pos += reclen;
            ImportOne(type, image, serializer.Layouts, dst);
        }

        // ---- Assert byte-identical table state, table by table ----
        AssertSameImages("ACCOUNT", src.Account.ReadAll().Select(a => serializer.Serialize(a, HostKind.Ebcdic)),
                                    dst.Account.ReadAll().Select(a => serializer.Serialize(a, HostKind.Ebcdic)));
        AssertSameImages("CARD", src.Card.ReadAll().Select(c => serializer.Serialize(c, HostKind.Ebcdic)),
                                 dst.Card.ReadAll().Select(c => serializer.Serialize(c, HostKind.Ebcdic)));
        AssertSameImages("CARD_XREF", src.CardXref.ReadAll().Select(x => serializer.Serialize(x, HostKind.Ebcdic)),
                                      dst.CardXref.ReadAll().Select(x => serializer.Serialize(x, HostKind.Ebcdic)));
        AssertSameImages("CUSTOMER", src.Customer.ReadAll().Select(c => serializer.Serialize(c, HostKind.Ebcdic)),
                                     dst.Customer.ReadAll().Select(c => serializer.Serialize(c, HostKind.Ebcdic)));
        AssertSameImages("TRAN_CAT_BAL", src.TranCatBalance.ReadAll().Select(t => serializer.Serialize(t, HostKind.Ebcdic)),
                                         dst.TranCatBalance.ReadAll().Select(t => serializer.Serialize(t, HostKind.Ebcdic)));
        AssertSameImages("DISCLOSURE_GROUP", src.DisclosureGroup.ReadAll().Select(g => serializer.Serialize(g, HostKind.Ebcdic)),
                                             dst.DisclosureGroup.ReadAll().Select(g => serializer.Serialize(g, HostKind.Ebcdic)));
        AssertSameImages("TRAN_TYPE", src.TranType.ReadAll().Select(t => serializer.Serialize(t, HostKind.Ebcdic)),
                                      dst.TranType.ReadAll().Select(t => serializer.Serialize(t, HostKind.Ebcdic)));
        AssertSameImages("TRAN_CATEGORY", src.TranCategory.ReadAll().Select(t => serializer.Serialize(t, HostKind.Ebcdic)),
                                          dst.TranCategory.ReadAll().Select(t => serializer.Serialize(t, HostKind.Ebcdic)));
        AssertSameImages("USER_SECURITY", src.UserSecurity.ReadAll().Select(u => serializer.Serialize(u, HostKind.Ebcdic)),
                                          dst.UserSecurity.ReadAll().Select(u => serializer.Serialize(u, HostKind.Ebcdic)));
        AssertSameImages("DAILY_TRANSACTION", src.DailyTransaction.ReadAll().Select(d => serializer.Serialize(d, HostKind.Ebcdic)),
                                              dst.DailyTransaction.ReadAll().Select(d => serializer.Serialize(d, HostKind.Ebcdic)));
    }

    // =================================================================================================
    // CBEXPORT / CBIMPORT — PROGRAM-LEVEL round-trip (drive the actual ported programs)
    // =================================================================================================

    /// <summary>
    /// End-to-end PROGRAM-LEVEL round-trip through the two ported programs themselves (not a hand-rolled
    /// re-implementation): seed an in-memory <see cref="RelationalDb"/> from the shipped EBCDIC masters via
    /// <see cref="MasterImporter"/>, post the daily transactions (<see cref="Cbtrn02c"/>) so the TRANSACTION
    /// table is non-empty, then run <see cref="Cbexport"/> to write the single multi-record 500-byte EXPORT
    /// dataset and <see cref="Cbimport"/> to split it back into the five per-type output files (CUSTOUT /
    /// ACCTOUT / XREFOUT / TRNXOUT / CARDOUT). Each output file must round-trip back to the seeded table
    /// data: its bytes equal the concatenation of <see cref="RecordSerializer"/>-serialized images of the
    /// seeded rows, in the program's ascending-PK export order — byte-for-byte.
    /// </summary>
    [Fact]
    public void Cbexport_Then_Cbimport_ProgramLevel_RoundTripsEveryType_ToByteIdenticalOutputs()
    {
        using BatchSupport s = OpenSeeded(out _);

        // Populate TRANSACTION so type-'T' is exercised (CBEXPORT reads it; there is no TRANSACT seed file).
        new Cbtrn02c().Run(s, TempFile("dalyrejs-for-export"), clock: Clock);
        Assert.NotEmpty(s.Transaction.ReadAll());

        var serializer = new RecordSerializer(new RecordLayouts(SeedPaths.CopybookDir));

        // ---- Run CBEXPORT: every master row -> one 500-byte CVEXPORT record into the flat EXPORT dataset.
        string exportPath = TempFile("cbexport");
        IReadOnlyList<string> exportSysout =
            Cbexport.Run(s, exportPath, SeedPaths.CopybookDir, Clock, HostKind.Ebcdic);
        Assert.Contains("CBEXPORT: Export completed", exportSysout);
        Assert.True(File.Exists(exportPath));
        Assert.Equal(0, new FileInfo(exportPath).Length % 500);  // RECFM=F, LRECL 500

        // ---- Run CBIMPORT: split the EXPORT feed into the five per-type output datasets. A CardOutPath is
        // supplied (recommended option-b override) so the type-'D' CARD sink is actually written.
        string custOut = TempFile("custout");
        string acctOut = TempFile("acctout");
        string xrefOut = TempFile("xrefout");
        string trnxOut = TempFile("trnxout");
        string cardOut = TempFile("cardout");
        string errOut = TempFile("errout");

        var ctx = NewImportContext(exportPath, custOut, acctOut, xrefOut, trnxOut, errOut, serializer, cardOut);
        int rc = new Cbimport(ctx).Run();
        Assert.Equal(0, rc);

        // ---- Assert each output file round-trips back to the seeded table data (re-serialize + byte-compare).
        // CBEXPORT browses each table ascending by PK; ReadAll() yields the same order, so the expected
        // output is the per-row serialized images concatenated in that order.
        AssertFileEquals("CUSTOUT", custOut,
            s.Customer.ReadAll().Select(c => serializer.Serialize(c, HostKind.Ebcdic)));
        AssertFileEquals("ACCTOUT", acctOut,
            s.Account.ReadAll().Select(a => serializer.Serialize(a, HostKind.Ebcdic)));
        AssertFileEquals("XREFOUT", xrefOut,
            s.CardXref.ReadAll().Select(x => serializer.Serialize(x, HostKind.Ebcdic)));
        AssertFileEquals("TRNXOUT", trnxOut,
            s.Transaction.ReadAll().Select(t => serializer.Serialize(t, HostKind.Ebcdic)));
        AssertFileEquals("CARDOUT", cardOut,
            s.Card.ReadAll().Select(d => serializer.Serialize(d, HostKind.Ebcdic)));
    }

    /// <summary>
    /// FB-1 (CARDOUT has no JCL DD): with the JCL-faithful Context (<c>CardOutPath == null</c>),
    /// <see cref="Cbimport"/> abends in 1100-OPEN-FILES on the missing CARDOUT DD. The export here carries a
    /// type-'D' record so the would-be CARD target is genuinely required; the port models the quirk as an
    /// <see cref="AbendException"/> (CEE3ABD), thrown before any record is processed.
    /// </summary>
    [Fact]
    public void Cbimport_AbendsOnMissingCardOut_WhenTypeDPresent_FaithfulBug()
    {
        // A tiny DB with at least one CARD (type 'D') row to export.
        using BatchSupport s = BatchSupport.Open();
        Assert.Equal(FileStatus.Ok, s.Card.Insert(new Card
        {
            CardNum = "4111111111111111", AcctId = 11111111111L, CvvCd = 123,
            EmbossedName = "TEST CARDHOLDER", ExpirationDate = "2030-12-31", ActiveStatus = "Y",
        }));

        var serializer = new RecordSerializer(new RecordLayouts(SeedPaths.CopybookDir));

        string exportPath = TempFile("cbexport-d");
        Cbexport.Run(s, exportPath, SeedPaths.CopybookDir, Clock, HostKind.Ebcdic);
        // The export must actually contain the type-'D' record (one 500-byte record).
        Assert.Equal(500, new FileInfo(exportPath).Length);

        // JCL-faithful Context: CardOutPath omitted (null) -> OPEN OUTPUT CARD-OUTPUT fails -> abend.
        var ctx = NewImportContext(
            exportPath,
            TempFile("custout-d"), TempFile("acctout-d"), TempFile("xrefout-d"),
            TempFile("trnxout-d"), TempFile("errout-d"), serializer, cardOutPath: null);

        AbendException abend = Assert.Throws<AbendException>(() => new Cbimport(ctx).Run());
        Assert.Equal("999", abend.AbendCode);
    }

    // =================================================================================================
    // Read-and-print drivers (CBACT02C / CBACT03C / CBCUS01C / CBTRN01C)
    // =================================================================================================

    /// <summary>
    /// CBACT02C prints the card file once per record between banners. SYSOUT line count =
    /// 1 (start) + N cards + 1 (end), and the optional flat SYSOUT report has the same line count.
    /// </summary>
    [Fact]
    public void Cbact02c_PrintsEachCardOnce_BetweenBanners()
    {
        using BatchSupport s = OpenSeeded(out _);
        long cards = s.Card.ReadAll().LongCount();
        Assert.True(cards > 0);

        string sysout = TempFile("cbact02c.sysout");
        var prog = new Cbact02c();
        int rc = prog.Run(s.Db, sysout);

        Assert.Equal(0, rc);
        Assert.Equal("START OF EXECUTION OF PROGRAM CBACT02C", prog.Sysout[0]);
        Assert.Equal("END OF EXECUTION OF PROGRAM CBACT02C", prog.Sysout[^1]);
        Assert.Equal(cards + 2, prog.Sysout.Count);   // once per card (CBACT02C has no double-print)
        Assert.Equal(prog.Sysout.Count, CountLines(sysout));
    }

    /// <summary>
    /// CBACT03C prints each cross-reference record TWICE (faithful bug #1: once in 1000-GET-NEXT, once in
    /// the MAIN loop). SYSOUT line count = 1 (start) + 2N + 1 (end).
    /// </summary>
    [Fact]
    public void Cbact03c_PrintsEachXrefTwice_FaithfulBug()
    {
        using BatchSupport s = OpenSeeded(out _);
        long xrefs = s.CardXref.ReadAll().LongCount();
        Assert.True(xrefs > 0);

        var prog = new Cbact03c();
        int rc = prog.Run(s.Db);

        Assert.Equal(0, rc);
        Assert.Equal("START OF EXECUTION OF PROGRAM CBACT03C", prog.Sysout[0]);
        Assert.Equal("END OF EXECUTION OF PROGRAM CBACT03C", prog.Sysout[^1]);
        Assert.Equal(2 * xrefs + 2, prog.Sysout.Count);
    }

    /// <summary>
    /// CBCUS01C prints each customer record TWICE (faithful bug #1). SYSOUT line count = 1 + 2N + 1, and the
    /// flat SYSOUT report matches.
    /// </summary>
    [Fact]
    public void Cbcus01c_PrintsEachCustomerTwice_FaithfulBug()
    {
        using BatchSupport s = OpenSeeded(out _);
        long custs = s.Customer.ReadAll().LongCount();
        Assert.True(custs > 0);

        string sysout = TempFile("cbcus01c.sysout");
        var prog = new Cbcus01c();
        int rc = prog.Run(s.Db, sysout);

        Assert.Equal(0, rc);
        Assert.Equal("START OF EXECUTION OF PROGRAM CBCUS01C", prog.Sysout[0]);
        Assert.Equal("END OF EXECUTION OF PROGRAM CBCUS01C", prog.Sysout[^1]);
        Assert.Equal(2 * custs + 2, prog.Sysout.Count);
        Assert.Equal(prog.Sysout.Count, CountLines(sysout));
    }

    /// <summary>
    /// CBTRN01C read-and-validate driver: it displays each daily-transaction record and validates it against
    /// the card xref / account masters. It starts and ends with its banners and produces at least one DISPLAY
    /// line per daily-transaction record processed.
    /// </summary>
    [Fact]
    public void Cbtrn01c_ReadsAndValidatesDailyTransactions_BetweenBanners()
    {
        using BatchSupport s = OpenSeeded(out _);
        long daily = s.DailyTransaction.ReadAll().LongCount();
        Assert.True(daily > 0);

        IReadOnlyList<string> sysout = Cbtrn01c.Run(s);

        Assert.Equal("START OF EXECUTION OF PROGRAM CBTRN01C", sysout[0]);
        Assert.Equal("END OF EXECUTION OF PROGRAM CBTRN01C", sysout[^1]);
        // At minimum the start + each displayed daily record + end (it displays one line per record, plus any
        // not-found diagnostics) — so the line count is strictly greater than the record count.
        Assert.True(sysout.Count > daily);
    }

    // =================================================================================================
    // CSUTLDTC — date validator
    // =================================================================================================

    /// <summary>
    /// CSUTLDTC validates a known good date (2024-02-29, a real leap day) and a known bad date (2023-02-29,
    /// not a leap year). A good date returns severity 0 and the verdict "Date is valid"; a bad date returns
    /// severity 3 and "Datevalue error" (message 2513), which is exactly the condition the CardDemo callers
    /// branch on.
    /// </summary>
    [Fact]
    public void Csutldtc_ValidatesGoodAndBadDates()
    {
        // Good date.
        Csutldtc good = Csutldtc.Run("2024-02-29", "YYYY-MM-DD", out string goodMsg, out int goodRc);
        Assert.Equal(0, goodRc);
        Assert.Equal("0000", goodMsg[..4]);          // bytes 1-4 = severity (callers test '0000')
        Assert.Contains("Date is valid", goodMsg);

        // Bad date — Feb 29 in a non-leap year.
        Csutldtc bad = Csutldtc.Run("2023-02-29", "YYYY-MM-DD", out string badMsg, out int badRc);
        Assert.Equal(3, badRc);                      // LE error severity
        Assert.Equal("0003", badMsg[..4]);
        Assert.Equal("2513", badMsg.Substring(15, 4)); // bytes 16-19 = message number
        Assert.Contains("Datevalue error", badMsg);

        // The other shipped mask (YYYYMMDD) also validates a good date.
        Csutldtc good8 = Csutldtc.Run("20240229", "YYYYMMDD", out string good8Msg, out int good8Rc);
        Assert.Equal(0, good8Rc);
        Assert.Contains("Date is valid", good8Msg);
    }

    // =================================================================================================
    // Additional ported programs complete sanely
    // =================================================================================================

    /// <summary>CBACT01C reads the account master sequentially and produces its banners + per-account DISPLAY lines.</summary>
    [Fact]
    public void Cbact01c_ReadsAccountMaster_AndWritesOutputDatasets()
    {
        using BatchSupport s = OpenSeeded(out _);
        long accts = s.Account.ReadAll().LongCount();
        Assert.True(accts > 0);

        string outFile = TempFile("cbact01c.out");
        string arryFile = TempFile("cbact01c.arry");
        string vbrcFile = TempFile("cbact01c.vbrc");

        IReadOnlyList<string> sysout = Cbact01c.Run(s, outFile, arryFile, vbrcFile);

        Assert.Equal("START OF EXECUTION OF PROGRAM CBACT01C", sysout[0]);
        Assert.Equal("END OF EXECUTION OF PROGRAM CBACT01C", sysout[^1]);
        Assert.True(sysout.Count >= accts + 2);
        Assert.True(File.Exists(outFile));
    }

    /// <summary>CBTRN03C transaction-detail report runs after a posting run and emits its banners + a report file.</summary>
    [Fact]
    public void Cbtrn03c_ProducesTransactionDetailReport()
    {
        using BatchSupport s = OpenSeeded(out _);

        // Build the TRANSACTION table via a posting run so there is something to report on.
        string rejPath = TempFile("dalyrejs-for-rept");
        new Cbtrn02c().Run(s, rejPath, clock: Clock);

        string reportPath = TempFile("tranrept");
        IReadOnlyList<string> sysout = Cbtrn03c.Run(s.Db, reportPath, startDate: "0000-00-00", endDate: "9999-99-99");

        Assert.Equal("START OF EXECUTION OF PROGRAM CBTRN03C", sysout[0]);
        Assert.Equal("END OF EXECUTION OF PROGRAM CBTRN03C", sysout[^1]);
        Assert.True(File.Exists(reportPath));
        Assert.True(new FileInfo(reportPath).Length > 0);
    }

    /// <summary>COBSWAIT derives the centisecond wait count from the SYSIN card and calls MVSWAIT once.</summary>
    [Fact]
    public void Cobswait_DerivesWaitCount_FromSysinCard()
    {
        var waiter = new Cobswait.RecordingWaiter();
        Cobswait prog = Cobswait.Run("00000010", waiter, HostKind.Ebcdic);

        Assert.Equal(10, prog.MvswaitTime);
        Assert.True(waiter.WasCalled);
        Assert.Equal(10, waiter.LastCentiseconds);
    }

    // =================================================================================================
    // Helpers
    // =================================================================================================

    private static void SeedOneInterestAccount(
        BatchSupport s, long acctId, string groupId, string typeCd, int catCd,
        decimal balance, decimal rate, string cardNum, decimal startBal)
    {
        Assert.Equal(FileStatus.Ok, s.Account.Insert(new Account
        {
            AcctId = acctId, ActiveStatus = "Y", CurrBal = startBal, CreditLimit = 100000.00m,
            CashCreditLimit = 100000.00m, OpenDate = "2020-01-01", ExpirationDate = "2099-12-31",
            ReissueDate = "2020-01-01", CurrCycCredit = 0m, CurrCycDebit = 0m,
            AddrZip = "12345", GroupId = groupId,
        }));
        Assert.Equal(FileStatus.Ok, s.CardXref.Insert(new CardXref { XrefCardNum = cardNum, CustId = 1, AcctId = acctId }));
        Assert.Equal(FileStatus.Ok, s.TranCatBalance.Insert(new TranCatBalance
        {
            AcctId = acctId, TypeCd = typeCd, CatCd = catCd, TranCatBal = balance,
        }));
        Assert.Equal(FileStatus.Ok, s.DisclosureGroup.Insert(new DisclosureGroup
        {
            AcctGroupId = groupId, TranTypeCd = typeCd, TranCatCd = catCd, IntRate = rate,
        }));
    }

    private static DailyTransaction NewDaily(string tranId, string card, decimal amt, string origTs) => new()
    {
        TranId = tranId, TypeCd = "01", CatCd = 1, Source = "POS", Desc = "test",
        Amt = amt, MerchantId = 1, MerchantName = "M", MerchantCity = "C", MerchantZip = "00000",
        CardNum = card, OrigTs = origTs, ProcTs = origTs,
    };

    /// <summary>Record length for the export record-type tag, from the parsed copybook layout.</summary>
    private static int RecLenFor(char type, RecordLayouts layouts) => type switch
    {
        'A' => layouts.For(CardDemoFiles.Account.Copybook).Length,
        'D' => layouts.For(CardDemoFiles.Card.Copybook).Length,
        'X' => layouts.For(CardDemoFiles.CardXref.Copybook).Length,
        'C' => layouts.For(CardDemoFiles.Customer.Copybook).Length,
        'B' => layouts.For(CardDemoFiles.TranCatBal.Copybook).Length,
        'G' => layouts.For(CardDemoFiles.DiscGroup.Copybook).Length,
        'Y' => layouts.For(CardDemoFiles.TranType.Copybook).Length,
        'K' => layouts.For(CardDemoFiles.TranCategory.Copybook).Length,
        'U' => layouts.For(CardDemoFiles.UserSecurity.Copybook).Length,
        'L' => layouts.For(CardDemoFiles.DailyTransactions.Copybook).Length,
        _ => throw new InvalidDataException($"Unknown export record type '{type}'."),
    };

    /// <summary>Parses one export record image and inserts the mapped entity into the destination DB.</summary>
    private static void ImportOne(char type, ReadOnlySpan<byte> image, RecordLayouts layouts, BatchSupport dst)
    {
        switch (type)
        {
            case 'A': Insert(dst.Account.Insert(FromAccount(Parse(CardDemoFiles.Account.Copybook, image, layouts)))); break;
            case 'D': Insert(dst.Card.Insert(FromCard(Parse(CardDemoFiles.Card.Copybook, image, layouts)))); break;
            case 'X': Insert(dst.CardXref.Insert(FromXref(Parse(CardDemoFiles.CardXref.Copybook, image, layouts)))); break;
            case 'C': Insert(dst.Customer.Insert(FromCustomer(Parse(CardDemoFiles.Customer.Copybook, image, layouts)))); break;
            case 'B': Insert(dst.TranCatBalance.Insert(FromTcatbal(Parse(CardDemoFiles.TranCatBal.Copybook, image, layouts)))); break;
            case 'G': Insert(dst.DisclosureGroup.Insert(FromDiscgrp(Parse(CardDemoFiles.DiscGroup.Copybook, image, layouts)))); break;
            case 'Y': Insert(dst.TranType.Insert(FromTrantype(Parse(CardDemoFiles.TranType.Copybook, image, layouts)))); break;
            case 'K': Insert(dst.TranCategory.Insert(FromTrancatg(Parse(CardDemoFiles.TranCategory.Copybook, image, layouts)))); break;
            case 'U': Insert(dst.UserSecurity.Insert(FromUsrsec(Parse(CardDemoFiles.UserSecurity.Copybook, image, layouts)))); break;
            case 'L': Insert(dst.DailyTransaction.Insert(FromDaily(Parse(CardDemoFiles.DailyTransactions.Copybook, image, layouts)))); break;
            default: throw new InvalidDataException($"Unknown export record type '{type}'.");
        }
    }

    private static void Insert(string status) => Assert.Equal(FileStatus.Ok, status);

    private static FixedRecord Parse(string copybook, ReadOnlySpan<byte> image, RecordLayouts layouts)
        => FixedRecord.Parse(layouts.For(copybook), image, HostKind.Ebcdic);

    // Field -> entity (the inverse of RecordSerializer; mirrors MasterImporter's mappers).
    private static Account FromAccount(FixedRecord r) => new()
    {
        AcctId = (long)r.GetNumber("ACCT-ID"), ActiveStatus = r.GetText("ACCT-ACTIVE-STATUS"),
        CurrBal = r.GetNumber("ACCT-CURR-BAL"), CreditLimit = r.GetNumber("ACCT-CREDIT-LIMIT"),
        CashCreditLimit = r.GetNumber("ACCT-CASH-CREDIT-LIMIT"), OpenDate = r.GetText("ACCT-OPEN-DATE"),
        ExpirationDate = r.GetText("ACCT-EXPIRAION-DATE"), ReissueDate = r.GetText("ACCT-REISSUE-DATE"),
        CurrCycCredit = r.GetNumber("ACCT-CURR-CYC-CREDIT"), CurrCycDebit = r.GetNumber("ACCT-CURR-CYC-DEBIT"),
        AddrZip = r.GetText("ACCT-ADDR-ZIP"), GroupId = r.GetText("ACCT-GROUP-ID"),
    };

    private static Card FromCard(FixedRecord r) => new()
    {
        CardNum = r.GetText("CARD-NUM"), AcctId = (long)r.GetNumber("CARD-ACCT-ID"),
        CvvCd = (int)r.GetNumber("CARD-CVV-CD"), EmbossedName = r.GetText("CARD-EMBOSSED-NAME"),
        ExpirationDate = r.GetText("CARD-EXPIRAION-DATE"), ActiveStatus = r.GetText("CARD-ACTIVE-STATUS"),
    };

    private static CardXref FromXref(FixedRecord r) => new()
    {
        XrefCardNum = r.GetText("XREF-CARD-NUM"), CustId = (long)r.GetNumber("XREF-CUST-ID"),
        AcctId = (long)r.GetNumber("XREF-ACCT-ID"),
    };

    private static Customer FromCustomer(FixedRecord r) => new()
    {
        CustId = (long)r.GetNumber("CUST-ID"), FirstName = r.GetText("CUST-FIRST-NAME"),
        MiddleName = r.GetText("CUST-MIDDLE-NAME"), LastName = r.GetText("CUST-LAST-NAME"),
        AddrLine1 = r.GetText("CUST-ADDR-LINE-1"), AddrLine2 = r.GetText("CUST-ADDR-LINE-2"),
        AddrLine3 = r.GetText("CUST-ADDR-LINE-3"), AddrStateCd = r.GetText("CUST-ADDR-STATE-CD"),
        AddrCountryCd = r.GetText("CUST-ADDR-COUNTRY-CD"), AddrZip = r.GetText("CUST-ADDR-ZIP"),
        PhoneNum1 = r.GetText("CUST-PHONE-NUM-1"), PhoneNum2 = r.GetText("CUST-PHONE-NUM-2"),
        Ssn = (long)r.GetNumber("CUST-SSN"), GovtIssuedId = r.GetText("CUST-GOVT-ISSUED-ID"),
        DobYyyyMmDd = r.GetText("CUST-DOB-YYYY-MM-DD"), EftAccountId = r.GetText("CUST-EFT-ACCOUNT-ID"),
        PriCardHolderInd = r.GetText("CUST-PRI-CARD-HOLDER-IND"), FicoCreditScore = (int)r.GetNumber("CUST-FICO-CREDIT-SCORE"),
    };

    private static TranCatBalance FromTcatbal(FixedRecord r) => new()
    {
        AcctId = (long)r.GetNumber("TRANCAT-ACCT-ID"), TypeCd = r.GetText("TRANCAT-TYPE-CD"),
        CatCd = (int)r.GetNumber("TRANCAT-CD"), TranCatBal = r.GetNumber("TRAN-CAT-BAL"),
    };

    private static DisclosureGroup FromDiscgrp(FixedRecord r) => new()
    {
        AcctGroupId = r.GetText("DIS-ACCT-GROUP-ID"), TranTypeCd = r.GetText("DIS-TRAN-TYPE-CD"),
        TranCatCd = (int)r.GetNumber("DIS-TRAN-CAT-CD"), IntRate = r.GetNumber("DIS-INT-RATE"),
    };

    private static TranType FromTrantype(FixedRecord r) => new()
    {
        TranTypeCode = r.GetText("TRAN-TYPE"), TranTypeDesc = r.GetText("TRAN-TYPE-DESC"),
    };

    private static TranCategory FromTrancatg(FixedRecord r) => new()
    {
        TranTypeCd = r.GetText("TRAN-TYPE-CD"), TranCatCd = (int)r.GetNumber("TRAN-CAT-CD"),
        TranCatTypeDesc = r.GetText("TRAN-CAT-TYPE-DESC"),
    };

    private static UserSecurity FromUsrsec(FixedRecord r) => new()
    {
        UsrId = r.GetText("SEC-USR-ID"), FirstName = r.GetText("SEC-USR-FNAME"),
        LastName = r.GetText("SEC-USR-LNAME"), Pwd = r.GetText("SEC-USR-PWD"), UsrType = r.GetText("SEC-USR-TYPE"),
    };

    private static DailyTransaction FromDaily(FixedRecord r) => new()
    {
        TranId = r.GetText("DALYTRAN-ID"), TypeCd = r.GetText("DALYTRAN-TYPE-CD"),
        CatCd = (int)r.GetNumber("DALYTRAN-CAT-CD"), Source = r.GetText("DALYTRAN-SOURCE"),
        Desc = r.GetText("DALYTRAN-DESC"), Amt = r.GetNumber("DALYTRAN-AMT"),
        MerchantId = (long)r.GetNumber("DALYTRAN-MERCHANT-ID"), MerchantName = r.GetText("DALYTRAN-MERCHANT-NAME"),
        MerchantCity = r.GetText("DALYTRAN-MERCHANT-CITY"), MerchantZip = r.GetText("DALYTRAN-MERCHANT-ZIP"),
        CardNum = r.GetText("DALYTRAN-CARD-NUM"), OrigTs = r.GetText("DALYTRAN-ORIG-TS"),
        ProcTs = r.GetText("DALYTRAN-PROC-TS"),
    };

    /// <summary>
    /// Builds a <see cref="Cbimport.Context"/> for the program-level tests: the five output paths + ERROUT,
    /// the shared serializer, and the five CVEXPORT record-type variant layouts parsed from
    /// <c>CVEXPORT.cpy</c>. <paramref name="cardOutPath"/> defaults to <c>null</c> (FB-1, JCL-faithful);
    /// supply a path to enable the type-'D' CARD sink.
    /// </summary>
    private static Cbimport.Context NewImportContext(
        string exportPath, string custOut, string acctOut, string xrefOut, string trnxOut, string errOut,
        RecordSerializer serializer, string? cardOutPath)
    {
        string cvexport = File.ReadAllText(Path.Combine(SeedPaths.CopybookDir, "CVEXPORT.cpy"));
        return new Cbimport.Context(
            ExportPath: exportPath,
            CustomerOutPath: custOut,
            AccountOutPath: acctOut,
            XrefOutPath: xrefOut,
            TransactionOutPath: trnxOut,
            ErrorOutPath: errOut,
            Serializer: serializer,
            CustomerVariant: CopybookParser.ParseVariant(cvexport, "EXPORT-CUSTOMER-DATA"),
            AccountVariant: CopybookParser.ParseVariant(cvexport, "EXPORT-ACCOUNT-DATA"),
            XrefVariant: CopybookParser.ParseVariant(cvexport, "EXPORT-CARD-XREF-DATA"),
            TransactionVariant: CopybookParser.ParseVariant(cvexport, "EXPORT-TRANSACTION-DATA"),
            CardVariant: CopybookParser.ParseVariant(cvexport, "EXPORT-CARD-DATA"),
            Clock: Clock,
            Host: HostKind.Ebcdic,
            CardOutPath: cardOutPath);
    }

    /// <summary>
    /// Asserts the whole flat output file <paramref name="path"/> equals the concatenation of the
    /// <paramref name="expectedRecords"/> images, byte-for-byte (record count + each record's bytes).
    /// </summary>
    private static void AssertFileEquals(string name, string path, IEnumerable<byte[]> expectedRecords)
    {
        Assert.True(File.Exists(path), $"{name}: output file was not written.");
        byte[] actual = File.ReadAllBytes(path);

        List<byte[]> expected = expectedRecords.ToList();
        int recLen = expected.Count > 0 ? expected[0].Length : 0;
        long expectedLen = expected.Sum(r => (long)r.Length);
        Assert.True(actual.LongLength == expectedLen,
            $"{name}: file length {actual.LongLength} != expected {expectedLen}.");

        int pos = 0;
        for (int i = 0; i < expected.Count; i++)
        {
            byte[] rec = expected[i];
            Assert.True(actual.AsSpan(pos, rec.Length).SequenceEqual(rec),
                $"{name}: record {i} (offset {pos}, len {rec.Length}) differs from the re-serialized seed row.");
            pos += rec.Length;
        }
        _ = recLen;
    }

    private static void AssertSameImages(string table, IEnumerable<byte[]> expected, IEnumerable<byte[]> actual)
    {
        List<byte[]> e = expected.ToList();
        List<byte[]> a = actual.ToList();
        Assert.True(e.Count == a.Count, $"{table}: row count {a.Count} != expected {e.Count}");
        for (int i = 0; i < e.Count; i++)
            Assert.True(e[i].AsSpan().SequenceEqual(a[i]),
                $"{table}: row {i} re-imported image differs from source image.");
    }

    private static int CountLines(string path)
        => File.ReadAllText(path).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

    private static string TempFile(string name)
    {
        string dir = Path.Combine(Path.GetTempPath(), "carddemo-batch-tests");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{name}-{Guid.NewGuid():N}.dat");
    }
}
