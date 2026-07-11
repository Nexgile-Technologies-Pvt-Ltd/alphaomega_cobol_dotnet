using CardDemo.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardDemo.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="PendingReportRequest"/>. Identity primary key.</summary>
public sealed class PendingReportRequestConfiguration : IEntityTypeConfiguration<PendingReportRequest>
{
    public void Configure(EntityTypeBuilder<PendingReportRequest> builder)
    {
        builder.ToTable("PendingReportRequests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedOnAdd();

        builder.Property(r => r.FromDate).HasMaxLength(10).IsRequired();
        builder.Property(r => r.ToDate).HasMaxLength(10).IsRequired();
        builder.Property(r => r.RequestedByUserId).HasMaxLength(8).IsRequired();
        builder.Property(r => r.RequestedAt).HasMaxLength(26).IsRequired();
        builder.Property(r => r.Status).HasMaxLength(20).IsRequired();
    }
}
