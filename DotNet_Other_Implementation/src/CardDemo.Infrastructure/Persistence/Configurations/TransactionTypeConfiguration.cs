using CardDemo.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardDemo.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="TransactionType"/> (CVTRA03Y, RECLN 60).</summary>
public sealed class TransactionTypeConfiguration : IEntityTypeConfiguration<TransactionType>
{
    public void Configure(EntityTypeBuilder<TransactionType> builder)
    {
        builder.ToTable("TransactionTypes");
        builder.HasKey(t => t.TypeCode);

        builder.Property(t => t.TypeCode).HasMaxLength(2).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(50).IsRequired();
    }
}
