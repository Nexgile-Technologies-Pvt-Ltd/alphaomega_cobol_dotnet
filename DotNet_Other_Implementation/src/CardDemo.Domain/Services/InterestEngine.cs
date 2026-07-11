using CardDemo.Domain.Entities;

namespace CardDemo.Domain.Services;

/// <summary>Outcome of an interest-calculation run (CBACT04C).</summary>
public sealed class InterestOutcome
{
    public List<Transaction> InterestTransactions { get; } = [];
    public List<Account> UpdatedAccounts { get; } = [];
    public decimal TotalInterest { get; set; }

    /// <summary>Last transaction-ID suffix used (WS-TRANID-SUFFIX).</summary>
    public int LastSuffix { get; set; }
}

/// <summary>In-memory reference data for the interest engine.</summary>
public sealed class InterestWorld
{
    public const string DefaultGroupId = "DEFAULT";

    private readonly Dictionary<string, Account> _accounts;
    private readonly Dictionary<string, decimal> _rates;
    private readonly Dictionary<string, string> _cardByAccount;

    public InterestWorld(
        IEnumerable<Account> accounts,
        IEnumerable<DisclosureGroup> disclosureGroups,
        IEnumerable<CardXref> xrefs)
    {
        _accounts = accounts.ToDictionary(a => a.AccountId);
        _rates = disclosureGroups
            .GroupBy(d => RateKey(d.GroupId, d.TypeCode, d.CategoryCode))
            .ToDictionary(g => g.Key, g => g.First().InterestRate);
        _cardByAccount = xrefs
            .GroupBy(x => x.AccountId)
            .ToDictionary(g => g.Key, g => g.First().CardNumber);
    }

    private static string RateKey(string group, string type, string cat) => $"{group.Trim()}|{type}|{cat}";

    public Account? FindAccount(string accountId) =>
        _accounts.TryGetValue(accountId, out var a) ? a : null;

    /// <summary>Rate lookup by (group,type,cat) with a DEFAULT-group fallback (1200-GET-INTEREST-RATE).</summary>
    public decimal FindRate(string groupId, string type, string cat)
    {
        if (_rates.TryGetValue(RateKey(groupId, type, cat), out var rate))
            return rate;
        if (_rates.TryGetValue(RateKey(DefaultGroupId, type, cat), out var def))
            return def;
        return 0m;
    }

    public string? FindCardByAccount(string accountId) =>
        _cardByAccount.TryGetValue(accountId, out var c) ? c : null;
}

/// <summary>
/// Reproduces CBACT04C interest calculation. Category balances are consumed in
/// key order (account, type, category). Interest per category is
/// <c>(TRAN-CAT-BAL * DIS-INT-RATE) / 1200</c> truncated to 2 dp; a transaction of
/// type '01' category '0005' is written for each non-zero rate; and each account's
/// accumulated interest is added to its balance with the cycle accumulators reset.
///
/// The legacy program never rewrites the final account (the EOF branch is
/// unreachable — see 14-Known-Defects "Interest final-account update"). The safe
/// default here DOES update the final account; set
/// <paramref name="updateFinalAccount"/> = false to reproduce the strict quirk.
/// </summary>
public sealed class InterestEngine
{
    public InterestOutcome Run(
        IEnumerable<TransactionCategoryBalance> orderedCategoryBalances,
        InterestWorld world,
        string cycleId,
        string processTimestamp,
        int startingSuffix = 0,
        bool updateFinalAccount = true)
    {
        ArgumentNullException.ThrowIfNull(orderedCategoryBalances);
        ArgumentNullException.ThrowIfNull(world);

        var outcome = new InterestOutcome { LastSuffix = startingSuffix };
        int suffix = startingSuffix;

        Account? current = null;
        string? currentCard = null;
        decimal totalInterest = 0m;
        bool first = true;

        foreach (var cb in orderedCategoryBalances)
        {
            if (current is null || !string.Equals(cb.AccountId, current.AccountId, StringComparison.Ordinal))
            {
                if (!first && current is not null)
                    Flush(current, totalInterest, outcome);

                first = false;
                totalInterest = 0m;
                current = world.FindAccount(cb.AccountId);
                currentCard = world.FindCardByAccount(cb.AccountId);
            }

            if (current is null)
                continue; // account missing; nothing to price

            var rate = world.FindRate(current.GroupId, cb.TypeCode, cb.CategoryCode);
            if (rate == 0m)
                continue;

            var interest = MoneyMath.Truncate2(cb.Balance * rate / 1200m);
            totalInterest += interest;
            suffix++;

            outcome.InterestTransactions.Add(new Transaction
            {
                TransactionId = cycleId + suffix.ToString("D6"),
                TypeCode = "01",
                CategoryCode = "0005",
                Source = "System",
                Description = "Int. for a/c " + current.AccountId,
                Amount = interest,
                MerchantId = "000000000",
                CardNumber = currentCard ?? string.Empty,
                OriginTimestamp = processTimestamp,
                ProcessTimestamp = processTimestamp,
            });
        }

        // Final account: safe mode updates it; strict mode reproduces the EOF-skip defect.
        if (!first && current is not null && updateFinalAccount)
            Flush(current, totalInterest, outcome);

        outcome.LastSuffix = suffix;
        outcome.TotalInterest = outcome.InterestTransactions.Sum(t => t.Amount);
        return outcome;
    }

    private static void Flush(Account account, decimal totalInterest, InterestOutcome outcome)
    {
        // 1050-UPDATE-ACCOUNT: add accrued interest and reset the cycle accumulators.
        account.CurrentBalance += totalInterest;
        account.CurrentCycleCredit = 0m;
        account.CurrentCycleDebit = 0m;
        outcome.UpdatedAccounts.Add(account);
    }
}
