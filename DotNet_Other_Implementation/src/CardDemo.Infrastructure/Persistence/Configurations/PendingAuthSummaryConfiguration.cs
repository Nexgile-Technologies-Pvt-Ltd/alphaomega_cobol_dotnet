using CardDemo.Domain.Entities;
using CardDemo.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardDemo.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="PendingAuthSummary"/> (IMS root PAUTSUM0, one row per account).
/// Money fields persist as integer minor units; RowVersion is the concurrency token.
/// </summary>
public sealed class PendingAuthSummaryConfiguration : IEntityTypeConfiguration<PendingAuthSummary>
{
    public void Configure(EntityTypeBuilder<PendingAuthSummary> builder)
    {
        builder.ToTable("PendingAuthSummaries");
        builder.HasKey(x => x.AccountId);

        builder.Property(x => x.AccountId).HasMaxLength(11).IsRequired();
        builder.Property(x => x.CustomerId).HasMaxLength(9).IsRequired();

        builder.Property(x => x.CreditLimit).HasConversion(ValueConverters.MoneyToCents);
        builder.Property(x => x.CashLimit).HasConversion(ValueConverters.MoneyToCents);
        builder.Property(x => x.CreditBalance).HasConversion(ValueConverters.MoneyToCents);
        builder.Property(x => x.CashBalance).HasConversion(ValueConverters.MoneyToCents);
        builder.Property(x => x.ApprovedAuthAmount).HasConversion(ValueConverters.MoneyToCents);
        builder.Property(x => x.DeclinedAuthAmount).HasConversion(ValueConverters.MoneyToCents);

        builder.Property(x => x.RowVersion).IsConcurrencyToken();
    }
}
