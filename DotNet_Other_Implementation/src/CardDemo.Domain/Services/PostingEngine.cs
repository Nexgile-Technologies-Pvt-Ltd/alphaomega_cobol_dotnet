using CardDemo.Domain.Entities;

namespace CardDemo.Domain.Services;

/// <summary>Reject reason codes set by CBTRN02C 1500-VALIDATE-TRAN.</summary>
public static class PostingRejectReason
{
    public const int InvalidCardNumber = 100;   // xref lookup by card failed
    public const int AccountNotFound = 101;     // account lookup by xref account failed
    public const int Overlimit = 102;           // credit limit < cycle-credit - cycle-debit + amount
    public const int AfterExpiration = 103;     // account expiration date < transaction date

    public static string Describe(int code) => code switch
    {
        InvalidCardNumber => "INVALID CARD NUMBER FOUND",
        AccountNotFound => "ACCOUNT RECORD NOT FOUND",
        Overlimit => "OVERLIMIT TRANSACTION",
        AfterExpiration => "TRANSACTION RECEIVED AFTER ACCT EXPIRATION",
        _ => "UNKNOWN REJECT REASON",
    };
}

/// <summary>A rejected daily transaction with its reason.</summary>
public sealed record PostingReject(Transaction Transaction, int ReasonCode)
{
    public string ReasonDescription => PostingRejectReason.Describe(ReasonCode);
}

/// <summary>Outcome of a posting run.</summary>
public sealed class PostingOutcome
{
    public List<Transaction> Posted { get; } = [];
    public List<PostingReject> Rejects { get; } = [];
    public List<TransactionCategoryBalance> CreatedCategoryBalances { get; } = [];

    public int AcceptedCount => Posted.Count;
    public int RejectedCount => Rejects.Count;
    public bool HasRejects => Rejects.Count > 0;
}

/// <summary>
/// In-memory data the posting engine reads and mutates. Built by the batch
/// orchestrator from EF-tracked entities so mutations flow back on SaveChanges.
/// </summary>
public sealed class PostingWorld
{
    private readonly Dictionary<string, CardXref> _xrefByCard;
    private readonly Dictionary<string, Account> _accountById;
    private readonly Dictionary<string, TransactionCategoryBalance> _catBal;

    public PostingWorld(
        IEnumerable<CardXref> xrefs,
        IEnumerable<Account> accounts,
        IEnumerable<TransactionCategoryBalance> categoryBalances)
    {
        _xrefByCard = xrefs.GroupBy(x => x.CardNumber).ToDictionary(g => g.Key, g => g.First());
        _accountById = accounts.ToDictionary(a => a.AccountId);
        _catBal = categoryBalances.ToDictionary(CatKey);
    }

    internal static string CatKey(TransactionCategoryBalance b) => CatKey(b.AccountId, b.TypeCode, b.CategoryCode);
    internal static string CatKey(string acct, string type, string cat) => $"{acct}|{type}|{cat}";

    public CardXref? FindXrefByCard(string cardNumber) =>
        _xrefByCard.TryGetValue(cardNumber, out var x) ? x : null;

    public Account? FindAccount(string accountId) =>
        _accountById.TryGetValue(accountId, out var a) ? a : null;

    public TransactionCategoryBalance? FindCategoryBalance(string acct, string type, string cat) =>
        _catBal.TryGetValue(CatKey(acct, type, cat), out var b) ? b : null;

    public void AddCategoryBalance(TransactionCategoryBalance b) => _catBal[CatKey(b)] = b;
}

