using CardDemo.Domain.Entities;
using CardDemo.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardDemo.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="AuthorizationReply"/> (reply outbox). Identity primary key; index on
/// RequestId links each reply back to the request that produced it.
/// </summary>
public sealed class AuthorizationReplyConfiguration : IEntityTypeConfiguration<AuthorizationReply>
{
    public void Configure(EntityTypeBuilder<AuthorizationReply> builder)
    {
        builder.ToTable("AuthorizationReplies");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();

        builder.Property(x => x.CardNumber).HasMaxLength(16).IsRequired();

        builder.Property(x => x.ApprovedAmount).HasConversion(ValueConverters.MoneyToCents);

        builder.HasIndex(x => x.RequestId);
    }
}
