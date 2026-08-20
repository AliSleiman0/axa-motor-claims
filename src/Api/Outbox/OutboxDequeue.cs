using Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Api.Outbox;

/// <summary>
/// design.md §6.3's claim step — the first of the three stored procedures §3 sanctions, because the
/// locking semantics are the whole point and LINQ cannot express them.
///
/// The claim is a single atomic UPDATE ... OUTPUT. That is deliberate and load-bearing: reading
/// `status` and then writing it would be two steps, and two workers would both pass the read. This is
/// the same trap that bit `public_link_token` in slice 1.5, and the answer is the same one — the
/// guard belongs in the schema, not in application code.
/// </summary>
public sealed class OutboxDequeue(AppDbContext db)
{
    /// <summary>Schema-qualified: the app and the migration may connect as different principals.</summary>
    public const string ProcedureName = "dbo.next3_outbox_dequeue";

    /// <summary>
    /// Claims up to <paramref name="batchSize"/> due rows, marking them `processing` and incrementing
    /// their attempt count.
    /// </summary>
    /// <param name="now">
    /// The worker's clock, not the database's. Passing it in is what makes the backoff schedule
    /// testable: tests run on a frozen <c>FakeTimeProvider</c>, so a proc using SYSUTCDATETIME()
    /// internally would compare app-written timestamps against real wall-clock time.
    /// </param>
    /// <param name="leaseSeconds">
    /// How long the claim holds. The claim pushes `next_retry_at` out by this much so a worker that
    /// dies mid-push has its row reclaimed instead of stranded in `processing` forever.
    /// </param>
    /// <param name="maxAttempts">
    /// Rows already at this attempt count whose lease has expired are retired to `failed` rather than
    /// reclaimed: a row that reliably kills its worker would otherwise be re-pushed for ever, because
    /// the give-up rule lives in code that only runs when a worker survives to record an outcome.
    /// </param>
    public Task<List<Next3OutboxMessage>> Claim(
        int batchSize, DateTime now, int leaseSeconds, int maxAttempts, CancellationToken ct) =>
        // The procedure name is a literal, never interpolated: FromSql turns every interpolation hole
        // into a SQL parameter, and a parameter cannot name the procedure being executed. Nothing is
        // composed onto this query either — EF would wrap a composed FromSql in a subquery, which
        // SQL Server rejects around EXEC.
        db.Set<Next3OutboxMessage>()
            .FromSql(
                $"EXEC dbo.next3_outbox_dequeue @batch_size = {batchSize}, @now = {now}, @lease_seconds = {leaseSeconds}, @max_attempts = {maxAttempts}")
            .ToListAsync(ct);
}
