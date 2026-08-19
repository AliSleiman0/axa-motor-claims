using Api.Modules.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Modules.Notifications.Persistence;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notification", t =>
        {
            t.HasCheckConstraint(
                "CK_notification_channel",
                "[channel] IN ('push', 'sms', 'email')");
            t.HasCheckConstraint(
                "CK_notification_status",
                "[status] IN ('queued', 'sent', 'failed')");
        });

        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(n => n.Channel).HasColumnName("channel").HasMaxLength(10).IsRequired();
        builder.Property(n => n.RecipientUserId).HasColumnName("recipient_user_id");
        // 320 = the practical max for an email address; phones and user ids fit comfortably.
        builder.Property(n => n.RecipientAddress).HasColumnName("recipient_address").HasMaxLength(320).IsRequired();
        builder.Property(n => n.Template).HasColumnName("template").HasMaxLength(50).IsRequired();
        builder.Property(n => n.Payload).HasColumnName("payload");
        builder.Property(n => n.Status).HasColumnName("status").HasMaxLength(10).IsRequired();
        builder.Property(n => n.SentAt).HasColumnName("sent_at");
        builder.Property(n => n.Error).HasColumnName("error");
        builder.Property(n => n.CreatedAt).HasColumnName("created_at");

        // No cascade: a deactivated user's send history must survive (§9 audit story). Deletion of
        // app_user rows is not a supported operation anyway (slice 1.3: no hard-delete).
        builder.HasOne<AppUser>().WithMany().HasForeignKey(n => n.RecipientUserId);
        // "What did we send this user / when did sends start failing" support queries.
        builder.HasIndex(n => new { n.RecipientUserId, n.CreatedAt });
        builder.HasIndex(n => n.Status);
    }
}
