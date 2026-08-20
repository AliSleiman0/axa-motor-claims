using Api.Infrastructure;
using Api.Infrastructure.Cleanup;
using Api.Integrations.Blob;
using Api.Modules.Audit;
using Api.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Modules.Media;

/// <summary>
/// design.md §7.3's blob lifecycle, and the rule HANDOFF §3 says is "obvious, and reliably broken
/// during a week-6 cleanup refactor":
///
/// > **No blob is deleted before its outbox row is `sent`.** The cleanup job's query joins on
/// > `next3_outbox.status = 'sent'` — structurally, not by convention.
///
/// So the eligibility set is built by joining <see cref="OutboxSentQuery"/> in the database, not by
/// re-deriving "is this sent?" in C# where a later edit can quietly widen it. A `pending` row's blob
/// is not a candidate; a `failed` row's blob is retained **indefinitely**, because it is exactly what
/// A2's Retry re-sends (§5.4).
///
/// Two sweeps, in §7.3's own words:
/// <list type="number">
/// <item>Delete the bytes of documents NEXT3 has confirmed, once <c>sent_at + Retention:BlobDays</c>
/// has passed. The metadata row stays — NEXT3 is the system of record for the file from then on, but
/// "who uploaded which photo, when" (§9) is ours to keep.</item>
/// <item>"A blob without a row is garbage the cleanup job sweeps": anything in the container older
/// than the grace window that no live document row claims. This is the backstop for an upload that
/// died between the blob PUT and its transaction — the failure §7.3 deliberately chose to accept.</item>
/// </list>
/// </summary>
public sealed partial class MediaBlobCleanupTask(
    AppDbContext db,
    IBlobStore blobs,
    OutboxSentQuery sentPushes,
    AuditWriter audit,
    IOptionsMonitor<RetentionOptions> options,
    TimeProvider time,
    ILogger<MediaBlobCleanupTask> logger) : ICleanupTask
{
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Retention delete failed for document {DocumentId} ({BlobKey}); it stays eligible.")]
    private static partial void LogDeleteFailed(
        ILogger logger, Guid documentId, string blobKey, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Orphan-blob delete failed for {BlobKey}; the next sweep retries it.")]
    private static partial void LogOrphanDeleteFailed(ILogger logger, string blobKey, Exception exception);

    /// <summary>
    /// A pass is bounded so one sweep cannot hold the database or the storage account for minutes on
    /// end; the loop simply picks the rest up next time.
    /// </summary>
    private const int BatchSize = 500;

    public string Name => "media_blobs";

    public async Task<int> Run(CancellationToken ct)
    {
        var deleted = await SweepConfirmedPushes(ct);
        deleted += await SweepOrphanBlobs(ct);
        return deleted;
    }

    private async Task<int> SweepConfirmedPushes(CancellationToken ct)
    {
        var settings = options.CurrentValue;
        var now = time.GetUtcNow().UtcDateTime;
        var cutoff = now.AddDays(-settings.BlobDays);

        // One SQL statement: the `Contains` becomes a subquery over next3_outbox. The join is the
        // safety property — a document whose push is not `sent` cannot appear in this set at all.
        var due = await db.Documents
            // Explicit, because this query composes another one in below and a stray AsNoTracking
            // anywhere in that tree makes the whole result untracked — see the remarks on
            // OutboxSentQuery. These rows are edited straight after, so tracking is not optional.
            .AsTracking()
            .Where(d => d.BlobDeletedAt == null
                && d.OutboxMessageId != null
                && sentPushes.MessageIdsSentOnOrBefore(cutoff).Contains(d.OutboxMessageId.Value))
            .OrderBy(d => d.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        var deleted = 0;

        foreach (var document in due)
        {
            // One document per transaction, and the delete wrapped on its own.
            //
            // The batch-wide version of this loop was actively dangerous: a single undeletable blob
            // threw out of the loop, CleanupRunner swallowed it, and every blob_deleted_at *and audit
            // row* already staged in that batch was rolled back — for bytes that were physically gone.
            // Worse, the batch is deterministic, so the same poison row aborted the identical 500
            // deletes on every pass afterwards and retention stalled behind it for ever. Same lesson
            // as the outbox's one-SaveChanges-per-message: a poison item must not take its neighbours
            // down with it.
            try
            {
                await blobs.Delete(document.BlobKey, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // blob_deleted_at stays null, so the row still says truthfully that the bytes are
                // there and the next pass retries. Skipped, not fatal.
                LogDeleteFailed(logger, document.Id, document.BlobKey, ex);
                continue;
            }

            document.BlobDeletedAt = now;
            audit.Append(
                actorUserId: null, AuditActions.DocumentBlobDeleted, AuditEntityKinds.Document,
                document.Id, new { document.BlobKey, Reason = "retention", settings.BlobDays });

            // Committed before moving on, so a later failure cannot erase the record of this one.
            // If *this* save fails the bytes are gone with the row still marked live — the next pass
            // re-issues a delete that is idempotent by contract and sets the flag then.
            await db.SaveChangesAsync(ct);
            deleted++;
        }

        return deleted;
    }

    private async Task<int> SweepOrphanBlobs(CancellationToken ct)
    {
        var settings = options.CurrentValue;
        var graceCutoff = time.GetUtcNow().UtcDateTime.AddHours(-settings.OrphanBlobHours);

        var candidates = (await blobs.List(string.Empty, ct))
            .Where(blob => blob.CreatedAt <= graceCutoff)
            .Take(BatchSize)
            .Select(blob => blob.Key)
            .ToList();

        if (candidates.Count == 0)
        {
            return 0;
        }

        // "Claimed" means a document row that still says its bytes are present. A row whose
        // blob_deleted_at is set has already given the bytes up, so a blob still sitting there — a
        // delete that failed after the flag was written — is garbage this sweep also collects.
        var claimed = await db.Documents
            .Where(d => d.BlobDeletedAt == null && candidates.Contains(d.BlobKey))
            .Select(d => d.BlobKey)
            .ToListAsync(ct);

        var orphans = candidates.Except(claimed, StringComparer.Ordinal).ToList();
        var deleted = 0;

        foreach (var key in orphans)
        {
            try
            {
                await blobs.Delete(key, ct);
                deleted++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Same rule as the retention sweep: one stubborn blob does not stop the others.
                LogOrphanDeleteFailed(logger, key, ex);
            }
        }

        return deleted;
    }
}
