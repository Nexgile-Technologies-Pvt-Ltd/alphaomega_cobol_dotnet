using CardDemo.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardDemo.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="Card"/> (CVACT02Y, RECLN 150).</summary>
public sealed class CardConfiguration : IEntityTypeConfiguration<Card>
{
    public void Configure(EntityTypeBuilder<Card> builder)
    {
        builder.ToTable("Cards");
        builder.HasKey(c => c.CardNumber);

        builder.Property(c => c.CardNumber).HasMaxLength(16).IsRequired();
        builder.Property(c => c.AccountId).HasMaxLength(11).IsRequired();
        builder.Property(c => c.Cvv).HasMaxLength(3).IsRequired();
        builder.Property(c => c.EmbossedName).HasMaxLength(50).IsRequired();
        builder.Property(c => c.ExpirationDate).HasMaxLength(10).IsRequired();
        builder.Property(c => c.ActiveStatus).HasMaxLength(1).IsRequired();

        builder.HasIndex(c => c.AccountId);
        builder.Property(c => c.RowVersion).IsConcurrencyToken();
    }
}
