using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Modules.Users.Persistence;

public sealed class InviteConfiguration : IEntityTypeConfiguration<Invite>
{
    public void Configure(EntityTypeBuilder<Invite> builder)
    {
        builder.ToTable("invite");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(i => i.UserId).HasColumnName("user_id");
        builder.Property(i => i.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsRequired();
        builder.Property(i => i.ExpiresAt).HasColumnName("expires_at");
        builder.Property(i => i.UsedAt).HasColumnName("used_at");

        builder.HasIndex(i => i.TokenHash).IsUnique();
        builder.HasOne<AppUser>().WithMany().HasForeignKey(i => i.UserId);
    }
}