/// <summary>
/// Reproduces CBTRN02C daily-transaction posting (1500-VALIDATE-TRAN,
/// 2000-POST-TRANSACTION, 2700/2800 balance mutations). Pure and deterministic:
/// it mutates only the supplied <see cref="PostingWorld"/> entities and returns
/// the accepted/rejected split. Records are processed in input order because the
/// legacy program posts sequentially and each record sees prior cycle updates.
/// </summary>
public sealed class PostingEngine
{
    /// <param name="dailyTransactions">Parsed daily transactions in file order.</param>
    /// <param name="world">Reference/master data to read and mutate.</param>
    /// <param name="processTimestamp">26-char processing timestamp (TRAN-PROC-TS).</param>
    public PostingOutcome Post(IEnumerable<Transaction> dailyTransactions, PostingWorld world, string processTimestamp)
    {
        ArgumentNullException.ThrowIfNull(dailyTransactions);
        ArgumentNullException.ThrowIfNull(world);
        var outcome = new PostingOutcome();

        foreach (var dt in dailyTransactions)
        {
            var reason = Validate(dt, world, out var account, out var accountId);

            if (reason != 0 || account is null)
            {
                outcome.Rejects.Add(new PostingReject(dt, reason == 0 ? PostingRejectReason.AccountNotFound : reason));
                continue;
            }

            // 2700-UPDATE-TCATBAL: create or accumulate the category balance.
            var catBal = world.FindCategoryBalance(accountId, dt.TypeCode, dt.CategoryCode);
            if (catBal is null)
            {
                catBal = new TransactionCategoryBalance
                {
                    AccountId = accountId,
                    TypeCode = dt.TypeCode,
                    CategoryCode = dt.CategoryCode,
                    Balance = dt.Amount,
                };
                world.AddCategoryBalance(catBal);
                outcome.CreatedCategoryBalances.Add(catBal);
            }
            else
            {
                catBal.Balance += dt.Amount;
            }

            // 2800-UPDATE-ACCOUNT-REC: balance and cycle accumulators.
            account.CurrentBalance += dt.Amount;
            if (dt.Amount >= 0)
                account.CurrentCycleCredit += dt.Amount;
            else
                account.CurrentCycleDebit += dt.Amount;

            // 2900-WRITE-TRANSACTION-FILE: the posted transaction record.
            outcome.Posted.Add(new Transaction
            {
                TransactionId = dt.TransactionId,
                TypeCode = dt.TypeCode,
                CategoryCode = dt.CategoryCode,
                Source = dt.Source,
                Description = dt.Description,
                Amount = dt.Amount,
                MerchantId = dt.MerchantId,
                MerchantName = dt.MerchantName,
                MerchantCity = dt.MerchantCity,
                MerchantZip = dt.MerchantZip,
                CardNumber = dt.CardNumber,
                OriginTimestamp = dt.OriginTimestamp,
                ProcessTimestamp = processTimestamp,
            });
        }

        return outcome;
    }

    private static int Validate(Transaction dt, PostingWorld world, out Account? account, out string accountId)
    {
        account = null;
        accountId = string.Empty;

        // 1500-A-LOOKUP-XREF
        var xref = world.FindXrefByCard(dt.CardNumber);
        if (xref is null)
            return PostingRejectReason.InvalidCardNumber; // 100

        // 1500-B-LOOKUP-ACCT
        account = world.FindAccount(xref.AccountId);
        if (account is null)
            return PostingRejectReason.AccountNotFound; // 101

        accountId = xref.AccountId;

        int reason = 0;

        // WS-TEMP-BAL = ACCT-CURR-CYC-CREDIT - ACCT-CURR-CYC-DEBIT + DALYTRAN-AMT
        var tempBal = account.CurrentCycleCredit - account.CurrentCycleDebit + dt.Amount;
        if (account.CreditLimit < tempBal)
            reason = PostingRejectReason.Overlimit; // 102

        // ACCT-EXPIRAION-DATE >= DALYTRAN-ORIG-TS(1:10). Text dates compare ordinally.
        var tranDate = dt.OriginTimestamp.Length >= 10 ? dt.OriginTimestamp[..10] : dt.OriginTimestamp;
        if (string.CompareOrdinal(account.ExpirationDate, tranDate) < 0)
            reason = PostingRejectReason.AfterExpiration; // 103 (overwrites 102, matching COBOL)

        return reason;
    }
}
