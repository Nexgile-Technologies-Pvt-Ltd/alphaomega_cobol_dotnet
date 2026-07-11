using CardDemo.Domain.Entities;
using CardDemo.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardDemo.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="Account"/> (CVACT01Y, RECLN 300).</summary>
public sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("Accounts");
        builder.HasKey(a => a.AccountId);

        builder.Property(a => a.AccountId).HasMaxLength(11).IsRequired();
        builder.Property(a => a.ActiveStatus).HasMaxLength(1).IsRequired();
        builder.Property(a => a.CurrentBalance).HasConversion(ValueConverters.MoneyToCents);
        builder.Property(a => a.CreditLimit).HasConversion(ValueConverters.MoneyToCents);
        builder.Property(a => a.CashCreditLimit).HasConversion(ValueConverters.MoneyToCents);
        builder.Property(a => a.OpenDate).HasMaxLength(10).IsRequired();
        builder.Property(a => a.ExpirationDate).HasMaxLength(10).IsRequired();
        builder.Property(a => a.ReissueDate).HasMaxLength(10).IsRequired();
        builder.Property(a => a.CurrentCycleCredit).HasConversion(ValueConverters.MoneyToCents);
        builder.Property(a => a.CurrentCycleDebit).HasConversion(ValueConverters.MoneyToCents);
        builder.Property(a => a.AddressZip).HasMaxLength(10).IsRequired();
        builder.Property(a => a.GroupId).HasMaxLength(10).IsRequired();

        builder.Property(a => a.RowVersion).IsConcurrencyToken();
    }
}
