using CardDemo.Domain.Entities;
using CardDemo.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardDemo.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="AuthFraudHistory"/> (Db2 CARDDEMO.AUTHFRDS). Identity primary key;
/// index on card number mirrors the XAUTHFRD access path.
/// </summary>
public sealed class AuthFraudHistoryConfiguration : IEntityTypeConfiguration<AuthFraudHistory>
{
    public void Configure(EntityTypeBuilder<AuthFraudHistory> builder)
    {
        builder.ToTable("AuthFraudHistories");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();

        builder.Property(x => x.CardNumber).HasMaxLength(16).IsRequired();
        builder.Property(x => x.AuthTimestamp).HasMaxLength(26).IsRequired();

        builder.Property(x => x.TransactionAmount).HasConversion(ValueConverters.MoneyToCents);
        builder.Property(x => x.ApprovedAmount).HasConversion(ValueConverters.MoneyToCents);

        builder.HasIndex(x => x.CardNumber);
    }
}
