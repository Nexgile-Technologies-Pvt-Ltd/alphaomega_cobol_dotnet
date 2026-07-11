using CardDemo.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardDemo.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="InquiryRequest"/> — the durable inbound request queue that replaces
/// the inbound MQ drained by COACCT01 (service "INQA") and CODATE01 (service "DATE").
/// Identity primary key; index on (Service, Status) for the pending drain.
/// </summary>
public sealed class InquiryRequestConfiguration : IEntityTypeConfiguration<InquiryRequest>
{
    public void Configure(EntityTypeBuilder<InquiryRequest> builder)
    {
        builder.ToTable("InquiryRequests");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();

        builder.Property(x => x.Service).HasMaxLength(4).IsRequired();
        builder.Property(x => x.AccountId).HasMaxLength(11).IsRequired();
        builder.Property(x => x.CorrelId).HasMaxLength(48).IsRequired();
        builder.Property(x => x.ReplyToQueue).HasMaxLength(48).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(16).IsRequired();
        builder.Property(x => x.CreatedTimestamp).HasMaxLength(26).IsRequired();

        builder.HasIndex(x => new { x.Service, x.Status });
    }
}
