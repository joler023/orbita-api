using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Orbita.Domain.Identity;

namespace Orbita.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(u => u.Email)
            .HasColumnName("email")
            .HasColumnType("citext")
            .IsRequired();
        builder.HasIndex(u => u.Email).IsUnique();

        builder.Property(u => u.PasswordHash).HasColumnName("password_hash").IsRequired();

        builder.Property(u => u.FullName)
            .HasColumnName("full_name")
            .HasMaxLength(User.FullNameMaxLength)
            .IsRequired();

        builder.Property(u => u.EmailVerifiedAt).HasColumnName("email_verified_at");
        builder.Property(u => u.LastLoginAt).HasColumnName("last_login_at");

        // Not in orbita-schema.dbml (added for ORB-A06's lockout acceptance criterion).
        builder.Property(u => u.FailedLoginAttempts).HasColumnName("failed_login_attempts").IsRequired();
        builder.Property(u => u.LockedUntil).HasColumnName("locked_until");

        // Not in orbita-schema.dbml (added for ORB-A07 — see User.PasswordSetAt).
        builder.Property(u => u.PasswordSetAt).HasColumnName("password_set_at");

        // Not in orbita-schema.dbml (added for ORB-A11). The ciphertext, never the
        // raw secret, is what's persisted — see IUserSecretProtector.
        builder.Property(u => u.TwoFactorSecretCiphertext).HasColumnName("two_factor_secret_ciphertext");
        builder.Property(u => u.TwoFactorEnabledAt).HasColumnName("two_factor_enabled_at");

        builder.Property(u => u.CreatedAt).HasColumnName("created_at").IsRequired();
    }
}
