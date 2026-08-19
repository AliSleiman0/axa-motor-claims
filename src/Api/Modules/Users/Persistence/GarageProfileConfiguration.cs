using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Modules.Users.Persistence;

public sealed class GarageProfileConfiguration : IEntityTypeConfiguration<GarageProfile>
{
    public void Configure(EntityTypeBuilder<GarageProfile> builder)
    {
        builder.ToTable("garage_profile");

        builder.HasKey(p => p.UserId);
        builder.Property(p => p.UserId).HasColumnName("user_id").ValueGeneratedNever();
        builder.Property(p => p.ContactName).HasColumnName("contact_name").HasMaxLength(200).IsRequired();
        builder.Property(p => p.Phone).HasColumnName("phone").HasMaxLength(16);
        builder.Property(p => p.Mobile).HasColumnName("mobile").HasMaxLength(16);
        builder.Property(p => p.Email).HasColumnName("email").HasMaxLength(256).IsRequired();
        builder.Property(p => p.Next3Id).HasColumnName("next3_id").HasMaxLength(50);
        builder.Property(p => p.Address).HasColumnName("address").HasMaxLength(300);
        builder.Property(p => p.OpeningHours).HasColumnName("opening_hours").HasMaxLength(200);
        builder.Property(p => p.Active).HasColumnName("active");
        builder.Property(p => p.InactivatedAt).HasColumnName("inactivated_at");

        builder.HasOne<AppUser>().WithOne().HasForeignKey<GarageProfile>(p => p.UserId);
        builder.HasIndex(p => p.Next3Id).IsUnique().HasFilter("[next3_id] IS NOT NULL");
    }
}
