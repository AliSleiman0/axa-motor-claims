using Api.Modules.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Modules.Audit.Persistence;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        // HasTrigger is required: a table with any trigger makes EF fall back from
        // OUTPUT-clause inserts, which the append-only trigger would otherwise break.
        builder.ToTable("audit_log", t => t.HasTrigger("TR_audit_log_append_only"));

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(a => a.ActorUserId).HasColumnName("actor_user_id");
        builder.Property(a => a.Action).HasColumnName("action").HasMaxLength(50).IsRequired();
        builder.Property(a => a.EntityKind).HasColumnName("entity_kind").HasMaxLength(30).IsRequired();
        builder.Property(a => a.EntityId).HasColumnName("entity_id");
        builder.Property(a => a.Detail).HasColumnName("detail");
        builder.Property(a => a.At).HasColumnName("at");

        builder.HasOne<AppUser>().WithMany().HasForeignKey(a => a.ActorUserId);
        // "Who touched this record" support queries.
        builder.HasIndex(a => new { a.EntityKind, a.EntityId });
    }
}
