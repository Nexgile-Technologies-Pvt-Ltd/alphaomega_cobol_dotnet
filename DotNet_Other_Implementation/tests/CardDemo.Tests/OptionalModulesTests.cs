using CardDemo.Domain.Entities;
using CardDemo.Infrastructure.Batch;
using CardDemo.Infrastructure.Fixtures;
using CardDemo.Infrastructure.Optional;
using CardDemo.Infrastructure.Persistence;
using CardDemo.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CardDemo.Tests;

/// <summary>
/// Integration tests for the OPTIONAL modules (transaction-type maintenance, statements,
/// branch export/import, authorization and inquiry) against a real seeded SQLite database.
/// Every test seeds a fresh in-memory database via <see cref="DatabaseManager"/>, uses a
/// <see cref="FixedTimeProvider"/> for determinism, and constructs each service directly
/// with <c>new &lt;Service&gt;(context, timeProvider)</c> — the same shape the DI wiring uses.
/// </summary>
public sealed class OptionalModulesTests
{
    // Seeded card that resolves to account 00000000050 (customer 000000050) via cardxref.txt.
    private const string SeededCard = "0500024453765740";
    private const string SeededAccount = "00000000050";

    private static readonly TimeProvider Clock =
        new FixedTimeProvider(new DateTimeOffset(2024, 6, 11, 12, 0, 0, TimeSpan.Zero));

    private static async Task<CardDemoDbContext> SeedAsync(SqliteTestDatabase testDb)
    {
        var db = testDb.NewContext();
        var seeder = new FixtureSeeder(db, new Pbkdf2PasswordHasher());
        var manager = new DatabaseManager(db, seeder);
        await manager.InitializeAsync(FixturePaths.AsciiRoot(), reseed: false, CancellationToken.None);
        return db;
    }

    private static async Task PostFixtureTransactionsAsync(SqliteTestDatabase testDb)
    {
        var hasher = new Pbkdf2PasswordHasher();
        var rejects = TempPath("rejects");
        await using var db = testDb.NewContext();
        var runner = new BatchRunner(db, new FixtureSeeder(db, hasher), Clock);
        await runner.PostTransactionsAsync(FixturePaths.DailyTran(), rejects, CancellationToken.None);
    }

    private static string TempPath(string prefix, string ext = "txt")
    {
        var dir = Path.Combine(Path.GetTempPath(), "carddemo-optional-tests");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{prefix}-{Guid.NewGuid():N}.{ext}");
    }

    // ==================== TransactionTypeService (CTLI / CTTU) ====================

    [Fact]
    public async Task TransactionType_ListTypes_FirstPage_ReportsHasNext()
    {
        using var testDb = new SqliteTestDatabase();
        await using var db = await SeedAsync(testDb);
        var svc = new TransactionTypeService(db);

        // 7 seeded types; a page size of 5 must report there is a next page.
        var page1 = await svc.ListTypesAsync(page: 0, pageSize: 5, CancellationToken.None);

        Assert.True(page1.Success, page1.Message);
        Assert.Equal(5, page1.Value!.Items.Count);
        Assert.True(page1.Value.HasNext);
        Assert.False(page1.Value.HasPrevious);

        var page2 = await svc.ListTypesAsync(page: 1, pageSize: 5, CancellationToken.None);
        Assert.True(page2.Success);
        Assert.Equal(2, page2.Value!.Items.Count);
        Assert.False(page2.Value.HasNext);
        Assert.True(page2.Value.HasPrevious);
    }

    [Fact]
    public async Task TransactionType_Upsert_ZeroPadsNumericKey_ThenGetReturnsIt()
    {
        using var testDb = new SqliteTestDatabase();
        await using var db = await SeedAsync(testDb);
        var svc = new TransactionTypeService(db);

        // "8" must persist zero-padded to "08" (COTRTUPC 1210/1245).
        var upsert = await svc.UpsertTypeAsync("8", "PROMO ADJUSTMENT", CancellationToken.None);
        Assert.True(upsert.Success, upsert.Message);

        var get = await svc.GetTypeAsync("8", CancellationToken.None);
        Assert.True(get.Success, get.Message);
        Assert.Equal("08", get.Value!.TypeCode);
        Assert.Equal("PROMO ADJUSTMENT", get.Value.Description);

        // Update the same key to a new description.
        var update = await svc.UpsertTypeAsync("08", "PROMO CREDIT", CancellationToken.None);
        Assert.True(update.Success, update.Message);
        var after = await svc.GetTypeAsync("08", CancellationToken.None);
        Assert.Equal("PROMO CREDIT", after.Value!.Description);
    }

