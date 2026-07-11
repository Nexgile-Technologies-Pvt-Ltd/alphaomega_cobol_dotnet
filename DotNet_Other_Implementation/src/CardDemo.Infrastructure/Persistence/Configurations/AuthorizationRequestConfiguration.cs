using CardDemo.Domain.Entities;
using CardDemo.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardDemo.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="AuthorizationRequest"/> (durable inbound-request queue that replaces
/// the inbound MQ). Identity primary key; index on Status for the pending drain.
/// </summary>
public sealed class AuthorizationRequestConfiguration : IEntityTypeConfiguration<AuthorizationRequest>
{
    public void Configure(EntityTypeBuilder<AuthorizationRequest> builder)
    {
        builder.ToTable("AuthorizationRequests");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();

        builder.Property(x => x.CardNumber).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(16).IsRequired();

        builder.Property(x => x.TransactionAmount).HasConversion(ValueConverters.MoneyToCents);

        builder.HasIndex(x => x.Status);
    }
}
