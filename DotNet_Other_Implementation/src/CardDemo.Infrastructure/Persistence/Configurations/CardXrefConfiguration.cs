using CardDemo.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardDemo.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="CardXref"/> (CVACT03Y, 36-byte ASCII compat form).</summary>
public sealed class CardXrefConfiguration : IEntityTypeConfiguration<CardXref>
{
    public void Configure(EntityTypeBuilder<CardXref> builder)
    {
        builder.ToTable("CardXrefs");
        builder.HasKey(x => x.CardNumber);

        builder.Property(x => x.CardNumber).HasMaxLength(16).IsRequired();
        builder.Property(x => x.CustomerId).HasMaxLength(9).IsRequired();
        builder.Property(x => x.AccountId).HasMaxLength(11).IsRequired();

        builder.HasIndex(x => x.AccountId);
    }
}
