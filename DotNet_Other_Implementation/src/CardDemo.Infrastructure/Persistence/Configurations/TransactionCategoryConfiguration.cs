using CardDemo.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardDemo.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="TransactionCategory"/> (CVTRA04Y, composite key type+category).</summary>
public sealed class TransactionCategoryConfiguration : IEntityTypeConfiguration<TransactionCategory>
{
    public void Configure(EntityTypeBuilder<TransactionCategory> builder)
    {
        builder.ToTable("TransactionCategories");
        builder.HasKey(c => new { c.TypeCode, c.CategoryCode });

        builder.Property(c => c.TypeCode).HasMaxLength(2).IsRequired();
        builder.Property(c => c.CategoryCode).HasMaxLength(4).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(50).IsRequired();
    }
}
