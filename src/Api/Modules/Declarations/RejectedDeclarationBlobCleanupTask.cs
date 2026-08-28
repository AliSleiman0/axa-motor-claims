using Api.Infrastructure;
using Api.Infrastructure.Cleanup;
using Api.Modules.Media;
using Microsoft.Extensions.Options;

namespace Api.Modules.Declarations;

/// <summary>
/// design.md §7.3's rejected-declaration retention gap, raised in slice 4.1 and deliberately left
/// open until there was a window to key it on (slice 7.2).
/// </summary>
/// <remarks>
/// <para>
/// **Why those blobs live for ever today.** §5.2 makes rejection terminal with no resubmit edge, so a
/// rejected declaration's documents stay <c>deferred</c> and never acquire an outbox row at all.
/// Sweep 1 requires an outbox row reading <c>sent</c>, so it cannot see them; sweep 2 spares any blob
/// a live <c>document</c> row still claims. Neither §7.3's "steady state ~11 GB, flat forever" nor
/// §9's "the app is deliberately not a long-term PII store" survives that — the rows accumulate
/// accident photographs with nothing that will ever move them on.
/// </para>
/// <para>
/// **<c>OutboxMessageId == null</c> is the safety property, and it is not the same test as
/// <c>push_status</c>.** A document that was queued belongs to A2 and to §7.3's first sweep — its
/// bytes are what a Retry re-sends, and deleting them would turn a recoverable failed push into a
/// photograph AXA can never be given. So this task takes only documents that have never entered the
/// pipeline, which is the honest reading of "nothing still needs these bytes". It also keeps the two
/// sweeps disjoint by construction rather than by the order the runner happens to call them in.
/// </para>
/// <para>
/// **Rejected only, not every terminal state.** An approved declaration's documents are queued and
/// pushed, so §7.3's first sweep owns them. An abandoned *draft* is the same shape as a rejection and
/// is deliberately not included, because "abandoned" needs a definition — how long may a draft sit? —
/// that #4/#22 has to supply and a recorded decision does not.
/// </para>
/// <para>
/// The window is a placeholder (<c>Retention:RejectedDeclarationBlobDays</c>, 30 days). What this
/// slice builds is the mechanism; when a rejected claim's photographs are actually deleted is a client
/// answer (#4/#22), and it is one config value away.
/// </para>
/// </remarks>
public sealed class RejectedDeclarationBlobCleanupTask(
    AppDbContext db,
    DocumentBlobSweeper sweeper,
    IOptionsMonitor<RetentionOptions> options,
    TimeProvider time) : ICleanupTask
{
    public string Name => "rejected_declaration_blobs";

    public Task<int> Run(CancellationToken ct)
    {
        var settings = options.CurrentValue;
        var cutoff = time.GetUtcNow().UtcDateTime.AddDays(-settings.RejectedDeclarationBlobDays);

        var due =
            from document in db.Documents
            where document.OwnerKind == DocumentOwnerKinds.Declaration
                && document.BlobDeletedAt == null
                && document.OutboxMessageId == null
            join declaration in db.Declarations on document.OwnerId equals declaration.Id
            where declaration.State == DeclarationState.Rejected && declaration.DecidedAt <= cutoff
            select document;

        return sweeper.Sweep(
            due,
            document => new
            {
                document.BlobKey,
                Reason = "rejected_declaration",
                settings.RejectedDeclarationBlobDays,
            },
            ct);
    }
}
