using Api.Infrastructure;
using Api.Infrastructure.Cleanup;
using Api.Integrations.Blob;
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
    DocumentBlobSweeper sweeper,
    IOptionsMonitor<RetentionOptions> options,
    TimeProvider time,
    ILogger<MediaBlobCleanupTask> logger) : ICleanupTask
{
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Orphan-blob delete failed for {BlobKey}; the next sweep retries it.")]
    private static partial void LogOrphanDeleteFailed(ILogger logger, string blobKey, Exception exception);

    public string Name => "media_blobs";

    public async Task<int> Run(CancellationToken ct)
    {
        var deleted = await SweepConfirmedPushes(ct);
        deleted += await SweepOrphanBlobs(ct);
        return deleted;
    }

    private Task<int> SweepConfirmedPushes(CancellationToken ct)
    {
        var settings = options.CurrentValue;
        var cutoff = time.GetUtcNow().UtcDateTime.AddDays(-settings.BlobDays);

        // One SQL statement: the `Contains` becomes a subquery over next3_outbox. The join is the
        // safety property — a document whose push is not `sent` cannot appear in this set at all.
        var due = db.Documents
            .Where(d => d.BlobDeletedAt == null
                && d.OutboxMessageId != null
                && sentPushes.MessageIdsSentOnOrBefore(cutoff).Contains(d.OutboxMessageId.Value));

        return sweeper.Sweep(
            due, document => new { document.BlobKey, Reason = "retention", settings.BlobDays }, ct);
    }

    private async Task<int> SweepOrphanBlobs(CancellationToken ct)
    {
        var settings = options.CurrentValue;
        var graceCutoff = time.GetUtcNow().UtcDateTime.AddHours(-settings.OrphanBlobHours);

        var candidates = (await blobs.List(string.Empty, ct))
            .Where(blob => blob.CreatedAt <= graceCutoff)
            .Take(DocumentBlobSweeper.BatchSize)
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
