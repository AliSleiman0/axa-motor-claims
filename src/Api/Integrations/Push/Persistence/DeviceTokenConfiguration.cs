using Api.Infrastructure;
using Api.Modules.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Integrations.Push.Persistence;

public sealed class DeviceTokenConfiguration : IEntityTypeConfiguration<DeviceToken>
{
    public void Configure(EntityTypeBuilder<DeviceToken> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("device_token", t =>
            t.HasCheckConstraint(
                "CK_device_token_platform",
                $"[platform] IN ('{DevicePlatforms.Android}')"));

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(t => t.UserId).HasColumnName("user_id");

        builder.Property(t => t.Token).HasColumnName("token")
            .HasMaxLength(DeviceTokenLimits.TokenLength).IsRequired();
        builder.Property(t => t.TokenHash).HasColumnName("token_hash")
            .HasMaxLength(TokenHashing.HashLength).IsRequired();
        builder.Property(t => t.Platform).HasColumnName("platform")
            .HasMaxLength(DeviceTokenLimits.PlatformLength).IsRequired();
        builder.Property(t => t.CreatedAt).HasColumnName("created_at");
        builder.Property(t => t.LastUsedAt).HasColumnName("last_used_at");
        builder.Property(t => t.RevokedAt).HasColumnName("revoked_at");

        // Restrict, mirroring push_subscription (and expert_assignment, and broker_request): a future
        // hard delete of an app_user must not silently take the record of which devices we notified.
        builder.HasOne<AppUser>().WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // **This index is the upsert**, the same way push_subscription's is, and for the same reason
        // it cannot be an `if` in the endpoint: re-registering the same handset must refresh the row
        // rather than add a second one, and a read-then-write cannot promise that — two POSTs racing
        // (an app relaunch while a retry is in flight, or the shell registering on every resume)
        // both pass the read and both insert. CLAUDE.md's first bug class, sixth slice running. The
        // endpoint catches the violation and updates instead.
        //
        // Scoped to the user rather than global, matching push_subscription: two people sharing a
        // handset each keep their own row, and a global unique index would let whoever registered
        // first quietly own the device.
        builder.HasIndex(t => new { t.UserId, t.TokenHash }).IsUnique();

        // The send path: "every live token for this user". Filtered, because a revoked row is never
        // a send target and there is no other query that wants one.
        builder.HasIndex(t => t.UserId).HasFilter("[revoked_at] IS NULL");
    }
}
