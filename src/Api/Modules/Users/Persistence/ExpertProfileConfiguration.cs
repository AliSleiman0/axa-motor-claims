using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Modules.Users.Persistence;

public sealed class ExpertProfileConfiguration : IEntityTypeConfiguration<ExpertProfile>
{
    public void Configure(EntityTypeBuilder<ExpertProfile> builder)
    {
        builder.ToTable("expert_profile");

        builder.HasKey(p => p.UserId);
        builder.Property(p => p.UserId).HasColumnName("user_id").ValueGeneratedNever();
        builder.Property(p => p.Next3Id).HasColumnName("next3_id").HasMaxLength(50);
        builder.Property(p => p.Email).HasColumnName("email").HasMaxLength(256).IsRequired();
        builder.Property(p => p.Active).HasColumnName("active");
        builder.Property(p => p.InactivatedAt).HasColumnName("inactivated_at");

        builder.HasOne<AppUser>().WithOne().HasForeignKey<ExpertProfile>(p => p.UserId);
        // Filtered: next3_id is nullable until the NEXT3 mapping is known; the seed/sync job (#8) matches on it.
        builder.HasIndex(p => p.Next3Id).IsUnique().HasFilter("[next3_id] IS NOT NULL");
    }
}
