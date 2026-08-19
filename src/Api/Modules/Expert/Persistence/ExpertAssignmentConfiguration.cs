using Api.Modules.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Modules.Expert.Persistence;

public sealed class ExpertAssignmentConfiguration : IEntityTypeConfiguration<ExpertAssignment>
{
    public void Configure(EntityTypeBuilder<ExpertAssignment> builder)
    {
        builder.ToTable("expert_assignment");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(a => a.VisaNo).HasColumnName("visa_no").HasMaxLength(50).IsRequired();
        builder.Property(a => a.ExpertUserId).HasColumnName("expert_user_id");
        builder.Property(a => a.Next3AssignmentRef).HasColumnName("next3_assignment_ref")
            .HasMaxLength(100).IsRequired();
        builder.Property(a => a.ReceivedAt).HasColumnName("received_at");
        builder.Property(a => a.NotifiedAt).HasColumnName("notified_at");
        builder.Property(a => a.OpenedAt).HasColumnName("opened_at");
        builder.Property(a => a.ArrivedAt).HasColumnName("arrived_at");
        builder.Property(a => a.ArrivalLat).HasColumnName("arrival_lat");
        builder.Property(a => a.ArrivalLng).HasColumnName("arrival_lng");

        // Restrict, matching broker_request: a future hard delete of an app_user must not silently
        // take an expert's assignment history — and with it the audit answer to "who photographed
        // this claim" — along with it.
        builder.HasOne<AppUser>().WithMany()
            .HasForeignKey(a => a.ExpertUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // §6.2's dedupe rule, enforced by the database rather than by the handler. Two deliveries of
        // the same assignment can be in flight at once (a replayed webhook overlapping a poll), and
        // both would pass a read-then-write check in application code — the 1.5 lesson, which cost
        // a review round on public_link_token. The handler catches the violation and reports a
        // no-op; the index is what makes that true.
        builder.HasIndex(a => a.Next3AssignmentRef).IsUnique();

        // E1: "my assignments, newest first" (§5.1).
        builder.HasIndex(a => new { a.ExpertUserId, a.ReceivedAt });

        // Deliberately no FK to `claim`: an assignment must commit while NEXT3 is unreachable, and
        // §4 makes the cache disposable and deletable at any time. A foreign key would make a
        // throwaway cache load-bearing and turn a NEXT3 outage into lost assignments.
    }
}
