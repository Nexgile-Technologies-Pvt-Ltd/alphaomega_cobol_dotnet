using CardDemo.Application.Abstractions;
using CardDemo.Application.Dtos;
using CardDemo.Application.Services;
using CardDemo.Domain.Entities;
using CardDemo.Domain.Services;
using CardDemo.Infrastructure.Batch;
using CardDemo.Infrastructure.Fixtures;
using CardDemo.Infrastructure.Optional;
using CardDemo.Infrastructure.Persistence;
using CardDemo.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CardDemo.Tests;

/// <summary>
/// Closes the completeness-audit gaps with deterministic, seeded coverage: the posting
/// after-expiration reject (103) and its precedence over over-limit (102); the interest
/// DEFAULT-group rate fallback and the golden interest oracle; the statement golden;
/// optimistic-concurrency guards; the transaction-add validation cascade; the COACTUPC
/// customer validators; the COCRDUPC card validators; user-admin update and last-admin
/// delete guard; combine-transactions reconciliation; and the authorization Unload/Load
/// round-trip. Every database test seeds a fresh in-memory SQLite database via
/// <see cref="DatabaseManager"/> and drives time through a <see cref="FixedTimeProvider"/>.
/// </summary>
public sealed class GapClosureTests
{
    // Seeded card -> account 00000000050 (customer 000000050) via cardxref.txt.
    private const string SeededCard = "0500024453765740";
    private const string SeededAccount = "00000000050";

    private static readonly TimeProvider Clock =
        new FixedTimeProvider(new DateTimeOffset(2024, 6, 11, 12, 0, 0, TimeSpan.Zero));

    private static async Task<(CardDemoDbContext Db, ICardDemoStore Store, IPasswordHasher Hasher)> SeedAsync(SqliteTestDatabase testDb)
    {
        var db = testDb.NewContext();
        var hasher = new Pbkdf2PasswordHasher();
        var manager = new DatabaseManager(db, new FixtureSeeder(db, hasher));
        await manager.InitializeAsync(FixturePaths.AsciiRoot(), reseed: false, CancellationToken.None);
        return (db, new CardDemoStore(db), hasher);
    }

    private static async Task PostFixtureAsync(SqliteTestDatabase testDb)
    {
        var hasher = new Pbkdf2PasswordHasher();
        await using var db = testDb.NewContext();
        var runner = new BatchRunner(db, new FixtureSeeder(db, hasher), Clock);
        var rejects = TempPath("rejects");
        await runner.PostTransactionsAsync(FixturePaths.DailyTran(), rejects, CancellationToken.None);
    }

    private static string TempPath(string prefix, string ext = "txt")
    {
        var dir = Path.Combine(Path.GetTempPath(), "carddemo-gapclosure-tests");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{prefix}-{Guid.NewGuid():N}.{ext}");
    }

    // ==================== PostingEngine: reject 103 + 102-then-103 precedence ====================

    private static Transaction Daily(string card, decimal amount, string originTs) =>
        new()
        {
            TransactionId = "0000000000000001",
            TypeCode = "01",
            CategoryCode = "0001",
            CardNumber = card,
            Amount = amount,
            OriginTimestamp = originTs,
        };

    private static (PostingWorld World, Account Account) BuildPostingWorld(decimal creditLimit, string expirationDate)
    {
        var account = new Account
        {
            AccountId = "00000000001",
            CreditLimit = creditLimit,
            CurrentBalance = 0m,
            CurrentCycleCredit = 0m,
            CurrentCycleDebit = 0m,
            ExpirationDate = expirationDate,
        };
        var xref = new CardXref { CardNumber = "1111222233334444", CustomerId = "000000001", AccountId = "00000000001" };
        return (new PostingWorld([xref], [account], []), account);
    }

