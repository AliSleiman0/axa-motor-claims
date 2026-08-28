using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Modules.Media.Persistence;

public sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("document", table =>
        {
            table.HasCheckConstraint(
                "CK_document_owner_kind",
                $"[owner_kind] IN ('{DocumentOwnerKinds.Assignment}', '{DocumentOwnerKinds.Declaration}', "
                + $"'{DocumentOwnerKinds.BrokerRequest}')");

            // Every bucket §7.1 currently defines. Adding one is a migration on purpose: a bucket is
            // a rule about capture-only enforcement, not a free-text label.
            table.HasCheckConstraint(
                "CK_document_bucket",
                $"[bucket] IN ({string.Join(", ", MediaBuckets.All.Order(StringComparer.Ordinal).Select(b => $"'{b}'"))})");

            table.HasCheckConstraint(
                "CK_document_origin",
                $"[origin] IN ('{DocumentOrigins.Captured}', '{DocumentOrigins.Uploaded}')");

            table.HasCheckConstraint(
                "CK_document_push_status",
                $"[push_status] IN ({Quoted(DocumentPushStatuses.All)})");

            table.HasCheckConstraint(
                "CK_document_clarity_result",
                $"[clarity_result] IN ('{ClarityResults.Passed}', '{ClarityResults.NotApplicable}')");

            // The cross-column invariant, in the schema rather than in the upload service's head.
            // `queued` with no outbox row is a document that is never pushed, never appears on A2
            // (there is no row for A2 to list) and never becomes eligible for deletion — retained
            // for ever and invisible everywhere. `n/a` with an outbox row is a broker document that
            // quietly went to NEXT3. Both are unrepresentable.
            //
            // Widened in slice 4.1 for `deferred` (§5.2), which sits on the same side as `n/a`: it has
            // no outbox row *yet*, because the visa it would be addressed to does not exist until the
            // officer approves. That is what makes "nothing goes to NEXT3 before approval" structural
            // — a deferred document that had somehow queued a push would fail this constraint at
            // SaveChanges rather than surfacing as a photo filed under the wrong claim.
            table.HasCheckConstraint(
                "CK_document_push_status_outbox",
                $"([push_status] = '{DocumentPushStatuses.Queued}' AND [outbox_message_id] IS NOT NULL) "
                + $"OR ([push_status] IN ({Quoted(DocumentPushStatuses.WithoutOutboxRow)}) "
                + "AND [outbox_message_id] IS NULL)");
        });

        builder.HasKey(d => d.Id);

        // Client-generated, like every other key in the model: the blob key and the outbox clientRef
        // are both derived from this id *before* the row is built, so letting EF substitute its own
        // would produce a document whose id matches neither its bytes nor its NEXT3 reference.
        builder.Property(d => d.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(d => d.OwnerKind).HasColumnName("owner_kind").HasMaxLength(20).IsRequired();
        builder.Property(d => d.OwnerId).HasColumnName("owner_id").IsRequired();
        builder.Property(d => d.Bucket).HasColumnName("bucket").HasMaxLength(40).IsRequired();
        builder.Property(d => d.DocType).HasColumnName("doc_type").HasMaxLength(50);
        builder.Property(d => d.Origin).HasColumnName("origin").HasMaxLength(10).IsRequired();
        builder.Property(d => d.ClarityResult).HasColumnName("clarity_result").HasMaxLength(20).IsRequired();
        builder.Property(d => d.BlobKey).HasColumnName("blob_key").HasMaxLength(400).IsRequired();
        builder.Property(d => d.ContentType).HasColumnName("content_type").HasMaxLength(100).IsRequired();

        // 128 is the length SafeFileName already truncates to, so the column cannot be the thing that
        // rejects a name the upload path was willing to accept.
        builder.Property(d => d.FileName).HasColumnName("file_name").HasMaxLength(128);
        builder.Property(d => d.SizeBytes).HasColumnName("size_bytes").IsRequired();
        // **The concurrency token, added in slice 7.2 for `StrandedDeferredRequeueTask`.** That sweep
        // reads a set of `deferred` documents and flips them to `queued`, and `CleanupWorker` runs
        // inside the API host, which §10 does not pin to one replica — so without a token two passes
        // both enqueue the same photograph. The unique index on `outbox_message_id` cannot catch it:
        // each writer generates its own Guid, so it is a lost update rather than a collision.
        //
        // `push_status` rather than a `rowversion`, on 4.1's reasoning for `declaration.state`: a
        // token applies to *every* write of the entity, so the question is whether there is an
        // innocent one to punish. There is not. The only other writers of a document row are the
        // approve transition, which changes this very column and wants exactly this guard, and
        // `DocumentBlobSweeper`, whose two sets are disjoint from the deferred one — sweep 1 requires
        // an outbox row and the rejected-declaration sweep requires a rejected declaration, which is
        // never a linked one. No DDL: a concurrency token is a predicate EF adds to the UPDATE.
        builder.Property(d => d.PushStatus).HasColumnName("push_status").HasMaxLength(10)
            .IsRequired().IsConcurrencyToken();
        builder.Property(d => d.OutboxMessageId).HasColumnName("outbox_message_id");
        builder.Property(d => d.BlobDeletedAt).HasColumnName("blob_deleted_at");
        builder.Property(d => d.CreatedBy).HasColumnName("created_by");
        builder.Property(d => d.CreatedAt).HasColumnName("created_at").IsRequired();

        // No FK on owner_id: it is polymorphic (§4's owner_kind decides the table).
        // No FK on outbox_message_id either — a relationship would put Next3OutboxMessage into this
        // module's model configuration, which architecture rule 4 forbids, and the id is only ever
        // read by §7.3's cleanup join.

        // E1's media counts and the per-assignment document list (§5.1).
        builder.HasIndex(d => new { d.OwnerKind, d.OwnerId });

        // §7.3's cleanup sweep, and the structural half of its safety rule.
        //
        // **Unique**, because one confirmed push must license the deletion of exactly one document's
        // bytes. Two rows sharing an outbox_message_id would both be swept when that single push
        // landed — bytes destroyed for a file NEXT3 never received, which is the precise outcome §7.3
        // exists to make impossible. The filter is `IS NOT NULL` only: adding `blob_deleted_at IS
        // NULL` would make uniqueness stop being enforced the moment a row is swept.
        //
        // Covering, because the sweep orders on created_at and projects blob_key; without the
        // includes each pass is a sort plus 500 key lookups.
        builder.HasIndex(d => d.OutboxMessageId)
            .IsUnique()
            .HasFilter("[outbox_message_id] IS NOT NULL")
            .IncludeProperties(d => new { d.CreatedAt, d.BlobKey });

        // The orphan sweep's other side: "does a live row claim this key?", run against a batch of
        // keys every pass. Not unique — a fresh GUID per document makes collisions impossible anyway,
        // and a uniqueness violation would surface only after the blob had already been written.
        builder.HasIndex(d => d.BlobKey).HasFilter("[blob_deleted_at] IS NULL");

        // Slice 7.2's stranded-deferred re-queue, which runs hourly and normally returns nothing.
        // Without it that pass reads every declaration document ever written — a set that grows with
        // the product and never shrinks — to find the handful that were admitted a microsecond before
        // an approval committed. Filtered on the status rather than keyed on it, because `deferred` is
        // the rare value and the whole point of the query is that the answer is usually empty.
        // The named overload, not HasDatabaseName: EF treats two indexes over the same properties as
        // one definition and would have *replaced* the unfiltered IX_document_owner_kind_owner_id
        // above — silently taking E1's media counts and every per-owner document list off an index
        // and onto a scan. Caught by reading the generated migration, which is why the skill says to.
        builder.HasIndex(d => new { d.OwnerKind, d.OwnerId }, "IX_document_deferred")
            .HasFilter($"[push_status] = '{DocumentPushStatuses.Deferred}'");

        // **One photograph per car side** (§5.3, slice 6.1) — in the schema, because CLAUDE.md's
        // first recurring bug class is that "this may only happen once" belongs in the schema or the
        // WHERE and never in an `if`.
        //
        // A customer who retakes the front shot replaces it (`PublicEndpoints` stages the delete into
        // the same transaction as the new row). Without this index that replacement would be a
        // convention: a race, a retry or a later edit that forgot it leaves two `public_car_front`
        // rows, and then **both** reach AXA as attachments — `BrokerRequestEmail.Attachments` selects
        // on the owner alone — with nothing on the email to say which is current. Duplicates also
        // consume §9.1's `MaxFiles`, and there is no delete route on `/public/*`, so enough retakes
        // would leave a request nobody can send and nobody can repair.
        //
        // Filtered to the five, generated from `MediaBuckets.PublicCarShots` for the same reason the
        // bucket check constraint is generated from `MediaBuckets.All`: a sixth side must not be able
        // to arrive without this rule following it. Every other bucket is deliberately outside the
        // filter — a request may carry many supporting documents, and an assignment many photographs.
        builder.HasIndex(d => new { d.OwnerKind, d.OwnerId, d.Bucket })
            .IsUnique()
            .HasFilter(
                "[bucket] IN ("
                + string.Join(
                    ", ",
                    MediaBuckets.PublicCarShots.Order(StringComparer.Ordinal).Select(b => $"'{b}'"))
                + ")");
    }

    /// <summary>
    /// Renders a status list as SQL literals. Ordinal-ordered so the constraint text is stable
    /// between runs — an unordered set would regenerate a different string each build and make every
    /// migration diff look like a schema change.
    /// </summary>
    private static string Quoted(IEnumerable<string> values) =>
        string.Join(", ", values.Order(StringComparer.Ordinal).Select(v => $"'{v}'"));
}