    [Fact]
    public async Task TransactionType_Get_Missing_Fails()
    {
        using var testDb = new SqliteTestDatabase();
        await using var db = await SeedAsync(testDb);
        var svc = new TransactionTypeService(db);

        var get = await svc.GetTypeAsync("99", CancellationToken.None);

        Assert.False(get.Success);
        Assert.Null(get.Value);
    }

    [Fact]
    public async Task TransactionType_Delete_IsRestricted_WhileCategoriesReferenceIt()
    {
        using var testDb = new SqliteTestDatabase();
        await using var db = await SeedAsync(testDb);
        var svc = new TransactionTypeService(db);

        // A type that has seeded categories cannot be deleted (FK RESTRICT / SQLCODE -532).
        var typeWithChildren = await db.TransactionCategories
            .AsNoTracking()
            .Select(c => c.TypeCode)
            .FirstAsync();

        var delete = await svc.DeleteTypeAsync(typeWithChildren, CancellationToken.None);

        Assert.False(delete.Success);
        Assert.True(await db.TransactionTypes.AnyAsync(t => t.TypeCode == typeWithChildren));
    }

    [Fact]
    public async Task TransactionType_Category_Upsert_ThenDelete_Roundtrips()
    {
        using var testDb = new SqliteTestDatabase();
        await using var db = await SeedAsync(testDb);
        var svc = new TransactionTypeService(db);

        // Parent type must exist first.
        await svc.UpsertTypeAsync("9", "ADJUSTMENTS", CancellationToken.None);

        // Category "7" zero-pads to "0007".
        var add = await svc.UpsertCategoryAsync("9", "7", "MANUAL ADJ", CancellationToken.None);
        Assert.True(add.Success, add.Message);

        var stored = await db.TransactionCategories.AsNoTracking()
            .SingleAsync(c => c.TypeCode == "09" && c.CategoryCode == "0007");
        Assert.Equal("MANUAL ADJ", stored.Description);

        var del = await svc.DeleteCategoryAsync("9", "7", CancellationToken.None);
        Assert.True(del.Success, del.Message);
        Assert.False(await db.TransactionCategories.AnyAsync(c => c.TypeCode == "09" && c.CategoryCode == "0007"));
    }

    [Fact]
    public async Task TransactionType_UpsertCategory_UnknownParent_Fails()
    {
        using var testDb = new SqliteTestDatabase();
        await using var db = await SeedAsync(testDb);
        var svc = new TransactionTypeService(db);

        var add = await svc.UpsertCategoryAsync("97", "0001", "ORPHAN", CancellationToken.None);

        Assert.False(add.Success);
    }

    // ==================== StatementService (CBSTM03A) ====================

    [Fact]
    public async Task Statement_Generate_AfterPosting_WritesEscapedTextAndHtml()
    {
        using var testDb = new SqliteTestDatabase();
        await using var db = await SeedAsync(testDb);
        await PostFixtureTransactionsAsync(testDb);

        var textPath = TempPath("stmt", "txt");
        var htmlPath = TempPath("stmt", "html");
        var svc = new StatementService(db);

        var result = await svc.GenerateAsync(textPath, htmlPath, CancellationToken.None);

        Assert.True(result.Statements > 0);
        Assert.True(result.TransactionLines > 0);
        Assert.True(File.Exists(result.TextPath));
        Assert.True(File.Exists(result.HtmlPath));

        var text = await File.ReadAllTextAsync(result.TextPath);
        var html = await File.ReadAllTextAsync(result.HtmlPath);

        // Some real account id appears in both outputs.
        var anyAccount = await db.Accounts.AsNoTracking().Select(a => a.AccountId).FirstAsync();
        Assert.Contains("START OF STATEMENT", text);
        Assert.Contains(anyAccount, text);
        Assert.Contains("<html", html);
        Assert.Contains("Statement for Account Number", html);

        // Safe-target: HTML output is escaped (no raw '<' from data leaks into a broken tag).
        // A valid document ends with the closing tag and contains a proper doctype.
        Assert.Contains("<!DOCTYPE html>", html);
        Assert.Contains("</html>", html);
    }

