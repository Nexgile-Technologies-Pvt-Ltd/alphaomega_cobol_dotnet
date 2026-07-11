using CardDemo.Application.Abstractions;
using CardDemo.Application.Dtos;
using CardDemo.Application.Services;
using CardDemo.Infrastructure.Fixtures;
using CardDemo.Infrastructure.Persistence;
using CardDemo.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CardDemo.Tests;

/// <summary>
/// Integration tests for the online application services — the exact business logic
/// the interactive terminal drives — against a real seeded SQLite database.
/// </summary>
public sealed class OnlineServicesTests
{
    private const string FirstCard = "0500024453765740";
    private const string FirstCardAccount = "00000000050";

    private static readonly TimeProvider Clock =
        new FixedTimeProvider(new DateTimeOffset(2024, 6, 11, 12, 0, 0, TimeSpan.Zero));

    private static async Task<(CardDemoDbContext Db, ICardDemoStore Store, IPasswordHasher Hasher)> SeedAsync(SqliteTestDatabase testDb)
    {
        var db = testDb.NewContext();
        var hasher = new Pbkdf2PasswordHasher();
        var seeder = new FixtureSeeder(db, hasher);
        var manager = new DatabaseManager(db, seeder);
        await manager.InitializeAsync(FixturePaths.AsciiRoot(), reseed: false, CancellationToken.None);
        return (db, new CardDemoStore(db), hasher);
    }

    // ---------- Sign-on (COSGN00C) ----------

