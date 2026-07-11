using CardDemo.Domain.Common;
using CardDemo.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CardDemo.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the CardDemo relational store (SQLite by default).
/// Entity mappings live in per-entity IEntityTypeConfiguration classes in the
/// Configurations folder and are applied from this assembly. Money persists as
/// integer minor units and rates as integer hundredths of a percent via value
/// converters (09-DotNet-Target-Architecture.md#persistence-design).
/// </summary>
public sealed class CardDemoDbContext(DbContextOptions<CardDemoDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Card> Cards => Set<Card>();
    public DbSet<CardXref> CardXrefs => Set<CardXref>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<TransactionType> TransactionTypes => Set<TransactionType>();
    public DbSet<TransactionCategory> TransactionCategories => Set<TransactionCategory>();
    public DbSet<TransactionCategoryBalance> TransactionCategoryBalances => Set<TransactionCategoryBalance>();
    public DbSet<DisclosureGroup> DisclosureGroups => Set<DisclosureGroup>();
    public DbSet<UserSecurity> Users => Set<UserSecurity>();
    public DbSet<PendingReportRequest> PendingReportRequests => Set<PendingReportRequest>();

    // Optional modules.
    public DbSet<PendingAuthSummary> PendingAuthSummaries => Set<PendingAuthSummary>();
    public DbSet<PendingAuthDetail> PendingAuthDetails => Set<PendingAuthDetail>();
    public DbSet<AuthFraudHistory> AuthFraudHistories => Set<AuthFraudHistory>();
    public DbSet<AuthorizationRequest> AuthorizationRequests => Set<AuthorizationRequest>();
    public DbSet<AuthorizationReply> AuthorizationReplies => Set<AuthorizationReply>();
    public DbSet<InquiryRequest> InquiryRequests => Set<InquiryRequest>();
    public DbSet<InquiryReply> InquiryReplies => Set<InquiryReply>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CardDemoDbContext).Assembly);
    }

    public override int SaveChanges()
    {
        BumpConcurrencyTokens();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        BumpConcurrencyTokens();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Advance the optimistic-concurrency token on every modified IVersioned entity.</summary>
    private void BumpConcurrencyTokens()
    {
        foreach (var entry in ChangeTracker.Entries<IVersioned>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
                entry.Entity.RowVersion++;
        }
    }
}
