using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Modules.Users.Persistence;

public sealed class ClaimOfficerProfileConfiguration : IEntityTypeConfiguration<ClaimOfficerProfile>
{
    public void Configure(EntityTypeBuilder<ClaimOfficerProfile> builder)
    {
        builder.ToTable("claim_officer_profile");

        builder.HasKey(p => p.UserId);
        builder.Property(p => p.UserId).HasColumnName("user_id").ValueGeneratedNever();
        builder.Property(p => p.Next3User).HasColumnName("next3_user").HasMaxLength(50).IsRequired();
        builder.Property(p => p.Email).HasColumnName("email").HasMaxLength(256).IsRequired();

        builder.HasOne<AppUser>().WithOne().HasForeignKey<ClaimOfficerProfile>(p => p.UserId);
    }
}
