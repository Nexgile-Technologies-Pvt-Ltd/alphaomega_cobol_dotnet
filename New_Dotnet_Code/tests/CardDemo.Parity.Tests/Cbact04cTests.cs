using CardDemo.Batch;
using CardDemo.Cobol.Runtime;
using CardDemo.Data;
using CardDemo.Tooling;
using Xunit;

namespace CardDemo.Parity.Tests;

/// <summary>
/// Drives the <see cref="Cbact04cInterestCalculator"/> port against the real bootstrapped data and
/// cross-checks its output transactions against an independent recomputation of the interest, plus the
/// faithful "last account is never balance-updated" quirk. (The GnuCOBOL byte-for-byte golden diff is a
/// separate gate added once cobc is available.)
/// </summary>
public class Cbact04cTests
{
    private const string ParmDate = "2022071800";
    private static readonly HostKind Host = HostKind.Ebcdic;

    private sealed record Layouts(RecordLayout TcatBal, RecordLayout Xref, RecordLayout Account, RecordLayout DiscGrp, RecordLayout Tran);

    private static Layouts LoadLayouts() => new(
        CopybookParser.Parse(File.ReadAllText(CardDemoPaths.Copybook("CVTRA01Y.cpy"))),
        CopybookParser.Parse(File.ReadAllText(CardDemoPaths.Copybook("CVACT03Y.cpy"))),
        CopybookParser.Parse(File.ReadAllText(CardDemoPaths.Copybook("CVACT01Y.cpy"))),
        CopybookParser.Parse(File.ReadAllText(CardDemoPaths.Copybook("CVTRA02Y.cpy"))),
        CopybookParser.Parse(File.ReadAllText(CardDemoPaths.Copybook("CVTRA05Y.cpy"))));

    private static (CardDemoDatabase db, BootstrapResult boot, SequentialFile sysTran) Setup()
    {
        var db = new CardDemoDatabase();
        BootstrapResult boot = new DataBootstrapper(CardDemoPaths.EbcdicDataDir).BootstrapAll(db);
        SequentialFile sysTran = db.DefineSequentialFile("SYSTRAN", 350);
        return (db, boot, sysTran);
    }

    private static Cbact04cContext Context(BootstrapResult boot, SequentialFile sysTran, Layouts l) => new(
        boot.File("TCATBALF"), boot.File("CARDXREF"), boot.File("ACCTFILE"), boot.File("DISCGRP"), sysTran,
        l.TcatBal, l.Xref, l.Account, l.DiscGrp, l.Tran,
        new FixedClock(new DateTime(2022, 7, 18, 12, 0, 0)), Host, ParmDate);

    [Fact]
    public void Run_completes_without_abend_and_writes_interest_transactions()
    {
        Layouts l = LoadLayouts();
        var (db, boot, sysTran) = Setup();
        using (db)
        {
            var program = new Cbact04cInterestCalculator(Context(boot, sysTran, l));
            int rc = program.Run();

            Assert.Equal(0, rc);
            Assert.Equal("START OF EXECUTION OF PROGRAM CBACT04C", program.Sysout[0]);
            Assert.Equal("END OF EXECUTION OF PROGRAM CBACT04C", program.Sysout[^1]);
            Assert.True(sysTran.Count() > 0, "expected at least one interest transaction");
        }
    }

