using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Modules.Users.Persistence;

public sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("app_user", t =>
        {
            t.HasCheckConstraint(
                "CK_app_user_role",
                "[role] IN ('expert', 'garage', 'claim_officer', 'broker', 'admin')");
            t.HasCheckConstraint(
                "CK_app_user_status",
                "[status] IN ('invited', 'active', 'inactive')");
        });

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(u => u.Phone).HasColumnName("phone").HasMaxLength(16).IsRequired();
        builder.Property(u => u.Role).HasColumnName("role").HasMaxLength(20)
            .HasConversion(r => r.ToDbValue(), v => UserRoles.FromDbValue(v));
        builder.Property(u => u.DisplayName).HasColumnName("display_name").HasMaxLength(200).IsRequired();
        builder.Property(u => u.Status).HasColumnName("status").HasMaxLength(10)
            .HasConversion(s => s.ToDbValue(), v => UserStatuses.FromDbValue(v));
        builder.Property(u => u.InactivatedAt).HasColumnName("inactivated_at");
        builder.Property(u => u.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(u => u.Phone).IsUnique();
    }
}