    [Fact]
    public async Task Statement_HtmlEscapesSpecialCharacters()
    {
        using var testDb = new SqliteTestDatabase();
        await using var db = await SeedAsync(testDb);

        // Craft one card + xref + posted transaction whose description contains HTML
        // metacharacters; the safe target must escape them (legacy defect: no escaping).
        var xref = await db.CardXrefs.AsNoTracking().FirstAsync(x => x.AccountId == SeededAccount);
        db.Transactions.Add(new Transaction
        {
            TransactionId = "TESTSCRIPT000001",
            TypeCode = "01",
            CategoryCode = "0001",
            Source = "POS",
            Description = "<script>alert(1)</script> & \"quote\"",
            Amount = 10.00m,
            MerchantId = "000000001",
            MerchantName = "M",
            MerchantCity = "C",
            MerchantZip = "12345",
            CardNumber = xref.CardNumber,
            OriginTimestamp = "2024-06-11-12.00.00.000000",
            ProcessTimestamp = "2024-06-11-12.00.00.000000",
        });
        await db.SaveChangesAsync();

        var textPath = TempPath("stmt", "txt");
        var htmlPath = TempPath("stmt", "html");
        var result = await new StatementService(db).GenerateAsync(textPath, htmlPath, CancellationToken.None);

        Assert.True(result.Statements > 0);
        var html = await File.ReadAllTextAsync(result.HtmlPath);

        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
    }

    // ==================== TransferService (CBEXPORT / CBIMPORT) ====================

    [Fact]
    public async Task Transfer_Export_Then_Import_RoundTrips_PerTypeCounts()
    {
        using var sourceDb = new SqliteTestDatabase();
        await using var src = await SeedAsync(sourceDb);

        var exportPath = TempPath("export", "dat");
        var export = await new TransferService(src).ExportAsync(exportPath, CancellationToken.None);

        // Seed oracle: 50 customers/accounts/xrefs/cards, 0 transactions before posting.
        Assert.Equal(50, export.RecordsByType["C"]);
        Assert.Equal(50, export.RecordsByType["A"]);
        Assert.Equal(50, export.RecordsByType["X"]);
        Assert.Equal(50, export.RecordsByType["D"]);
        Assert.Equal(0, export.RecordsByType["T"]);
        Assert.Equal(200, export.Records);

        // Import into an independent fresh database (upsert-by-key); counts must match.
        using var targetDb = new SqliteTestDatabase();
        await using var tgt = targetDb.NewContext();
        await tgt.Database.EnsureCreatedAsync();

        var errorPath = TempPath("import-errors", "txt");
        var import = await new TransferService(tgt).ImportAsync(exportPath, errorPath, CancellationToken.None);

        Assert.Equal(200, import.Records);
        Assert.Equal(50, import.RecordsByType["C"]);
        Assert.Equal(50, import.RecordsByType["A"]);
        Assert.Equal(50, import.RecordsByType["X"]);
        Assert.Equal(50, import.RecordsByType["D"]);

        Assert.Equal(50, await tgt.Customers.AsNoTracking().CountAsync());
        Assert.Equal(50, await tgt.Accounts.AsNoTracking().CountAsync());
        Assert.Equal(50, await tgt.CardXrefs.AsNoTracking().CountAsync());
        Assert.Equal(50, await tgt.Cards.AsNoTracking().CountAsync());

        // A logical row is reproduced exactly (money round-trips through the codec).
        var srcAccount = await src.Accounts.AsNoTracking().SingleAsync(a => a.AccountId == SeededAccount);
        var tgtAccount = await tgt.Accounts.AsNoTracking().SingleAsync(a => a.AccountId == SeededAccount);
        Assert.Equal(srcAccount.CreditLimit, tgtAccount.CreditLimit);
        Assert.Equal(srcAccount.CurrentBalance, tgtAccount.CurrentBalance);

        // No malformed records: the error file is empty.
        Assert.Equal(0, new FileInfo(errorPath).Length);
    }

    [Fact]
    public async Task Transfer_Import_MalformedTrailingRecord_GoesToErrorFile()
    {
        using var sourceDb = new SqliteTestDatabase();
        await using var src = await SeedAsync(sourceDb);

        var exportPath = TempPath("export", "dat");
        await new TransferService(src).ExportAsync(exportPath, CancellationToken.None);

        // Corrupt the file by appending a short (truncated) trailing record.
        await File.AppendAllTextAsync(exportPath, "XSHORT");

        using var targetDb = new SqliteTestDatabase();
        await using var tgt = targetDb.NewContext();
        await tgt.Database.EnsureCreatedAsync();

        var errorPath = TempPath("import-errors", "txt");
        var import = await new TransferService(tgt).ImportAsync(exportPath, errorPath, CancellationToken.None);

        // The 200 well-formed records still import; the trailing short record is rejected.
        Assert.Equal(200, import.Records);
        var errorText = await File.ReadAllTextAsync(errorPath);
        Assert.Contains("Short record", errorText);
    }

