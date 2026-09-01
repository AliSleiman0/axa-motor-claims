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
                "[status] IN ('invited', 'active', 'inactive', 'sync_blocked')");
        });

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(u => u.Phone).HasColumnName("phone").HasMaxLength(16).IsRequired();
        builder.Property(u => u.Role).HasColumnName("role").HasMaxLength(20)
            .HasConversion(r => r.ToDbValue(), v => UserRoles.FromDbValue(v));
        builder.Property(u => u.DisplayName).HasColumnName("display_name").HasMaxLength(200).IsRequired();
        // Concurrency token since slice 7.5, same reasoning as declaration.state and
        // document.push_status: two writers now change this column (AdminUserEndpoints.Deactivate
        // and MasterDataSyncTask), §10 pins minReplicas but not maxReplicas so more than one API
        // replica can run at once, and a read-then-write across either boundary is a lost update
        // with no arbiter otherwise — found by the db-reviewer, not by a test.
        builder.Property(u => u.Status).HasColumnName("status").HasMaxLength(12)
            .HasConversion(s => s.ToDbValue(), v => UserStatuses.FromDbValue(v))
            .IsConcurrencyToken();
        builder.Property(u => u.InactivatedAt).HasColumnName("inactivated_at");
        builder.Property(u => u.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(u => u.Phone).IsUnique();
    }
}
