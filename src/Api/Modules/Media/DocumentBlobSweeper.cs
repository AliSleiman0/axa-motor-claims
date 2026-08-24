using Api.Infrastructure;
using Api.Integrations.Blob;
using Api.Modules.Audit;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Media;

/// <summary>
/// design.md §7.3's retention delete, once (slice 5.2).
///
/// Two sweeps now delete a document's bytes on a schedule — the confirmed-push one (§7.3's first rule)
/// and the broker one (`Retention:BrokerBlobDays`) — and they differ **only in which rows are due**.
/// Everything after that is the same loop, and it is a loop 2.3 got wrong the first time in a way no
/// test caught: a batch-wide commit let one undeletable blob throw out of the iteration and roll back
/// the `blob_deleted_at` and audit rows already staged for bytes that were physically gone, then stall
/// retention behind the same poison row for ever.
///
/// So it lives here rather than being written a second time. A caller supplies the query and the
/// reason; the ordering, the per-document transaction, the per-delete try and the audit row are not
/// theirs to get right.
/// </summary>
public sealed partial class DocumentBlobSweeper(
    AppDbContext db,
    IBlobStore blobs,
    AuditWriter audit,
    TimeProvider time,
    ILogger<DocumentBlobSweeper> logger)
{
    /// <summary>
    /// A pass is bounded so one sweep cannot hold the database or the storage account for minutes on
    /// end; the loop simply picks the rest up next time.
    /// </summary>
    public const int BatchSize = 500;

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Retention delete failed for document {DocumentId} ({BlobKey}); it stays eligible.")]
    private static partial void LogDeleteFailed(
        ILogger logger, Guid documentId, string blobKey, Exception exception);

    /// <param name="due">
    /// The eligible rows, **tracked**. Explicitly so: these rows are edited straight after, and a
    /// stray <c>AsNoTracking</c> anywhere in a composed query tree makes the whole result untracked.
    /// </param>
    /// <param name="detail">Written into the audit row, naming which rule deleted these bytes.</param>
    public async Task<int> Sweep(
        IQueryable<Document> due, Func<Document, object> detail, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(due);
        ArgumentNullException.ThrowIfNull(detail);

        var now = time.GetUtcNow().UtcDateTime;
        var documents = await due.AsTracking().OrderBy(d => d.CreatedAt).Take(BatchSize).ToListAsync(ct);
        var deleted = 0;

        foreach (var document in documents)
        {
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
                document.Id, detail(document));

            // Committed before moving on, so a later failure cannot erase the record of this one.
            // If *this* save fails the bytes are gone with the row still marked live — the next pass
            // re-issues a delete that is idempotent by contract and sets the flag then.
            await db.SaveChangesAsync(ct);
            deleted++;
        }

        return deleted;
    }
}