    // ==================== AuthorizationService (COPAU* / CBPAUP0C) ====================

    [Fact]
    public async Task Auth_Approvable_Below_CreditLimit_IsApproved_AndUpdatesSummary()
    {
        using var testDb = new SqliteTestDatabase();
        await using var db = await SeedAsync(testDb);
        var svc = new AuthorizationService(db, Clock);

        // Derive an approvable amount from the seeded account's actual available credit.
        var account = await db.Accounts.AsNoTracking().SingleAsync(a => a.AccountId == SeededAccount);
        var amount = decimal.Round(account.CreditLimit / 2m, 2);
        Assert.True(amount > 0m);

        var submit = await svc.SubmitAsync(new AuthorizationRequest
        {
            CardNumber = SeededCard,
            TransactionAmount = amount,
            AuthType = "01",
            MerchantId = "000000001",
            MerchantName = "MERCH",
        }, CancellationToken.None);
        Assert.True(submit.Success, submit.Message);

        var result = await svc.ProcessPendingAsync(maxMessages: 10, CancellationToken.None);
        Assert.Equal(1, result.Processed);
        Assert.Equal(1, result.Approved);
        Assert.Equal(0, result.Declined);

        var summary = await db.PendingAuthSummaries.AsNoTracking().SingleAsync(s => s.AccountId == SeededAccount);
        Assert.Equal(amount, summary.CreditBalance);
        Assert.Equal(1, summary.ApprovedAuthCount);
        Assert.Equal(amount, summary.ApprovedAuthAmount);

        var detail = await db.PendingAuthDetails.AsNoTracking().SingleAsync(d => d.AccountId == SeededAccount);
        Assert.Equal("00", detail.AuthRespCode);
        Assert.Equal(amount, detail.ApprovedAmount);

        var reply = await db.AuthorizationReplies.AsNoTracking().SingleAsync();
        Assert.Equal("00", reply.AuthRespCode);
        Assert.Equal(amount, reply.ApprovedAmount);

        // The request is marked PROCESSED.
        Assert.False(await db.AuthorizationRequests.AsNoTracking().AnyAsync(r => r.Status == "PENDING"));
    }

    [Fact]
    public async Task Auth_OverLimit_IsDeclined_WithRespCode05()
    {
        using var testDb = new SqliteTestDatabase();
        await using var db = await SeedAsync(testDb);
        var svc = new AuthorizationService(db, Clock);

        var account = await db.Accounts.AsNoTracking().SingleAsync(a => a.AccountId == SeededAccount);
        var overLimit = account.CreditLimit + 1_000_000m;

        await svc.SubmitAsync(new AuthorizationRequest
        {
            CardNumber = SeededCard,
            TransactionAmount = overLimit,
            AuthType = "01",
        }, CancellationToken.None);

        var result = await svc.ProcessPendingAsync(maxMessages: 10, CancellationToken.None);
        Assert.Equal(1, result.Processed);
        Assert.Equal(0, result.Approved);
        Assert.Equal(1, result.Declined);

        var detail = await db.PendingAuthDetails.AsNoTracking().SingleAsync();
        Assert.Equal("05", detail.AuthRespCode);
        Assert.Equal(0m, detail.ApprovedAmount);

        var summary = await db.PendingAuthSummaries.AsNoTracking().SingleAsync();
        Assert.Equal(0m, summary.CreditBalance);
        Assert.Equal(1, summary.DeclinedAuthCount);
    }

    [Fact]
    public async Task Auth_UnknownCard_IsDeclined_InvalidCard()
    {
        using var testDb = new SqliteTestDatabase();
        await using var db = await SeedAsync(testDb);
        var svc = new AuthorizationService(db, Clock);

        await svc.SubmitAsync(new AuthorizationRequest
        {
            CardNumber = "9999999999999999",
            TransactionAmount = 10.00m,
        }, CancellationToken.None);

        var result = await svc.ProcessPendingAsync(maxMessages: 10, CancellationToken.None);
        Assert.Equal(1, result.Processed);
        Assert.Equal(1, result.Declined);

        var detail = await db.PendingAuthDetails.AsNoTracking().SingleAsync();
        Assert.Equal("05", detail.AuthRespCode);
        Assert.Equal("3100", detail.AuthRespReason);
    }

