using Api.Infrastructure;
using Api.Infrastructure.Cleanup;
using Api.Modules.Media;
using Api.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Declarations;

/// <summary>
/// design.md §5.2's "known gap, carried": a <c>deferred</c> document on a declaration that already
/// has a visa is by definition stranded, and this is the sweep that un-strands it (slice 7.2).
/// </summary>
/// <remarks>
/// <para>
/// **The window it closes.** A garage's upload reads the declaration's state to decide whether the
/// bucket is allowed, then commits the document row later in the same request. Slice 4.1 narrowed
/// that gap twice — the upload now carries <c>state</c> as a concurrency token, and approve's read of
/// the deferred set moved to after its NEXT3 verification — but the last microseconds between
/// approve's read and its commit cannot be closed from either side. A file that lands in them is
/// written <c>deferred</c> with no outbox row, and the only transition that drains the deferred set
/// has already run.
/// </para>
/// <para>
/// **Why it is worth a sweep rather than a note.** Nothing throws and nothing logs. The document is
/// invisible on A2 (no outbox row to list), structurally excluded from §7.3's first sweep (which
/// requires an outbox row reading <c>sent</c>), and spared by the orphan sweep (a live row still
/// claims the bytes). It is a photograph that silently never reaches AXA — the exact outcome this
/// project exists to remove — and it surfaces weeks later as a support ticket, if at all.
/// </para>
/// <para>
/// **One <c>SaveChanges</c> for the batch, unlike <see cref="DocumentBlobSweeper"/>'s
/// one-per-document.** That task commits per item because it has already deleted bytes by the time it
/// writes the flag, so a batch-wide rollback would discard the record of a physically irreversible
/// act. Nothing here leaves the database: if the save fails, no outbox row and no status flip
/// happened, and the next pass finds exactly the same set. What the single save *does* buy is §4's
/// cross-cutting rule — the outbox row and the <c>push_status</c> flip commit together or not at all.
/// </para>
/// <para>
/// **The claim is <c>push_status</c> itself, as a concurrency token, and it is not optional.**
/// `CleanupWorker` is a hosted service inside the API, and §3 pins `minReplicas: 1` rather than
/// `maxReplicas: 1` — so two replicas whose hourly ticks overlap both select the same stranded rows.
/// A plain read-then-write would have each of them enqueue: the unique index on
/// <c>outbox_message_id</c> catches nothing, because the two writers generate *different* Guids, so
/// it is a lost update rather than a collision. The result is one photograph pushed to NEXT3 twice
/// under a <c>clientRef</c> whose dedupe is still #32, plus an orphan outbox row whose <c>sent</c>
/// licenses no blob deletion. CLAUDE.md's first recurring bug class, found by the db-review, and here
/// the rule was not even in an <c>if</c>. The loser's whole batch rolls back and the next pass finds
/// the rows already <c>queued</c>.
/// </para>
/// <para>
/// Architecture rule 4 holds: <see cref="DeferredDocuments"/> reaches the queue through
/// <see cref="OutboxWriter"/> alone, and no type here names an outbox row.
/// </para>
/// </remarks>
public sealed class StrandedDeferredRequeueTask(AppDbContext db, OutboxWriter outbox) : ICleanupTask
{
    /// <summary>
    /// The same ceiling as the blob sweeps, for the same reason: a pass is a pass, not a migration. A
    /// backlog larger than this drains over successive passes rather than holding one transaction
    /// open across thousands of rows.
    /// </summary>
    private const int BatchSize = DocumentBlobSweeper.BatchSize;

    public string Name => "stranded_deferred";

    public async Task<int> Run(CancellationToken ct)
    {
        // Tracked deliberately — `DeferredDocuments.Queue` mutates `push_status` and
        // `outbox_message_id` on each row, and the entities inside the projection are the tracked
        // instances the save writes back.
        var stranded = await (
            from document in db.Documents
            where document.OwnerKind == DocumentOwnerKinds.Declaration
                && document.PushStatus == DocumentPushStatuses.Deferred
                // Never enqueue a push for bytes that are gone. Unreachable today only because
                // `RejectedDeclarationBlobCleanupTask` — the one sweep that deletes a deferred
                // document's blob — is kept disjoint from this one by declaration state. One
                // predicate makes that structural instead of incidental (db-review).
                && document.BlobDeletedAt == null
            join declaration in db.Declarations on document.OwnerId equals declaration.Id
            where DeclarationStates.LinkedStates.Contains(declaration.State) && declaration.VisaNo != null
            orderby document.CreatedAt
            select new { Document = document, declaration.VisaNo })
            .Take(BatchSize)
            .ToListAsync(ct);

        if (stranded.Count == 0)
        {
            return 0;
        }

        // Grouped by visa because that is what an enqueue takes; ordinal, because a visa number is an
        // identifier matched byte for byte at NEXT3's end and never case-folded on the way (3.1).
        foreach (var claim in stranded.GroupBy(x => x.VisaNo!, StringComparer.Ordinal))
        {
            DeferredDocuments.Queue(outbox, claim.Select(x => x.Document), claim.Key);
        }

        await db.SaveChangesAsync(ct);
        return stranded.Count;
    }
}
