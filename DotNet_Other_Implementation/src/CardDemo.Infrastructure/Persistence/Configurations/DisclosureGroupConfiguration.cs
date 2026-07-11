using CardDemo.Domain.Entities;
using CardDemo.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardDemo.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="DisclosureGroup"/> (CVTRA02Y, composite key group+type+category).</summary>
public sealed class DisclosureGroupConfiguration : IEntityTypeConfiguration<DisclosureGroup>
{
    public void Configure(EntityTypeBuilder<DisclosureGroup> builder)
    {
        builder.ToTable("DisclosureGroups");
        builder.HasKey(d => new { d.GroupId, d.TypeCode, d.CategoryCode });

        builder.Property(d => d.GroupId).HasMaxLength(10).IsRequired();
        builder.Property(d => d.TypeCode).HasMaxLength(2).IsRequired();
        builder.Property(d => d.CategoryCode).HasMaxLength(4).IsRequired();
        builder.Property(d => d.InterestRate).HasConversion(ValueConverters.RateToHundredths);
    }
}