    [Fact]
    public void Post_AfterExpiration_Rejects103_AndDoesNotMutateAccount()
    {
        // ACCT-EXPIRAION-DATE (2020-01-01) is ordinally < tran origin date (2024-06-10) => 103.
        var (world, account) = BuildPostingWorld(creditLimit: 1_000_000m, expirationDate: "2020-01-01");
        var outcome = new PostingEngine().Post(
            [Daily("1111222233334444", 10m, "2024-06-10 12:00:00.000000")], world, "2024-06-11-00.00.00.000000");

        Assert.Equal(0, outcome.AcceptedCount);
        var reject = Assert.Single(outcome.Rejects);
        Assert.Equal(PostingRejectReason.AfterExpiration, reject.ReasonCode);
        Assert.Equal(103, reject.ReasonCode);
        Assert.Equal(0m, account.CurrentBalance);
    }

    [Fact]
    public void Post_OverLimit_And_AfterExpiration_FinalReasonIs103()
    {
        // Both conditions hold: amount 500 > limit 100 (102) AND expired (103). CBTRN02C sets
        // 102 then overwrites with 103, so the final recorded reason is 103.
        var (world, account) = BuildPostingWorld(creditLimit: 100m, expirationDate: "2020-01-01");
        var outcome = new PostingEngine().Post(
            [Daily("1111222233334444", 500m, "2024-06-10 12:00:00.000000")], world, "2024-06-11-00.00.00.000000");

        Assert.Equal(0, outcome.AcceptedCount);
        var reject = Assert.Single(outcome.Rejects);
        Assert.Equal(PostingRejectReason.AfterExpiration, reject.ReasonCode);
        Assert.Equal(0m, account.CurrentBalance);
    }

    // ==================== InterestEngine: DEFAULT-group fallback ====================

    [Fact]
    public void Interest_UnknownGroup_FallsBackToDefaultRate()
    {
        // The account's group ("PLATINUM") has no disclosure row; only a DEFAULT row exists.
        // 1200-GET-INTEREST-RATE falls back to the DEFAULT group, so interest is priced at 12%.
        var account = new Account
        {
            AccountId = "00000000001",
            GroupId = "PLATINUM",
            CurrentBalance = 0m,
            CurrentCycleCredit = 5m,
            CurrentCycleDebit = 3m,
        };
        var disc = new DisclosureGroup { GroupId = "DEFAULT", TypeCode = "01", CategoryCode = "0005", InterestRate = 12m };
        var xref = new CardXref { CardNumber = "1111222233334444", CustomerId = "000000001", AccountId = "00000000001" };
        var world = new InterestWorld([account], [disc], [xref]);

        var balances = new[]
        {
            new TransactionCategoryBalance { AccountId = "00000000001", TypeCode = "01", CategoryCode = "0005", Balance = 1000m },
        };

        var outcome = new InterestEngine().Run(balances, world, "0000000001", "2024-06-11-00.00.00.000000");

        // truncate2(1000 * 12 / 1200) = 10.00, priced via the DEFAULT fallback rate.
        var tran = Assert.Single(outcome.InterestTransactions);
        Assert.Equal(10.00m, tran.Amount);
        Assert.Equal(10.00m, outcome.TotalInterest);
    }

    // ==================== Interest golden oracle (fixture) ====================

