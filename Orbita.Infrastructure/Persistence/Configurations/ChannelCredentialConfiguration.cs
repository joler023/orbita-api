using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Orbita.Infrastructure.Channels;

namespace Orbita.Infrastructure.Persistence.Configurations;

/// <summary>Not tenant-scoped and not RLS'd — see <see cref="ChannelCredential"/>. Not in orbita-schema.dbml (local stand-in for Secrets Manager).</summary>
public sealed class ChannelCredentialConfiguration : IEntityTypeConfiguration<ChannelCredential>
{
    public void Configure(EntityTypeBuilder<ChannelCredential> builder)
    {
        builder.ToTable("channel_credentials");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(c => c.Ciphertext).HasColumnName("ciphertext").IsRequired();
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();
    }
}
