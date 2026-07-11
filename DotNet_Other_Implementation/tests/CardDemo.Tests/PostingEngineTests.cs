using CardDemo.Domain.Entities;
using CardDemo.Domain.Services;
using Xunit;

namespace CardDemo.Tests;

public sealed class PostingEngineTests
{
    private const string Ts = "2024-06-10 12:00:00.000000";
    private const string ProcTs = "2024-06-11-00.00.00.000000";

    private static Transaction Daily(string card, decimal amount, string type = "01", string cat = "0001") =>
        new()
        {
            TransactionId = "0000000000000001",
            TypeCode = type,
            CategoryCode = cat,
            CardNumber = card,
            Amount = amount,
            OriginTimestamp = Ts,
        };

    private static (PostingWorld world, Account account) BuildWorld(
        decimal creditLimit,
        decimal cycleCredit = 0m,
        decimal cycleDebit = 0m)
    {
        var account = new Account
        {
            AccountId = "00000000001",
            CreditLimit = creditLimit,
            CurrentBalance = 0m,
            CurrentCycleCredit = cycleCredit,
            CurrentCycleDebit = cycleDebit,
            ExpirationDate = "9999-12-31",
        };
        var xref = new CardXref { CardNumber = "1111222233334444", CustomerId = "000000001", AccountId = "00000000001" };
        var world = new PostingWorld([xref], [account], []);
        return (world, account);
    }

    [Fact]
    public void Post_AcceptedCase_MutatesBalancesAndPosts()
    {
        var (world, account) = BuildWorld(creditLimit: 1000m);
        var engine = new PostingEngine();

        var outcome = engine.Post([Daily("1111222233334444", 194.00m)], world, ProcTs);

        Assert.Equal(1, outcome.AcceptedCount);
        Assert.Equal(0, outcome.RejectedCount);
        Assert.Equal(194.00m, account.CurrentBalance);
        Assert.Equal(194.00m, account.CurrentCycleCredit);
        Assert.Single(outcome.CreatedCategoryBalances);
        Assert.Equal(194.00m, outcome.CreatedCategoryBalances[0].Balance);
        Assert.Equal(ProcTs, outcome.Posted[0].ProcessTimestamp);
    }

    [Fact]
    public void Post_NoXref_Rejects100()
    {
        var (world, _) = BuildWorld(creditLimit: 1000m);
        var engine = new PostingEngine();

        var outcome = engine.Post([Daily("9999888877776666", 10m)], world, ProcTs);

        Assert.Equal(0, outcome.AcceptedCount);
        var reject = Assert.Single(outcome.Rejects);
        Assert.Equal(PostingRejectReason.InvalidCardNumber, reject.ReasonCode);
    }

    [Fact]
    public void Post_NoAccount_Rejects101()
    {
        // Xref present but its account is not in the world.
        var xref = new CardXref { CardNumber = "1111222233334444", CustomerId = "000000001", AccountId = "00000000099" };
        var world = new PostingWorld([xref], System.Array.Empty<Account>(), []);
        var engine = new PostingEngine();

        var outcome = engine.Post([Daily("1111222233334444", 10m)], world, ProcTs);

        Assert.Equal(0, outcome.AcceptedCount);
        var reject = Assert.Single(outcome.Rejects);
        Assert.Equal(PostingRejectReason.AccountNotFound, reject.ReasonCode);
    }

    [Fact]
    public void Post_Overlimit_Rejects102()
    {
        // tempBal = cycleCredit(0) - cycleDebit(0) + 500 = 500 > creditLimit 100 => 102.
        var (world, account) = BuildWorld(creditLimit: 100m);
        var engine = new PostingEngine();

        var outcome = engine.Post([Daily("1111222233334444", 500m)], world, ProcTs);

        Assert.Equal(0, outcome.AcceptedCount);
        var reject = Assert.Single(outcome.Rejects);
        Assert.Equal(PostingRejectReason.Overlimit, reject.ReasonCode);
        // Rejected transactions must not mutate the account.
        Assert.Equal(0m, account.CurrentBalance);
    }
}
