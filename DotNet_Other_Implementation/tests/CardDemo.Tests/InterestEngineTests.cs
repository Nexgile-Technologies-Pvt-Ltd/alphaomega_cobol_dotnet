using CardDemo.Domain.Entities;
using CardDemo.Domain.Services;
using Xunit;

namespace CardDemo.Tests;

public sealed class InterestEngineTests
{
    private const string CycleId = "0000000001";
    private const string ProcTs = "2024-06-11-00.00.00.000000";

    private static Account Account(string id, decimal balance = 0m, string group = "DEFAULT") =>
        new()
        {
            AccountId = id,
            GroupId = group,
            CurrentBalance = balance,
            CurrentCycleCredit = 5m,
            CurrentCycleDebit = 3m,
        };

    private static TransactionCategoryBalance Cat(string acct, decimal balance, string type = "01", string cat = "0005") =>
        new() { AccountId = acct, TypeCode = type, CategoryCode = cat, Balance = balance };

    private static DisclosureGroup Disc(decimal rate, string group = "DEFAULT", string type = "01", string cat = "0005") =>
        new() { GroupId = group, TypeCode = type, CategoryCode = cat, InterestRate = rate };

    private static CardXref Xref(string card, string acct) =>
        new() { CardNumber = card, CustomerId = "000000001", AccountId = acct };

    [Fact]
    public void Run_InterestFormula_AndTransactionShape()
    {
        // interest = truncate2(1000 * 12 / 1200) = 10.00
        var account = Account("00000000001", balance: 100m);
        var world = new InterestWorld([account], [Disc(12m)], [Xref("1111222233334444", "00000000001")]);
        var engine = new InterestEngine();

        var outcome = engine.Run([Cat("00000000001", 1000m)], world, CycleId, ProcTs);

        var tran = Assert.Single(outcome.InterestTransactions);
        Assert.Equal(10.00m, tran.Amount);
        Assert.Equal("01", tran.TypeCode);
        Assert.Equal("0005", tran.CategoryCode);
        // id = cycleId + 6-digit suffix; first suffix is 000001.
        Assert.Equal(CycleId + "000001", tran.TransactionId);
        Assert.Equal("1111222233334444", tran.CardNumber);
        Assert.Equal(10.00m, outcome.TotalInterest);
    }

    [Fact]
    public void Run_TruncatesToTwoDecimals()
    {
        // 100.99 * 12 / 1200 = 1.0099 => truncate2 => 1.00
        var account = Account("00000000001");
        var world = new InterestWorld([account], [Disc(12m)], [Xref("1111222233334444", "00000000001")]);
        var engine = new InterestEngine();

        var outcome = engine.Run([Cat("00000000001", 100.99m)], world, CycleId, ProcTs);

        Assert.Equal(1.00m, outcome.InterestTransactions[0].Amount);
    }

    [Fact]
    public void Run_ZeroRate_ProducesNoTransaction()
    {
        var account = Account("00000000001");
        var world = new InterestWorld([account], [Disc(0m)], [Xref("1111222233334444", "00000000001")]);
        var engine = new InterestEngine();

        var outcome = engine.Run([Cat("00000000001", 1000m)], world, CycleId, ProcTs);

        Assert.Empty(outcome.InterestTransactions);
    }

    [Fact]
    public void Run_UpdateFinalAccountTrue_UpdatesLastAccount()
    {
        var acct1 = Account("00000000001", balance: 0m);
        var acct2 = Account("00000000002", balance: 0m);
        var world = new InterestWorld(
            [acct1, acct2],
            [Disc(12m)],
            [Xref("1111000000000001", "00000000001"), Xref("1111000000000002", "00000000002")]);
        var engine = new InterestEngine();

        // Ordered by account: acct1 then acct2 (acct2 is the final account).
        var balances = new[] { Cat("00000000001", 1000m), Cat("00000000002", 1000m) };

        var outcome = engine.Run(balances, world, CycleId, ProcTs, startingSuffix: 0, updateFinalAccount: true);

        Assert.Equal(2, outcome.UpdatedAccounts.Count);
        Assert.Equal(10.00m, acct1.CurrentBalance);
        Assert.Equal(10.00m, acct2.CurrentBalance); // final account updated
        Assert.Equal(0m, acct2.CurrentCycleCredit);  // accumulators reset on flush
        Assert.Equal(0m, acct2.CurrentCycleDebit);
    }

    [Fact]
    public void Run_UpdateFinalAccountFalse_SkipsLastAccount()
    {
        var acct1 = Account("00000000001", balance: 0m);
        var acct2 = Account("00000000002", balance: 0m);
        var world = new InterestWorld(
            [acct1, acct2],
            [Disc(12m)],
            [Xref("1111000000000001", "00000000001"), Xref("1111000000000002", "00000000002")]);
        var engine = new InterestEngine();

        var balances = new[] { Cat("00000000001", 1000m), Cat("00000000002", 1000m) };

        var outcome = engine.Run(balances, world, CycleId, ProcTs, startingSuffix: 0, updateFinalAccount: false);

        // Only the first account is flushed; the last is skipped (strict CBACT04C quirk).
        Assert.Single(outcome.UpdatedAccounts);
        Assert.Equal("00000000001", outcome.UpdatedAccounts[0].AccountId);
        Assert.Equal(10.00m, acct1.CurrentBalance);
        Assert.Equal(0m, acct2.CurrentBalance);            // final account NOT updated
        Assert.Equal(5m, acct2.CurrentCycleCredit);        // accumulators NOT reset
        // But the interest transaction for the last account IS still emitted.
        Assert.Equal(2, outcome.InterestTransactions.Count);
    }
}
