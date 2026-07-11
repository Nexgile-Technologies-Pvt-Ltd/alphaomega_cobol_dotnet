using CardDemo.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardDemo.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="UserSecurity"/> (CSUSR01Y, RECLN 80). Stores a hash, never cleartext.</summary>
public sealed class UserSecurityConfiguration : IEntityTypeConfiguration<UserSecurity>
{
    public void Configure(EntityTypeBuilder<UserSecurity> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.UserId);

        builder.Property(u => u.UserId).HasMaxLength(8).IsRequired();
        builder.Property(u => u.FirstName).HasMaxLength(20).IsRequired();
        builder.Property(u => u.LastName).HasMaxLength(20).IsRequired();
        builder.Property(u => u.PasswordHash).IsRequired();
        builder.Property(u => u.UserType).HasMaxLength(1).IsRequired();

        builder.Ignore(u => u.IsAdmin);
        builder.Property(u => u.RowVersion).IsConcurrencyToken();
    }
}
