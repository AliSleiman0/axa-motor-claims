using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Modules.Users.Persistence;

public sealed class OtpChallengeConfiguration : IEntityTypeConfiguration<OtpChallenge>
{
    public void Configure(EntityTypeBuilder<OtpChallenge> builder)
    {
        builder.ToTable("otp_challenge");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(c => c.Phone).HasColumnName("phone").HasMaxLength(16).IsRequired();
        builder.Property(c => c.CodeHash).HasColumnName("code_hash").HasMaxLength(64).IsRequired();
        builder.Property(c => c.ExpiresAt).HasColumnName("expires_at");
        builder.Property(c => c.Attempts).HasColumnName("attempts");
        builder.Property(c => c.ConsumedAt).HasColumnName("consumed_at");
        builder.Property(c => c.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(c => c.Phone);
    }
}