    [Fact]
    public async Task Auth_Submit_BlankCard_Fails()
    {
        using var testDb = new SqliteTestDatabase();
        await using var db = await SeedAsync(testDb);
        var svc = new AuthorizationService(db, Clock);

        var submit = await svc.SubmitAsync(new AuthorizationRequest { CardNumber = "  " }, CancellationToken.None);

        Assert.False(submit.Success);
        Assert.Equal(0, await db.AuthorizationRequests.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task Auth_GetDetails_ReturnsNewestFirst()
    {
        using var testDb = new SqliteTestDatabase();
        await using var db = await SeedAsync(testDb);
        var svc = new AuthorizationService(db, Clock);

        var account = await db.Accounts.AsNoTracking().SingleAsync(a => a.AccountId == SeededAccount);
        var unit = decimal.Round(account.CreditLimit / 10m, 2);

        // Two approvable requests processed in one run; the second is "newer".
        await svc.SubmitAsync(new AuthorizationRequest { CardNumber = SeededCard, TransactionAmount = unit, AuthType = "01" }, CancellationToken.None);
        await svc.SubmitAsync(new AuthorizationRequest { CardNumber = SeededCard, TransactionAmount = unit, AuthType = "02" }, CancellationToken.None);
        await svc.ProcessPendingAsync(maxMessages: 10, CancellationToken.None);

        var details = await svc.GetDetailsAsync(SeededAccount, CancellationToken.None);
        Assert.True(details.Success);
        Assert.Equal(2, details.Value!.Count);
        // Newest (the second-submitted, AuthType "02") sorts first.
        Assert.Equal("02", details.Value[0].AuthType);
    }

    [Fact]
    public async Task Auth_SetFraud_FlipsFlag_AndWritesFraudHistory()
    {
        using var testDb = new SqliteTestDatabase();
        await using var db = await SeedAsync(testDb);
        var svc = new AuthorizationService(db, Clock);

        var account = await db.Accounts.AsNoTracking().SingleAsync(a => a.AccountId == SeededAccount);
        await svc.SubmitAsync(new AuthorizationRequest
        {
            CardNumber = SeededCard,
            TransactionAmount = decimal.Round(account.CreditLimit / 4m, 2),
            AuthType = "01",
        }, CancellationToken.None);
        await svc.ProcessPendingAsync(maxMessages: 10, CancellationToken.None);

        var detail = await db.PendingAuthDetails.AsNoTracking().SingleAsync();

        var fraud = await svc.SetFraudAsync(detail.Id, fraud: true, CancellationToken.None);
        Assert.True(fraud.Success, fraud.Message);

        var updated = await db.PendingAuthDetails.AsNoTracking().SingleAsync(d => d.Id == detail.Id);
        Assert.Equal("F", updated.AuthFraud);
        Assert.Equal("2024-06-11", updated.FraudReportDate);

        var history = await db.AuthFraudHistories.AsNoTracking().SingleAsync();
        Assert.Equal(detail.CardNumber, history.CardNumber);
        Assert.Equal("F", history.AuthFraud);
        Assert.Equal(SeededAccount, history.AccountId);
        Assert.False(string.IsNullOrEmpty(history.CustomerId));
    }

    [Fact]
    public async Task Auth_SetFraud_MissingDetail_Fails()
    {
        using var testDb = new SqliteTestDatabase();
        await using var db = await SeedAsync(testDb);
        var svc = new AuthorizationService(db, Clock);

        var result = await svc.SetFraudAsync(detailId: 999999, fraud: true, CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Auth_PurgeExpired_ZeroDays_RemovesDetailsAndOrphanSummaries()
    {
        using var testDb = new SqliteTestDatabase();
        await using var db = await SeedAsync(testDb);
        var svc = new AuthorizationService(db, Clock);

        var account = await db.Accounts.AsNoTracking().SingleAsync(a => a.AccountId == SeededAccount);
        await svc.SubmitAsync(new AuthorizationRequest
        {
            CardNumber = SeededCard,
            TransactionAmount = decimal.Round(account.CreditLimit / 3m, 2),
            AuthType = "01",
        }, CancellationToken.None);
        await svc.ProcessPendingAsync(maxMessages: 10, CancellationToken.None);

        Assert.Equal(1, await db.PendingAuthDetails.AsNoTracking().CountAsync());

        // Details were created "today" (fixed clock); purge(0) removes everything up to today.
        var deleted = await svc.PurgeExpiredAsync(days: 0, CancellationToken.None);

        Assert.Equal(1, deleted);
        Assert.Equal(0, await db.PendingAuthDetails.AsNoTracking().CountAsync());
        // The summary is now orphaned (no remaining details) and is removed too.
        Assert.Equal(0, await db.PendingAuthSummaries.AsNoTracking().CountAsync());
    }

    // ==================== InquiryService (COACCT01 / CODATE01) ====================

    [Fact]
    public async Task Inquiry_Account_Found_ReplyContainsAccountIdAndBalance()
    {
        using var testDb = new SqliteTestDatabase();
        await using var db = await SeedAsync(testDb);
        var svc = new InquiryService(db, Clock);

        var submit = await svc.SubmitAccountInquiryAsync(SeededAccount, CancellationToken.None);
        Assert.True(submit.Success, submit.Message);

        var result = await svc.ProcessPendingAsync("INQA", maxMessages: 10, CancellationToken.None);
        Assert.Equal(1, result.Processed);

        var reply = await db.InquiryReplies.AsNoTracking().SingleAsync();
        Assert.Contains(SeededAccount, reply.Payload);
        Assert.Contains("BALANCE", reply.Payload);
        Assert.Contains("ACCOUNT STATUS", reply.Payload);
        Assert.Equal(reply.Payload.Length, reply.LogicalLength);

        // Request drained.
        Assert.False(await db.InquiryRequests.AsNoTracking().AnyAsync(r => r.Status == "PENDING"));
    }

    [Fact]
    public async Task Inquiry_Account_NotFound_ReplyIsInvalidRequest()
    {
        using var testDb = new SqliteTestDatabase();
        await using var db = await SeedAsync(testDb);
        var svc = new InquiryService(db, Clock);

        // Well-formed 11-digit key that is not a seeded account.
        const string missing = "99999999999";
        var submit = await svc.SubmitAccountInquiryAsync(missing, CancellationToken.None);
        Assert.True(submit.Success, submit.Message);

        await svc.ProcessPendingAsync("INQA", maxMessages: 10, CancellationToken.None);

        var reply = await db.InquiryReplies.AsNoTracking().SingleAsync();
        Assert.Contains("INVALID REQUEST PARAMETERS", reply.Payload);
        Assert.Contains(missing, reply.Payload);
    }

    [Fact]
    public async Task Inquiry_Account_InvalidKey_Fails()
    {
        using var testDb = new SqliteTestDatabase();
        await using var db = await SeedAsync(testDb);
        var svc = new InquiryService(db, Clock);

        var submit = await svc.SubmitAccountInquiryAsync("123", CancellationToken.None);

        Assert.False(submit.Success);
        Assert.Equal(0, await db.InquiryRequests.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task Inquiry_Date_ReplyContainsSystemDate()
    {
        using var testDb = new SqliteTestDatabase();
        await using var db = await SeedAsync(testDb);
        var svc = new InquiryService(db, Clock);

        var submit = await svc.SubmitDateInquiryAsync(CancellationToken.None);
        Assert.True(submit.Success, submit.Message);

        var result = await svc.ProcessPendingAsync("DATE", maxMessages: 10, CancellationToken.None);
        Assert.Equal(1, result.Processed);

        var reply = await db.InquiryReplies.AsNoTracking().SingleAsync();
        Assert.Contains("SYSTEM DATE", reply.Payload);
        Assert.Contains("SYSTEM TIME", reply.Payload);
        // Fixed clock is 2024-06-11 12:00:00 -> MM-DD-YYYY.
        Assert.Contains("06-11-2024", reply.Payload);
    }

    [Fact]
    public async Task Inquiry_RecentReplies_ReturnsNewestFirst()
    {
        using var testDb = new SqliteTestDatabase();
        await using var db = await SeedAsync(testDb);
        var svc = new InquiryService(db, Clock);

        await svc.SubmitDateInquiryAsync(CancellationToken.None);
        await svc.ProcessPendingAsync("DATE", maxMessages: 10, CancellationToken.None);
        await svc.SubmitAccountInquiryAsync(SeededAccount, CancellationToken.None);
        await svc.ProcessPendingAsync("INQA", maxMessages: 10, CancellationToken.None);

        var recent = await svc.RecentRepliesAsync(take: 1, CancellationToken.None);

        Assert.Single(recent);
        Assert.Equal("INQA", recent[0].Service);
    }
}
