using CardDemo.Infrastructure.Fixtures;
using CardDemo.Infrastructure.Persistence;
using CardDemo.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CardDemo.Tests;

/// <summary>
/// Verifies that seeding inserts the exact field VALUES from the fixtures (not just
/// the right row counts) — signed overpunch money, text dates and identifiers with
/// preserved leading zeros. Values are taken from the first fixture records.
/// </summary>
public sealed class DataFidelityTests
{
    private static async Task<CardDemoDbContext> SeedAsync(SqliteTestDatabase testDb)
    {
        var db = testDb.NewContext();
        var seeder = new FixtureSeeder(db, new Pbkdf2PasswordHasher());
        var manager = new DatabaseManager(db, seeder);
        await manager.InitializeAsync(FixturePaths.AsciiRoot(), reseed: false, CancellationToken.None);
        return db;
    }

    [Fact]
    public async Task Account_00000000001_HasExactFixtureValues()
    {
        using var testDb = new SqliteTestDatabase();
        await using var db = await SeedAsync(testDb);

        var account = await db.Accounts.SingleAsync(a => a.AccountId == "00000000001");

        Assert.Equal("Y", account.ActiveStatus);
        Assert.Equal(194.00m, account.CurrentBalance);      // 00000001940{ overpunch => +194.00
        Assert.Equal(2020.00m, account.CreditLimit);        // 00000020200{ => +2020.00
        Assert.Equal(1020.00m, account.CashCreditLimit);    // 00000010200{ => +1020.00
        Assert.Equal("2014-11-20", account.OpenDate);
    }

    [Fact]
    public async Task Card_FirstRecord_HasExactFixtureValues()
    {
        using var testDb = new SqliteTestDatabase();
        await using var db = await SeedAsync(testDb);

        var card = await db.Cards.SingleAsync(c => c.CardNumber == "0500024453765740");

        Assert.Equal("00000000050", card.AccountId);
        Assert.Equal("747", card.Cvv);
        Assert.Equal("Aniya Von", card.EmbossedName.Trim());
        Assert.Equal("2023-03-09", card.ExpirationDate);
        Assert.Equal("Y", card.ActiveStatus);
    }

    [Fact]
    public async Task Xref_FirstRecord_LinksCardCustomerAccount()
    {
        using var testDb = new SqliteTestDatabase();
        await using var db = await SeedAsync(testDb);

        var xref = await db.CardXrefs.SingleAsync(x => x.CardNumber == "0500024453765740");

        Assert.Equal("000000050", xref.CustomerId);
        Assert.Equal("00000000050", xref.AccountId);
    }

    [Fact]
    public async Task ReferenceData_TypesAndCategories_HaveExpectedDescriptions()
    {
        using var testDb = new SqliteTestDatabase();
        await using var db = await SeedAsync(testDb);

        var purchase = await db.TransactionTypes.SingleAsync(t => t.TypeCode == "01");
        Assert.Equal("Purchase", purchase.Description.Trim());

        var payment = await db.TransactionTypes.SingleAsync(t => t.TypeCode == "02");
        Assert.Equal("Payment", payment.Description.Trim());

        var cat = await db.TransactionCategories.SingleAsync(c => c.TypeCode == "01" && c.CategoryCode == "0001");
        Assert.Equal("Regular Sales Draft", cat.Description.Trim());
    }

    [Fact]
    public async Task Customer_LeadingZeroIdentifiers_ArePreserved()
    {
        using var testDb = new SqliteTestDatabase();
        await using var db = await SeedAsync(testDb);

        // Customer IDs are 9-digit; leading zeros must survive as strings.
        var customer = await db.Customers.SingleAsync(c => c.CustomerId == "000000050");
        Assert.Equal(9, customer.CustomerId.Length);
        Assert.True(customer.FicoCreditScore is >= 300 and <= 850, "FICO within valid range");
    }
}
