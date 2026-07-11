using CardDemo.Domain.Entities;
using CardDemo.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardDemo.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="Transaction"/> (CVTRA05Y, RECLN 350).</summary>
public sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("Transactions");
        builder.HasKey(t => t.TransactionId);

        builder.Property(t => t.TransactionId).HasMaxLength(16).IsRequired();
        builder.Property(t => t.TypeCode).HasMaxLength(2).IsRequired();
        builder.Property(t => t.CategoryCode).HasMaxLength(4).IsRequired();
        builder.Property(t => t.Source).HasMaxLength(10).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(100).IsRequired();
        builder.Property(t => t.Amount).HasConversion(ValueConverters.MoneyToCents);
        builder.Property(t => t.MerchantId).HasMaxLength(9).IsRequired();
        builder.Property(t => t.MerchantName).HasMaxLength(50).IsRequired();
        builder.Property(t => t.MerchantCity).HasMaxLength(50).IsRequired();
        builder.Property(t => t.MerchantZip).HasMaxLength(10).IsRequired();
        builder.Property(t => t.CardNumber).HasMaxLength(16).IsRequired();
        builder.Property(t => t.OriginTimestamp).HasMaxLength(26).IsRequired();
        builder.Property(t => t.ProcessTimestamp).HasMaxLength(26).IsRequired();

        builder.HasIndex(t => t.CardNumber);
    }
}