    [Fact]
    public void Output_transactions_match_independent_interest_recomputation()
    {
        Layouts l = LoadLayouts();

        // Expected: recompute interest independently from a clean bootstrap.
        var (dbE, bootE, _) = Setup();
        List<(decimal Amt, string Card)> expected;
        using (dbE) expected = ExpectedInterest(bootE, l);

        // Actual: run the port and read SYSTRAN.
        var (dbA, bootA, sysTran) = Setup();
        using (dbA)
        {
            new Cbact04cInterestCalculator(Context(bootA, sysTran, l)).Run();

            sysTran.OpenInput();
            var actual = new List<(decimal Amt, string Card)>();
            while (sysTran.Read(out byte[]? img) == FileStatus.Ok)
            {
                FixedRecord t = FixedRecord.Parse(l.Tran, img!, Host);
                actual.Add((t.GetNumber("TRAN-AMT"), t.GetText("TRAN-CARD-NUM")));
                Assert.Equal("01", t.GetText("TRAN-TYPE-CD"));
                Assert.Equal(5m, t.GetNumber("TRAN-CAT-CD"));
                Assert.Equal("System    ", t.GetText("TRAN-SOURCE"));
                Assert.StartsWith("Int. for a/c ", t.GetText("TRAN-DESC"));
                Assert.StartsWith(ParmDate, t.GetText("TRAN-ID"));
            }

            Assert.Equal(expected.Count, actual.Count);
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.Equal(expected[i].Amt, actual[i].Amt);
                Assert.Equal(expected[i].Card, actual[i].Card);
            }
        }
    }

    [Fact]
    public void Last_account_balance_is_not_updated_but_an_earlier_account_is()
    {
        Layouts l = LoadLayouts();

        var (dbBefore, bootBefore, _) = Setup();
        var (dbAfter, bootAfter, sysTran) = Setup();
        using (dbBefore)
        using (dbAfter)
        {
            // Determine the first and last distinct account ids in TCATBAL key order.
            List<string> accountOrder = DistinctAccountsInOrder(bootBefore, l);
            string firstAcct = accountOrder[0];
            string lastAcct = accountOrder[^1];

            decimal lastBalBefore = AccountBalance(bootBefore, l, lastAcct);

            new Cbact04cInterestCalculator(Context(bootAfter, sysTran, l)).Run();

            // Faithful quirk: the last account is never updated -> balance unchanged.
            Assert.Equal(lastBalBefore, AccountBalance(bootAfter, l, lastAcct));

            // An earlier account is updated by 1050: its cycle credit/debit are reset to zero.
            FixedRecord firstAfter = ReadAccount(bootAfter, l, firstAcct);
            Assert.Equal(0m, firstAfter.GetNumber("ACCT-CURR-CYC-CREDIT"));
            Assert.Equal(0m, firstAfter.GetNumber("ACCT-CURR-CYC-DEBIT"));
        }
    }

    // --- independent helpers ----------------------------------------------------------------------

    private static List<(decimal Amt, string Card)> ExpectedInterest(BootstrapResult boot, Layouts l)
    {
        var result = new List<(decimal, string)>();
        VsamFile tcat = boot.File("TCATBALF");
        tcat.StartBrowse();
        while (tcat.ReadNext(out byte[]? img) == FileStatus.Ok)
        {
            FixedRecord tb = FixedRecord.Parse(l.TcatBal, img!, Host);
            string acct = ((long)tb.GetNumber("TRANCAT-ACCT-ID")).ToString("D11");
            FixedRecord account = ReadAccount(boot, l, acct);
            string groupId = account.GetText("ACCT-GROUP-ID");
            string typeCd = tb.GetText("TRANCAT-TYPE-CD");
            decimal catCd = tb.GetNumber("TRANCAT-CD");

            decimal rate = ResolveRate(boot, l, groupId, typeCd, catCd);
            if (rate == 0m) continue;

            decimal amt = Decimals.Store(tb.GetNumber("TRAN-CAT-BAL") * rate / 1200m, 9, 2, true);
            string card = ReadXrefCard(boot, l, acct);
            result.Add((amt, card));
        }
        tcat.EndBrowse();
        return result;
    }

    private static decimal ResolveRate(BootstrapResult boot, Layouts l, string groupId, string typeCd, decimal catCd)
    {
        decimal? r = ReadRate(boot, l, groupId, typeCd, catCd);
        r ??= ReadRate(boot, l, "DEFAULT", typeCd, catCd);
        return r ?? throw new InvalidOperationException("No DEFAULT disclosure group rate found.");
    }

    private static decimal? ReadRate(BootstrapResult boot, Layouts l, string groupId, string typeCd, decimal catCd)
    {
        byte[] key = FixedRecord.CreateBlank(l.DiscGrp)
            .SetText("DIS-ACCT-GROUP-ID", groupId)
            .SetText("DIS-TRAN-TYPE-CD", typeCd)
            .SetNumber("DIS-TRAN-CAT-CD", catCd)
            .ToBytes(Host)[..16];
        if (boot.File("DISCGRP").Read(key, out byte[]? img) != FileStatus.Ok) return null;
        return FixedRecord.Parse(l.DiscGrp, img!, Host).GetNumber("DIS-INT-RATE");
    }

    private static List<string> DistinctAccountsInOrder(BootstrapResult boot, Layouts l)
    {
        var seen = new List<string>();
        string? last = null;
        VsamFile tcat = boot.File("TCATBALF");
        tcat.StartBrowse();
        while (tcat.ReadNext(out byte[]? img) == FileStatus.Ok)
        {
            string acct = ((long)FixedRecord.Parse(l.TcatBal, img!, Host).GetNumber("TRANCAT-ACCT-ID")).ToString("D11");
            if (acct != last) { seen.Add(acct); last = acct; }
        }
        tcat.EndBrowse();
        return seen;
    }

    private static FixedRecord ReadAccount(BootstrapResult boot, Layouts l, string acctId)
    {
        var key = new byte[11];
        ZonedDecimalCodec.Encode(decimal.Parse(acctId), key, 11, 0, false, Host);
        Assert.Equal(FileStatus.Ok, boot.File("ACCTFILE").Read(key, out byte[]? img));
        return FixedRecord.Parse(l.Account, img!, Host);
    }

    private static decimal AccountBalance(BootstrapResult boot, Layouts l, string acctId) =>
        ReadAccount(boot, l, acctId).GetNumber("ACCT-CURR-BAL");

    private static string ReadXrefCard(BootstrapResult boot, Layouts l, string acctId)
    {
        var altKey = new byte[11];
        ZonedDecimalCodec.Encode(decimal.Parse(acctId), altKey, 11, 0, false, Host);
        Assert.Equal(FileStatus.Ok, boot.File("CARDXREF").ReadByAlternateKey(altKey, out byte[]? img));
        return FixedRecord.Parse(l.Xref, img!, Host).GetText("XREF-CARD-NUM");
    }
}
