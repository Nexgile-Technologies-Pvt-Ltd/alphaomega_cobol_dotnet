using CardDemo.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardDemo.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="Customer"/> (CVCUS01Y, RECLN 500).</summary>
public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");
        builder.HasKey(c => c.CustomerId);

        builder.Property(c => c.CustomerId).HasMaxLength(9).IsRequired();
        builder.Property(c => c.FirstName).HasMaxLength(25).IsRequired();
        builder.Property(c => c.MiddleName).HasMaxLength(25).IsRequired();
        builder.Property(c => c.LastName).HasMaxLength(25).IsRequired();
        builder.Property(c => c.AddressLine1).HasMaxLength(50).IsRequired();
        builder.Property(c => c.AddressLine2).HasMaxLength(50).IsRequired();
        builder.Property(c => c.AddressLine3).HasMaxLength(50).IsRequired();
        builder.Property(c => c.StateCode).HasMaxLength(2).IsRequired();
        builder.Property(c => c.CountryCode).HasMaxLength(3).IsRequired();
        builder.Property(c => c.Zip).HasMaxLength(10).IsRequired();
        builder.Property(c => c.PhoneNumber1).HasMaxLength(15).IsRequired();
        builder.Property(c => c.PhoneNumber2).HasMaxLength(15).IsRequired();
        builder.Property(c => c.Ssn).HasMaxLength(9).IsRequired();
        builder.Property(c => c.GovtIssuedId).HasMaxLength(20).IsRequired();
        builder.Property(c => c.DateOfBirth).HasMaxLength(10).IsRequired();
        builder.Property(c => c.EftAccountId).HasMaxLength(10).IsRequired();
        builder.Property(c => c.PrimaryCardHolderIndicator).HasMaxLength(1).IsRequired();
        builder.Property(c => c.FicoCreditScore);
    }
}
