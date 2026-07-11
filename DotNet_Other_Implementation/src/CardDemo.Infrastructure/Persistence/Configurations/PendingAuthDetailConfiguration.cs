using CardDemo.Domain.Entities;
using CardDemo.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardDemo.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="PendingAuthDetail"/> (IMS child PAUTDTL1). Identity primary key;
/// <see cref="PendingAuthDetail.AuthKey"/> is reverse-encoded so newest sorts first.
/// </summary>
public sealed class PendingAuthDetailConfiguration : IEntityTypeConfiguration<PendingAuthDetail>
{
    public void Configure(EntityTypeBuilder<PendingAuthDetail> builder)
    {
        builder.ToTable("PendingAuthDetails");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();

        builder.Property(x => x.AccountId).HasMaxLength(11).IsRequired();
        builder.Property(x => x.AuthKey).HasMaxLength(24).IsRequired();
        builder.Property(x => x.CardNumber).HasMaxLength(16).IsRequired();

        builder.Property(x => x.TransactionAmount).HasConversion(ValueConverters.MoneyToCents);
        builder.Property(x => x.ApprovedAmount).HasConversion(ValueConverters.MoneyToCents);

        builder.HasIndex(x => x.AccountId);
    }
}