    [Fact]
    public async Task SignIn_ValidAdmin_LowercaseId_Succeeds_AndIsAdmin()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, hasher) = await SeedAsync(testDb);
        await using var _ = db;
        var auth = new AuthService(store, hasher);

        var result = await auth.SignInAsync("admin001", "PASSWORD", CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Value);
        Assert.True(result.Value!.IsAdmin);
    }

    [Fact]
    public async Task SignIn_RegularUser_Succeeds_NotAdmin()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, hasher) = await SeedAsync(testDb);
        await using var _ = db;
        var auth = new AuthService(store, hasher);

        var result = await auth.SignInAsync("USER0001", "PASSWORD", CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.False(result.Value!.IsAdmin);
    }

    [Fact]
    public async Task SignIn_WrongPassword_Fails()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, hasher) = await SeedAsync(testDb);
        await using var _ = db;
        var auth = new AuthService(store, hasher);

        var result = await auth.SignInAsync("ADMIN001", "not-the-password", CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task SignIn_UnknownUser_Fails()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, hasher) = await SeedAsync(testDb);
        await using var _ = db;
        var auth = new AuthService(store, hasher);

        var result = await auth.SignInAsync("NOBODY99", "PASSWORD", CancellationToken.None);

        Assert.False(result.Success);
    }

    // ---------- Account view (COACTVWC) ----------

    [Fact]
    public async Task AccountView_ResolvesCustomerAndCards()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, _) = await SeedAsync(testDb);
        await using var _d = db;
        var accounts = new AccountService(store, Clock);

        var result = await accounts.ViewAsync(FirstCardAccount, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Equal(FirstCardAccount, result.Value!.Account.AccountId);
        Assert.NotNull(result.Value.Customer);
        Assert.NotEmpty(result.Value.Cards);
    }

    // ---------- Card list/view (COCRDLIC/COCRDSLC) ----------

    [Fact]
    public async Task CardList_FirstPage_HasNext()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, _) = await SeedAsync(testDb);
        await using var _d = db;
        var cards = new CardService(store);

        var result = await cards.ListAsync(null, null, page: 1, pageSize: 10, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(10, result.Value!.Items.Count);
        Assert.True(result.Value.HasNext);
    }

    [Fact]
    public async Task CardView_ReturnsCardWithAccount()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, _) = await SeedAsync(testDb);
        await using var _d = db;
        var cards = new CardService(store);

        var result = await cards.ViewAsync(FirstCardAccount, FirstCard, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Equal(FirstCard, result.Value!.Card.CardNumber);
        Assert.NotNull(result.Value.Account);
    }

    // ---------- Account update (COACTUPC) ----------

    [Fact]
    public async Task AccountUpdate_PersistsCreditLimitChange()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, _) = await SeedAsync(testDb);
        await using var _d = db;
        var accounts = new AccountService(store, Clock);

        var view = await accounts.ViewAsync(FirstCardAccount, CancellationToken.None);
        Assert.True(view.Success, view.Message);
        var a = view.Value!.Account;
        var cust = view.Value.Customer;

        // Supply values that satisfy the COACTUPC customer validators (SSN/DOB/state/zip/phone).
        var request = new AccountUpdateRequest(
            AccountId: a.AccountId,
            ActiveStatus: a.ActiveStatus,
            CreditLimit: 9999.00m,
            CashCreditLimit: a.CashCreditLimit,
            ExpirationDate: a.ExpirationDate,
            ReissueDate: a.ReissueDate,
            GroupId: a.GroupId,
            FirstName: cust.FirstName,
            MiddleName: cust.MiddleName,
            LastName: cust.LastName,
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
            AccountRowVersion: a.RowVersion);

        var update = await accounts.UpdateAsync(request, CancellationToken.None);
        Assert.True(update.Success, update.Message);

        var after = await db.Accounts.AsNoTracking().SingleAsync(x => x.AccountId == FirstCardAccount);
        Assert.Equal(9999.00m, after.CreditLimit);
    }

    // ---------- Card update (COCRDUPC) ----------

    [Fact]
    public async Task CardUpdate_PersistsNameAndStatusChange()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, _) = await SeedAsync(testDb);
        await using var _d = db;
        var cards = new CardService(store);

        var view = await cards.ViewAsync(FirstCardAccount, FirstCard, CancellationToken.None);
        Assert.True(view.Success, view.Message);
        var c = view.Value!.Card;

        var request = new CardUpdateRequest(
            CardNumber: c.CardNumber,
            EmbossedName: "UPDATED NAME",
            ActiveStatus: "N",
            ExpirationMonth: 12,
            ExpirationYear: 2030,
            CardRowVersion: c.RowVersion);

        var update = await cards.UpdateAsync(request, CancellationToken.None);
        Assert.True(update.Success, update.Message);

        var after = await db.Cards.AsNoTracking().SingleAsync(x => x.CardNumber == FirstCard);
        Assert.Equal("UPDATED NAME", after.EmbossedName.Trim());
        Assert.Equal("N", after.ActiveStatus);
    }

    // ---------- Transaction add/list (COTRN02C/COTRN00C) ----------

    [Fact]
    public async Task TransactionAdd_AllocatesSixteenCharId_AndListsIt()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, _) = await SeedAsync(testDb);
        await using var _d = db;
        var txns = new TransactionService(store, Clock);

        var request = new TransactionAddRequest(
            AccountId: FirstCardAccount,
            CardNumber: FirstCard,
            TypeCode: "01",
            CategoryCode: "0001",
            Source: "POS",
            Description: "Integration test purchase",
            Amount: 12.34m,
            MerchantId: "000000001",
            MerchantName: "Test Merchant",
            MerchantCity: "Test City",
            MerchantZip: "12345",
            OriginDate: "2022-06-01");

        var add = await txns.AddAsync(request, CancellationToken.None);

        Assert.True(add.Success, add.Message);
        Assert.Equal(16, add.Value!.TransactionId.Length);
        Assert.Equal(12.34m, add.Value.Amount);

        var list = await txns.ListAsync(page: 1, pageSize: 10, CancellationToken.None);
        Assert.True(list.Success);
        Assert.Single(list.Value!.Items);
    }

    // ---------- Bill payment (COBIL00C) ----------

    [Fact]
    public async Task BillPay_PaysFullBalance_AndZeroesAccount()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, _) = await SeedAsync(testDb);
        await using var _d = db;
        var billpay = new BillPayService(store, Clock);

        var before = await store.FindAccountAsync("00000000001", CancellationToken.None);
        Assert.Equal(194.00m, before!.CurrentBalance);

        var result = await billpay.PayFullBalanceAsync("00000000001", CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Value);

        var after = await db.Accounts.AsNoTracking().SingleAsync(a => a.AccountId == "00000000001");
        Assert.Equal(0m, after.CurrentBalance);
    }

    // ---------- User administration (COUSR0*) ----------

    [Fact]
    public async Task UserAdmin_Add_List_Get_Delete_Roundtrip()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, hasher) = await SeedAsync(testDb);
        await using var _d = db;
        var users = new UserAdminService(store, hasher);

        var add = await users.AddAsync(new UserUpsertRequest("TESTUSER", "Test", "User", "pass1234", "U"), "ADMIN001", CancellationToken.None);
        Assert.True(add.Success, add.Message);

        var get = await users.GetAsync("TESTUSER", CancellationToken.None);
        Assert.True(get.Success);
        Assert.Equal("Test", get.Value!.FirstName);

        var list = await users.ListAsync(page: 1, pageSize: 50, CancellationToken.None);
        Assert.Contains(list.Value!.Items, u => u.UserId == "TESTUSER");

        var del = await users.DeleteAsync("TESTUSER", "ADMIN001", CancellationToken.None);
        Assert.True(del.Success, del.Message);
    }

    [Fact]
    public async Task UserAdmin_SelfDelete_IsBlocked()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, hasher) = await SeedAsync(testDb);
        await using var _d = db;
        var users = new UserAdminService(store, hasher);

        var del = await users.DeleteAsync("ADMIN001", "ADMIN001", CancellationToken.None);

        Assert.False(del.Success);
    }

    // ---------- Report request (CORPT00C) ----------

    [Fact]
    public async Task ReportRequest_Valid_PersistsPendingRequest()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, _) = await SeedAsync(testDb);
        await using var _d = db;
        var reports = new ReportRequestService(store, Clock);

        var result = await reports.RequestAsync("2022-01-01", "2022-12-31", "ADMIN001", CancellationToken.None);

        Assert.True(result.Success, result.Message);
        var pending = await db.PendingReportRequests.AsNoTracking().SingleAsync();
        Assert.Equal("PENDING", pending.Status);
        Assert.Equal("2022-01-01", pending.FromDate);
        Assert.Equal("2022-12-31", pending.ToDate);
    }

    [Fact]
    public async Task ReportRequest_InvertedRange_Fails()
    {
        using var testDb = new SqliteTestDatabase();
        var (db, store, _) = await SeedAsync(testDb);
        await using var _d = db;
        var reports = new ReportRequestService(store, Clock);

        var result = await reports.RequestAsync("2022-12-31", "2022-01-01", "ADMIN001", CancellationToken.None);

        Assert.False(result.Success);
    }
}
