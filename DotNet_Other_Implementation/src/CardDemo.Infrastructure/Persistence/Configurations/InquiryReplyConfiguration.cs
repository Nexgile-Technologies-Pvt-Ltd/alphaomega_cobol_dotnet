using CardDemo.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardDemo.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="InquiryReply"/> — the reply outbox for the inquiry/date workers.
/// Identity primary key; index on RequestId to join back to the originating request.
/// </summary>
public sealed class InquiryReplyConfiguration : IEntityTypeConfiguration<InquiryReply>
{
    public void Configure(EntityTypeBuilder<InquiryReply> builder)
    {
        builder.ToTable("InquiryReplies");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();

        builder.Property(x => x.RequestId).IsRequired();
        builder.Property(x => x.Service).HasMaxLength(4).IsRequired();
        builder.Property(x => x.Payload).IsRequired();
        builder.Property(x => x.LogicalLength).IsRequired();
        builder.Property(x => x.CreatedTimestamp).HasMaxLength(26).IsRequired();

        builder.HasIndex(x => x.RequestId);
    }
}
