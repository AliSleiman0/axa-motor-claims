using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Outbox.Persistence;

public sealed class Next3OutboxMessageConfiguration : IEntityTypeConfiguration<Next3OutboxMessage>
{
    public void Configure(EntityTypeBuilder<Next3OutboxMessage> builder)
    {
        // Deliberately no HasTrigger and deliberately no trigger on this table: §6.3's dequeue
        // returns OUTPUT inserted.*, and EF stops using the OUTPUT clause on any table it knows has
        // a trigger. Whatever append-only-style protection this table might seem to want, it cannot
        // have one that way.
        builder.ToTable("next3_outbox", t =>
        {
            t.HasCheckConstraint(
                "CK_next3_outbox_operation",
                "[operation] IN ('upload_document', 'update_arrival', 'push_approval')");
            t.HasCheckConstraint(
                "CK_next3_outbox_status",
                "[status] IN ('pending', 'processing', 'sent', 'failed')");
        });

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id").ValueGeneratedNever();

        // No FK to `claim`: the cache is disposable and deletable at any time (§4), and a NEXT3
        // outage that empties it must never take the queue of pending pushes with it. Same reasoning
        // as expert_assignment's missing FK (slice 2.1).
        builder.Property(m => m.VisaNo).HasColumnName("visa_no").HasMaxLength(50).IsRequired();

        builder.Property(m => m.Operation).HasColumnName("operation").HasMaxLength(30).IsRequired();
        builder.Property(m => m.Payload).HasColumnName("payload").IsRequired();
        builder.Property(m => m.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        // `attempts` doubles as the concurrency token, so every outcome write carries
        // `AND attempts = <the value this worker claimed>`. Without it the lease is unsafe: a row
        // reclaimed after its lease expired is held by two workers, and the slow one's SaveChanges
        // would happily overwrite the new one's — a row already `sent` rewritten to `failed`, or a
        // `failed` row rewritten to `sent` with a timestamp. §7.3 deletes blobs on `sent` and §5.4's
        // A2 lists `failed`, so both the retention rule and the safety net would be acting on a
        // status that does not match what NEXT3 actually received.
        //
        // This needs no new column — a reclaim increments `attempts`, so the claim's attempt number
        // *is* its generation counter. §4's contract stays intact, and unlike `public_link_token`'s
        // `row_version` there is nothing extra to keep in sync.
        builder.Property(m => m.Attempts).HasColumnName("attempts").IsConcurrencyToken();
        builder.Property(m => m.LastError).HasColumnName("last_error");
        builder.Property(m => m.NextRetryAt).HasColumnName("next_retry_at");
        builder.Property(m => m.CreatedAt).HasColumnName("created_at");
        builder.Property(m => m.SentAt).HasColumnName("sent_at");

        // No index: A2 displays this column and never filters or sorts on it (slice 6.2).
        builder.Property(m => m.LastAttemptAt).HasColumnName("last_attempt_at");

        // The dequeue's WHERE clause, and A2's "show me the failed pushes" list (§5.4).
        builder.HasIndex(m => new { m.Status, m.NextRetryAt });

        // "Why is this claim missing photos?" — the support question this whole project exists for.
        builder.HasIndex(m => m.VisaNo);
    }
}