    [Fact]
    public async Task Interest_Golden_AfterPost_50Transactions_Total1279_16()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, _, hasher) = await SeedAsync(testDb);
        await using var _d = db;
        await PostFixtureAsync(testDb);

        await using var c = testDb.NewContext();
        var report = await new BatchRunner(c, new FixtureSeeder(c, hasher), Clock)
            .CalculateInterestAsync("0000000001", CancellationToken.None);

        Assert.Equal(50, report.InterestTransactions);
        Assert.Equal(1279.16m, report.TotalInterest);
    }

    // ==================== Statement golden (fixture) ====================

    [Fact]
    public async Task Statement_Golden_AfterPost_50Statements_262Lines()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, _, _) = await SeedAsync(testDb);
        await using var _d = db;
        await PostFixtureAsync(testDb);

        await using var c = testDb.NewContext();
        var result = await new StatementService(c).GenerateAsync(TempPath("stmt", "txt"), TempPath("stmt", "html"), CancellationToken.None);

        Assert.Equal(50, result.Statements);
        Assert.Equal(262, result.TransactionLines);
    }

    // ==================== Concurrency: stale RowVersion ====================

    private static AccountUpdateRequest ValidAccountUpdate(Account a, Customer cust, long rowVersion, decimal creditLimit) =>
        new(
            AccountId: a.AccountId,
            ActiveStatus: a.ActiveStatus,
            CreditLimit: creditLimit,
            CashCreditLimit: a.CashCreditLimit,
            ExpirationDate: a.ExpirationDate,
            ReissueDate: a.ReissueDate,
            GroupId: a.GroupId,
            FirstName: string.IsNullOrWhiteSpace(cust.FirstName) ? "Jane" : cust.FirstName,
            MiddleName: cust.MiddleName,
            LastName: string.IsNullOrWhiteSpace(cust.LastName) ? "Doe" : cust.LastName,
            AddressLine1: cust.AddressLine1,
            AddressLine2: cust.AddressLine2,
            AddressLine3: cust.AddressLine3,
            StateCode: "NY",
            CountryCode: cust.CountryCode,
            Zip: "10001",
            PhoneNumber1: "(212)555-1212",
            PhoneNumber2: "(212)555-3434",
            FicoCreditScore: 700,
            Ssn: "123456789",
            DateOfBirth: "1990-01-01",
            AccountRowVersion: rowVersion);

    [Fact]
    public async Task AccountUpdate_StaleRowVersion_Fails_AndDoesNotPersist()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, _) = await SeedAsync(testDb);
        await using var _d = db;
        var accounts = new AccountService(store, Clock);

        var view = await accounts.ViewAsync(SeededAccount, CancellationToken.None);
        Assert.True(view.Success, view.Message);
        var a = view.Value!.Account;
        var before = a.CreditLimit;

        // Present a row version that no longer matches (someone else changed the row).
        var request = ValidAccountUpdate(a, view.Value.Customer, a.RowVersion + 999, creditLimit: 424242.00m);

        var update = await accounts.UpdateAsync(request, CancellationToken.None);

        Assert.False(update.Success);
        Assert.Contains("Record changed", update.Message, StringComparison.OrdinalIgnoreCase);

        var after = await db.Accounts.AsNoTracking().SingleAsync(x => x.AccountId == SeededAccount);
        Assert.Equal(before, after.CreditLimit);
    }

    [Fact]
    public async Task CardUpdate_StaleRowVersion_Fails_AndDoesNotPersist()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, _) = await SeedAsync(testDb);
        await using var _d = db;
        var cards = new CardService(store);

        var view = await cards.ViewAsync(SeededAccount, SeededCard, CancellationToken.None);
        Assert.True(view.Success, view.Message);
        var c = view.Value!.Card;
        var beforeName = c.EmbossedName;

        var request = new CardUpdateRequest(
            CardNumber: c.CardNumber,
            EmbossedName: "STALE NAME",
            ActiveStatus: "N",
            ExpirationMonth: 12,
            ExpirationYear: 2030,
            CardRowVersion: c.RowVersion + 999);

        var update = await cards.UpdateAsync(request, CancellationToken.None);

        Assert.False(update.Success);
        Assert.Contains("Record changed", update.Message, StringComparison.OrdinalIgnoreCase);

        var after = await db.Cards.AsNoTracking().SingleAsync(x => x.CardNumber == SeededCard);
        Assert.Equal(beforeName, after.EmbossedName);
    }

    // ==================== TransactionService.AddAsync validation cascade ====================

    private static TransactionAddRequest TxnRequest(
        string account = SeededAccount,
        string card = "",
        string type = "01",
        string category = "0001",
        string source = "POS",
        string description = "Test",
        string merchantId = "000000001",
        string origDate = "2022-06-01") =>
        new(
            AccountId: account,
            CardNumber: card,
            TypeCode: type,
            CategoryCode: category,
            Source: source,
            Description: description,
            Amount: 10.00m,
            MerchantId: merchantId,
            MerchantName: "M",
            MerchantCity: "C",
            MerchantZip: "12345",
            OriginDate: origDate);

    [Fact]
    public async Task TransactionAdd_BlankAccountAndCard_Fails_KeyMessage()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, _) = await SeedAsync(testDb);
        await using var _d = db;
        var txns = new TransactionService(store, Clock);

        var result = await txns.AddAsync(TxnRequest(account: "", card: ""), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Account or Card Number must be entered", result.Message);
    }

    [Fact]
    public async Task TransactionAdd_NonNumericMerchantId_Fails()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, _) = await SeedAsync(testDb);
        await using var _d = db;
        var txns = new TransactionService(store, Clock);

        var result = await txns.AddAsync(TxnRequest(merchantId: "12A456789"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Merchant ID must be Numeric", result.Message);
    }

    [Fact]
    public async Task TransactionAdd_BlankType_Fails_BeforeCategory()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, _) = await SeedAsync(testDb);
        await using var _d = db;
        var txns = new TransactionService(store, Clock);

        // Type blank AND category blank: first-failure order reports the Type message.
        var result = await txns.AddAsync(TxnRequest(type: "", category: ""), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Type CD can NOT be empty", result.Message);
    }

    [Fact]
    public async Task TransactionAdd_BlankCategory_Fails()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, _) = await SeedAsync(testDb);
        await using var _d = db;
        var txns = new TransactionService(store, Clock);

        var result = await txns.AddAsync(TxnRequest(category: ""), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Category CD can NOT be empty", result.Message);
    }

    [Fact]
    public async Task TransactionAdd_InvalidIsoOriginDate_Fails()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, _) = await SeedAsync(testDb);
        await using var _d = db;
        var txns = new TransactionService(store, Clock);

        // 2022-13-40 is not a real calendar date.
        var result = await txns.AddAsync(TxnRequest(origDate: "2022-13-40"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Orig Date - Not a valid date", result.Message);
    }

    // ==================== Account customer validation (COACTUPC) ====================

    private static async Task<(AccountService Accounts, Account Account, Customer Customer)> AccountCtxAsync(SqliteTestDatabase testDb, ICardDemoStore store)
    {
        var accounts = new AccountService(store, Clock);
        var view = await accounts.ViewAsync(SeededAccount, CancellationToken.None);
        Assert.True(view.Success, view.Message);
        return (accounts, view.Value!.Account, view.Value.Customer);
    }

    [Fact]
    public async Task AccountUpdate_InvalidSsn_Fails()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, _) = await SeedAsync(testDb);
        await using var _d = db;
        var (accounts, a, cust) = await AccountCtxAsync(testDb, store);

        // SSN area "000" is invalid.
        var request = ValidAccountUpdate(a, cust, a.RowVersion, 5000m) with { Ssn = "000456789" };
        var result = await accounts.UpdateAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("SSN", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AccountUpdate_FutureDateOfBirth_Fails()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, _) = await SeedAsync(testDb);
        await using var _d = db;
        var (accounts, a, cust) = await AccountCtxAsync(testDb, store);

        // Fixed clock is 2024-06-11; a 2999 DOB is in the future.
        var request = ValidAccountUpdate(a, cust, a.RowVersion, 5000m) with { DateOfBirth = "2999-01-01" };
        var result = await accounts.UpdateAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Date of Birth", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AccountUpdate_UnknownState_Fails()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, _) = await SeedAsync(testDb);
        await using var _d = db;
        var (accounts, a, cust) = await AccountCtxAsync(testDb, store);

        // "ZZ" is not a US state code.
        var request = ValidAccountUpdate(a, cust, a.RowVersion, 5000m) with { StateCode = "ZZ", Zip = "10001" };
        var result = await accounts.UpdateAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("State", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AccountUpdate_InconsistentStateZip_Fails()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, _) = await SeedAsync(testDb);
        await using var _d = db;
        var (accounts, a, cust) = await AccountCtxAsync(testDb, store);

        // NY with a ZIP prefix "99" is not a known NY state/zip combination.
        var request = ValidAccountUpdate(a, cust, a.RowVersion, 5000m) with { StateCode = "NY", Zip = "99999" };
        var result = await accounts.UpdateAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("zip", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AccountUpdate_BadPhoneAreaCode_Fails()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, _) = await SeedAsync(testDb);
        await using var _d = db;
        var (accounts, a, cust) = await AccountCtxAsync(testDb, store);

        // Area code 000 is not a valid North America general-purpose area code.
        var request = ValidAccountUpdate(a, cust, a.RowVersion, 5000m) with { PhoneNumber1 = "(000)555-1212" };
        var result = await accounts.UpdateAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Phone", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AccountUpdate_FullyValid_Persists_SsnAndDob()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, _) = await SeedAsync(testDb);
        await using var _d = db;
        var (accounts, a, cust) = await AccountCtxAsync(testDb, store);

        var request = ValidAccountUpdate(a, cust, a.RowVersion, creditLimit: 8888.00m);
        var result = await accounts.UpdateAsync(request, CancellationToken.None);

        Assert.True(result.Success, result.Message);

        var afterAccount = await db.Accounts.AsNoTracking().SingleAsync(x => x.AccountId == SeededAccount);
        Assert.Equal(8888.00m, afterAccount.CreditLimit);

        var xref = await db.CardXrefs.AsNoTracking().FirstAsync(x => x.AccountId == SeededAccount);
        var afterCustomer = await db.Customers.AsNoTracking().SingleAsync(x => x.CustomerId == xref.CustomerId);
        Assert.Equal("123456789", afterCustomer.Ssn);
        Assert.Equal("1990-01-01", afterCustomer.DateOfBirth);
    }

    // ==================== Card validation (COCRDUPC) ====================

    private static async Task<(CardService Cards, Card Card)> CardCtxAsync(SqliteTestDatabase testDb, ICardDemoStore store)
    {
        var cards = new CardService(store);
        var view = await cards.ViewAsync(SeededAccount, SeededCard, CancellationToken.None);
        Assert.True(view.Success, view.Message);
        return (cards, view.Value!.Card);
    }

    [Fact]
    public async Task CardUpdate_EmbossedNameWithDigits_Fails()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, _) = await SeedAsync(testDb);
        await using var _d = db;
        var (cards, c) = await CardCtxAsync(testDb, store);

        var request = new CardUpdateRequest(c.CardNumber, "JOHN 123", "Y", 12, 2030, c.RowVersion);
        var result = await cards.UpdateAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Card name can only contain letters and spaces", result.Message);
    }

    [Fact]
    public async Task CardUpdate_InvalidMonth_Fails()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, _) = await SeedAsync(testDb);
        await using var _d = db;
        var (cards, c) = await CardCtxAsync(testDb, store);

        var request = new CardUpdateRequest(c.CardNumber, "JOHN DOE", "Y", 13, 2030, c.RowVersion);
        var result = await cards.UpdateAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("month", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CardUpdate_InvalidYear_Fails()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, _) = await SeedAsync(testDb);
        await using var _d = db;
        var (cards, c) = await CardCtxAsync(testDb, store);

        var request = new CardUpdateRequest(c.CardNumber, "JOHN DOE", "Y", 12, 1900, c.RowVersion);
        var result = await cards.UpdateAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("year", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ==================== UserAdminService: update + last-admin delete guard ====================

    [Fact]
    public async Task UserAdmin_Update_HappyPath_PersistsChanges()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, hasher) = await SeedAsync(testDb);
        await using var _d = db;
        var users = new UserAdminService(store, hasher);

        await users.AddAsync(new UserUpsertRequest("EDITME01", "Old", "Name", "pass1234", "U"), "ADMIN001", CancellationToken.None);

        var update = await users.UpdateAsync(new UserUpsertRequest("EDITME01", "New", "Person", "", "A"), "ADMIN001", CancellationToken.None);
        Assert.True(update.Success, update.Message);

        var after = await store.FindUserAsync("EDITME01", CancellationToken.None);
        Assert.NotNull(after);
        Assert.Equal("New", after!.FirstName);
        Assert.Equal("Person", after.LastName);
        Assert.Equal("A", after.UserType);
    }

    [Fact]
    public async Task UserAdmin_Update_MissingFirstName_Fails()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, hasher) = await SeedAsync(testDb);
        await using var _d = db;
        var users = new UserAdminService(store, hasher);

        var update = await users.UpdateAsync(new UserUpsertRequest("USER0001", "", "Person", "", "U"), "ADMIN001", CancellationToken.None);

        Assert.False(update.Success);
        Assert.Contains("First Name", update.Message);
    }

    [Fact]
    public async Task UserAdmin_NonAdminActor_CannotDeleteAdmin_IsBlocked()
    {
        // Security hole fixed (FR-AUTH-005 / FR-USER-008): a non-administrator acting user
        // may not delete an administrator. The service resolves the acting user first and
        // refuses the operation entirely. (Previously this path succeeded — the audit gap.)
        using var testDb = new SqliteTestDatabase();
        var (db, store, hasher) = await SeedAsync(testDb);
        await using var _d = db;
        var users = new UserAdminService(store, hasher);

        var admins = (await store.ListUsersAsync(0, 1000, CancellationToken.None))
            .Where(u => u.IsAdmin)
            .Select(u => u.UserId)
            .ToList();
        Assert.True(admins.Count >= 1, "fixture must seed at least one admin");

        var target = admins[0];
        var adminsBefore = await store.CountAdminsAsync(CancellationToken.None);

        // A non-admin acting user is rejected at the service boundary.
        const string nonAdminActor = "USER0001";
        var blocked = await users.DeleteAsync(target, nonAdminActor, CancellationToken.None);

        Assert.False(blocked.Success);
        Assert.Contains("Not authorized", blocked.Message, StringComparison.OrdinalIgnoreCase);

        // Nothing was deleted.
        Assert.NotNull(await store.FindUserAsync(target, CancellationToken.None));
        Assert.Equal(adminsBefore, await store.CountAdminsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task UserAdmin_LastAdminDelete_IsBlocked()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, hasher) = await SeedAsync(testDb);
        await using var _d = db;
        var users = new UserAdminService(store, hasher);

        // Delete every admin down to one, then the final admin delete must be blocked.
        var admins = (await store.ListUsersAsync(0, 1000, CancellationToken.None))
            .Where(u => u.IsAdmin)
            .Select(u => u.UserId)
            .ToList();
        Assert.True(admins.Count >= 2, "fixture must seed at least two admins");

        // Act as one admin (the last one) so the earlier admins can be thinned out without
        // tripping the self-delete guard; the acting user must itself be an administrator.
        var actor = admins[^1];
        for (var i = 0; i < admins.Count - 1; i++)
        {
            var del = await users.DeleteAsync(admins[i], actor, CancellationToken.None);
            Assert.True(del.Success, del.Message);
        }

        Assert.Equal(1, await store.CountAdminsAsync(CancellationToken.None));

        // The acting user is now the only remaining admin: deleting it is blocked by BOTH the
        // self-delete guard and the last-admin guard; either way the row survives.
        var blocked = await users.DeleteAsync(actor, actor, CancellationToken.None);

        Assert.False(blocked.Success);
        Assert.NotNull(await store.FindUserAsync(actor, CancellationToken.None));
        Assert.Equal(1, await store.CountAdminsAsync(CancellationToken.None));
    }

    // ==================== CombineTransactionsAsync reconciliation ====================

    [Fact]
    public async Task Combine_AfterPostAndInterest_Total312_262Posted_50Interest()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, _, hasher) = await SeedAsync(testDb);
        await using var _d = db;
        await PostFixtureAsync(testDb);

        await using (var c = testDb.NewContext())
            await new BatchRunner(c, new FixtureSeeder(c, hasher), Clock).CalculateInterestAsync("0000000001", CancellationToken.None);

        await using var cc = testDb.NewContext();
        var combine = await new BatchRunner(cc, new FixtureSeeder(cc, hasher), Clock).CombineTransactionsAsync(CancellationToken.None);

        Assert.Equal(312, combine.TotalTransactions);
        Assert.Equal(262, combine.PostedTransactions);
        Assert.Equal(50, combine.InterestTransactions);
    }

    // ==================== AuthorizationService Unload/Load round-trip ====================

    [Fact]
    public async Task Authorization_UnloadThenLoad_RoundTripsSummariesAndDetails()
    {
        using var sourceDb = new SqliteTestDatabase();
        await using var src = (await SeedAsync(sourceDb)).Db;

        var auth = new AuthorizationService(src, Clock);
        var account = await src.Accounts.AsNoTracking().SingleAsync(a => a.AccountId == SeededAccount);
        var unit = decimal.Round(account.CreditLimit / 5m, 2);

        // Two approvable requests -> one summary + two details for the account.
        await auth.SubmitAsync(new AuthorizationRequest { CardNumber = SeededCard, TransactionAmount = unit, AuthType = "01" }, CancellationToken.None);
        await auth.SubmitAsync(new AuthorizationRequest { CardNumber = SeededCard, TransactionAmount = unit, AuthType = "02" }, CancellationToken.None);
        await auth.ProcessPendingAsync(maxMessages: 10, CancellationToken.None);

        var expectedSummaries = await src.PendingAuthSummaries.AsNoTracking().CountAsync();
        var expectedDetails = await src.PendingAuthDetails.AsNoTracking().CountAsync();
        Assert.Equal(1, expectedSummaries);
        Assert.Equal(2, expectedDetails);

        var file = TempPath("pauth-unload", "dat");
        var written = await auth.UnloadAsync(file, CancellationToken.None);
        Assert.Equal(expectedSummaries + expectedDetails, written);

        // Load into an independent, freshly seeded database (no pending-auth rows yet).
        using var targetDb = new SqliteTestDatabase();
        await using var tgt = (await SeedAsync(targetDb)).Db;
        Assert.Equal(0, await tgt.PendingAuthSummaries.AsNoTracking().CountAsync());
        Assert.Equal(0, await tgt.PendingAuthDetails.AsNoTracking().CountAsync());

        var read = await new AuthorizationService(tgt, Clock).LoadAsync(file, CancellationToken.None);
        Assert.Equal(written, read);

        Assert.Equal(expectedSummaries, await tgt.PendingAuthSummaries.AsNoTracking().CountAsync());
        Assert.Equal(expectedDetails, await tgt.PendingAuthDetails.AsNoTracking().CountAsync());

        // A logical summary row round-trips its money fields exactly.
        var srcSummary = await src.PendingAuthSummaries.AsNoTracking().SingleAsync(s => s.AccountId == SeededAccount);
        var tgtSummary = await tgt.PendingAuthSummaries.AsNoTracking().SingleAsync(s => s.AccountId == SeededAccount);
        Assert.Equal(srcSummary.CreditBalance, tgtSummary.CreditBalance);
        Assert.Equal(srcSummary.ApprovedAuthCount, tgtSummary.ApprovedAuthCount);
        Assert.Equal(srcSummary.ApprovedAuthAmount, tgtSummary.ApprovedAuthAmount);
    }
}
