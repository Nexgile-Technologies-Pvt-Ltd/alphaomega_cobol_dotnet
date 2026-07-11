using CardDemo.Domain.Entities;
using CardDemo.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardDemo.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="TransactionCategoryBalance"/> (CVTRA01Y, composite key account+type+category).</summary>
public sealed class TransactionCategoryBalanceConfiguration : IEntityTypeConfiguration<TransactionCategoryBalance>
{
    public void Configure(EntityTypeBuilder<TransactionCategoryBalance> builder)
    {
        builder.ToTable("TransactionCategoryBalances");
        builder.HasKey(b => new { b.AccountId, b.TypeCode, b.CategoryCode });

        builder.Property(b => b.AccountId).HasMaxLength(11).IsRequired();
        builder.Property(b => b.TypeCode).HasMaxLength(2).IsRequired();
        builder.Property(b => b.CategoryCode).HasMaxLength(4).IsRequired();
        builder.Property(b => b.Balance).HasConversion(ValueConverters.MoneyToCents);

        builder.Property(b => b.RowVersion).IsConcurrencyToken();
    }
}
