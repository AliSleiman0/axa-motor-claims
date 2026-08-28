using System.Linq.Expressions;
using System.Security.Claims;
using Api.Infrastructure;
using Api.Modules.Audit;
using Api.Modules.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Outbox;

/// <summary>
/// One row of §5.4's A2 queue. A projection, deliberately: architecture rule 4 lets no type outside
/// <c>Api.Outbox</c> reference <see cref="Next3OutboxMessage"/>, so what leaves this namespace is
/// primitives — the same discipline as <see cref="OutboxSentQuery"/> handing out ids and
/// <see cref="OutboxWriter"/> returning a Guid rather than the row it just added.
/// </summary>
/// <param name="Status">
/// Raw, not prettified. The screen decides how to render `failed` against a long-`pending` row, and
/// an admin reading a status to a developer needs the string the database holds.
/// </param>
/// <param name="LastAttemptAt">Null until the row has been claimed at least once — A2 shows an em dash.</param>
public sealed record OutboxAdminRowDto(
    Guid Id,
    string Operation,
    string VisaNo,
    string Status,
    int Attempts,
    string? LastError,
    DateTime CreatedAt,
    DateTime? LastAttemptAt,
    DateTime NextRetryAt);

/// <summary>
/// §5.4's A2 failed-push queue: the pushes AXA has not received, and the button that re-sends them.
///
/// **Why this lives in <c>Api.Outbox</c> and not in an admin module.** Architecture rule 4 forbids any
/// namespace outside this one from referencing <see cref="Next3OutboxMessage"/>, so the choice was to
/// weaken the rule or move the surface to the row. The rule is right — it is what stops a feature
/// module hand-rolling a queue row and skipping the one-transaction guarantee — so the surface moved.
/// Nothing about that makes this an integration concern: the group is mounted under
/// <c>/api/admin</c> and gated by <see cref="AuthPolicies.Admin"/> exactly like the profile CRUDs.
/// </summary>
public static class OutboxAdminEndpoints
{
    public static IEndpointRouteBuilder MapOutboxAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/outbox").RequireAuthorization(AuthPolicies.Admin);

        group.MapGet("/", async (
            AppDbContext db, IOptionsMonitor<OutboxOptions> options, TimeProvider time,
            CancellationToken ct) =>
        {
            var rows = await Listed(db, Ahead(options, time), Behind(options, time))
                // Failed first, then oldest first within each: it is a queue somebody works down,
                // not a log somebody scrolls. The two-key sort keeps the rows that will never move
                // on their own above the ones that still might.
                .OrderBy(m => m.Status == Next3OutboxStatuses.Failed ? 0 : 1)
                .ThenBy(m => m.CreatedAt)
                // **The cap is on this chain and deliberately not inside `Listed` (slice 7.2), so
                // this endpoint and Retry all no longer cover exactly the same rows.** 6.2's note
                // above the bulk retry said they did, and that stops being true here: a `Take` in the
                // shared predicate would make the *button* partial, which is far worse than a
                // truncated screen — an admin would press Retry all during an outage, watch it report
                // 200, and be silently left with a backlog it never touched. So the screen truncates
                // and the button keeps the whole predicate. The divergence is stated on the button.
                .Take(ListLimits.MaxRows)
                .Select(m => new OutboxAdminRowDto(
                    m.Id, m.Operation, m.VisaNo, m.Status, m.Attempts, m.LastError,
                    m.CreatedAt, m.LastAttemptAt, m.NextRetryAt))
                .AsNoTracking()
                .ToListAsync(ct);

            return Results.Ok(rows);
        });

        // **Two numbers, and the split is the whole design (slice 7.2).**
        //
        // `failed` is 6.2's badge unchanged: rows that have given up and will sit there for ever.
        // Long-`pending` rows are still deliberately **not** counted — one is on its way and clears
        // itself, and a badge that rose and fell with the 1 min / 5 min / 30 min backoff schedule
        // would be an alarm nobody trusts, which is the argument 6.2 recorded and this slice keeps.
        //
        // `overdue` is the blind spot that argument left open, raised by the db-review and carried
        // here from 6.2's scope-decisions row: a `pending` row due in the *past* and not being
        // claimed is the signature of a stopped worker job, or a backlog draining slower than it
        // fills. Nothing clears it by itself, so it belongs in the badge for exactly the reason a
        // long-pending row does not — and without it the screen built to contradict "nothing has
        // failed" can show a clean queue while nothing at all is being pushed.
        group.MapGet("/count", async (
            AppDbContext db, IOptionsMonitor<OutboxOptions> options, TimeProvider time,
            CancellationToken ct) =>
        {
            var failed = await db.Set<Next3OutboxMessage>()
                .CountAsync(m => m.Status == Next3OutboxStatuses.Failed, ct);

            var overdue = await db.Set<Next3OutboxMessage>()
                .CountAsync(Overdue(Behind(options, time)), ct);

            return Results.Ok(new { failed, overdue });
        });

        group.MapPost("/{id:guid}/retry", async (
            Guid id, ClaimsPrincipal principal, AppDbContext db, AuditWriter audit, TimeProvider time,
            CancellationToken ct) =>
        {
            var now = time.GetUtcNow().UtcDateTime;

            // The guard is the status in the WHERE clause, never an `if` above it (CLAUDE.md's first
            // recurring bug class). This statement and the dequeue's claim are each a single atomic
            // UPDATE, so whichever runs first, the other's predicate stops matching: a claim turns
            // the row `processing`, which this excludes; this one leaves it `pending` and due, which
            // the claim wants anyway. It therefore can never touch a row a worker is holding, and it
            // needs no read first — the parallel test goes red if the status leaves the predicate.
            //
            // `attempts` is deliberately untouched. It is the concurrency token a live worker's
            // outcome write carries, so changing it here would be reaching into somebody else's
            // generation counter; and it is the honest count of how many times this push has been
            // tried, which is the column an admin is reading. The consequence, stated: a `failed` row
            // sits at MaxAttempts, so a retry buys exactly one more attempt rather than a fresh
            // 26-hour schedule. That is the wanted behaviour - a manual retry must not be able to
            // hide the problem for another day and a half.
            var rows = await db.Set<Next3OutboxMessage>()
                .Where(RetryableRow)
                .Where(m => m.Id == id)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(m => m.Status, Next3OutboxStatuses.Pending)
                        .SetProperty(m => m.NextRetryAt, now),
                    ct);

            if (rows == 0)
            {
                // Unknown id, already `sent`, or being pushed right now. All three are "there is
                // nothing here for you to retry", and the screen says so in one sentence.
                return Results.Conflict(new { error = "not_retryable" });
            }

            // `last_error` is deliberately left standing: until the next attempt records an outcome,
            // the last thing that went wrong is still the last thing that went wrong, and blanking it
            // would leave a row on the screen with nothing to say about itself.
            audit.Append(
                principal.GetUserId(), AuditActions.OutboxPushRetried, AuditEntityKinds.Next3Outbox, id);
            await db.SaveChangesAsync(ct);

            return Results.Ok();
        });

        group.MapPost("/retry-all", async (
            ClaimsPrincipal principal, AppDbContext db, AuditWriter audit,
            IOptionsMonitor<OutboxOptions> options, TimeProvider time, CancellationToken ct) =>
        {
            var now = time.GetUtcNow().UtcDateTime;

            // Exactly the list *predicate* — but, since slice 7.2, not exactly the list: the screen
            // stops at `ListLimits.MaxRows` and this does not. That is the deliberate direction of the
            // divergence. The button retries everything the predicate covers, so an outage that
            // backed up three thousand rows is drained by one press rather than by fifteen; a bulk
            // retry that quietly stopped at two hundred would report success and leave the rest, and
            // an admin has no way to see the difference.
            //
            // No confirmation dialog: every push carries a stable clientRef, so re-sending one is
            // meant to be a no-op at NEXT3's end (#32), and an admin looking at this screen during an
            // outage should not have to answer a question first.
            var retried = await Listed(db, Ahead(options, time), Behind(options, time))
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(m => m.Status, Next3OutboxStatuses.Pending)
                        .SetProperty(m => m.NextRetryAt, now),
                    ct);

            // Null entity id: this event is about the queue, not about a row. The count is the fact
            // worth keeping - "somebody retried everything at 09:14" is one decision, and writing
            // thirty rows instead would misrepresent it as thirty.
            audit.Append(
                principal.GetUserId(), AuditActions.OutboxPushesRetried, AuditEntityKinds.Next3Outbox,
                entityId: null, detail: new { Count = retried });
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { retried });
        });

        return app;
    }

    /// <summary>
    /// What A2 shows: everything that has given up, plus everything that is taking long enough that
    /// somebody would otherwise be left wondering where a photograph went.
    ///
    /// Filter only, no ordering — <c>ExecuteUpdateAsync</c> composes onto this for retry-all, and an
    /// ordered source is not a legal update target. One definition rather than three, because the
    /// list, the bulk retry and the tests must agree on what "long pending" means and two
    /// hand-written copies of a predicate eventually disagree.
    ///
    /// <c>processing</c> is never listed: those rows are being pushed right now, and if their worker
    /// dies the lease returns them to the queue by itself (§6.3). Showing them would invite an admin
    /// to act on a row nothing is wrong with.
    /// </summary>
    private static IQueryable<Next3OutboxMessage> Listed(
        AppDbContext db, DateTime ahead, DateTime behind) =>
        db.Set<Next3OutboxMessage>()
            .Where(m => m.Status == Next3OutboxStatuses.Failed
                || (m.Status == Next3OutboxStatuses.Pending
                    && (m.NextRetryAt > ahead || m.NextRetryAt < behind)));

    /// <summary>
    /// A <c>pending</c> row that is **overdue**: due in the past, and still sitting there (slice 7.2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the blind spot 6.2 recorded and did not close. Its predicate listed rows scheduled far
    /// *ahead*; a row whose moment came and went is invisible, which is precisely the shape of a
    /// stopped worker job — the case where the question "where did that photograph go?" is most
    /// urgent and A2 was answering "nothing has failed".
    /// </para>
    /// <para>
    /// **Two polls of grace, and it is load-bearing rather than round.** Every row is enqueued with
    /// <c>next_retry_at = now</c>, so at one poll of grace a queue that is working perfectly would
    /// flash rows overdue in the seconds before the worker's next tick — an alarm that fires during
    /// normal operation is worse than no alarm. Two ticks means the worker has to have missed a beat
    /// entirely. It also cannot collide with the long-pending arm: a claimed row's
    /// <c>next_retry_at</c> is pushed a full <c>LeaseSeconds</c> out and it is <c>processing</c>
    /// anyway, which A2 never lists (§6.3).
    /// </para>
    /// </remarks>
    private static Expression<Func<Next3OutboxMessage, bool>> Overdue(DateTime behind) =>
        m => m.Status == Next3OutboxStatuses.Pending && m.NextRetryAt < behind;

    /// <summary>
    /// One poll ahead (pass-3 decision 2). A <c>pending</c> row due inside the next tick is about to
    /// be tried and is nobody's problem; past that it is waiting out a backoff. The other edge is
    /// <see cref="Behind"/>, added in slice 7.2 — between the two is the window where a row is simply
    /// about to be attempted, and nothing in it is ever shown.
    ///
    /// Stated plainly, because the db-review caught the comment here claiming more than the code
    /// does: the first backoff step is one minute against a thirty-second poll, so a row that has
    /// failed transiently **once** appears as "still trying" almost immediately and drops off again
    /// when its attempt comes due. That is the decision working as written — the row genuinely has
    /// not reached NEXT3 — but the screen is noisier early in a schedule than the "hours" the
    /// long-pending case was argued for. Recorded rather than quietly retuned: the threshold is
    /// <c>Outbox:PollSeconds</c>, and moving it is a decision, not a tidy-up.
    /// </summary>
    private static DateTime Ahead(IOptionsMonitor<OutboxOptions> options, TimeProvider time) =>
        time.GetUtcNow().UtcDateTime.AddSeconds(options.CurrentValue.PollSeconds);

    /// <summary>
    /// Two polls behind — the far edge of <see cref="Overdue"/>. See its remarks for why two.
    /// </summary>
    private static DateTime Behind(IOptionsMonitor<OutboxOptions> options, TimeProvider time) =>
        time.GetUtcNow().UtcDateTime.AddSeconds(-2 * options.CurrentValue.PollSeconds);

    /// <summary>
    /// Retryable means "not moving on its own and not in flight". <c>sent</c> is done; a row being
    /// pushed at this moment belongs to its worker. Retrying an already-`pending` row is deliberately
    /// allowed and is what "Retry now" does to a long-pending one — it pulls the attempt forward.
    /// </summary>
    /// <remarks>
    /// An <see cref="Expression"/> rather than a method, because a method call inside a LINQ tree is
    /// not translatable — EF would throw at the first press rather than at compile time. Composed as
    /// its own <c>Where</c>, which ANDs with the id.
    /// </remarks>
    private static readonly Expression<Func<Next3OutboxMessage, bool>> RetryableRow =
        m => m.Status == Next3OutboxStatuses.Failed || m.Status == Next3OutboxStatuses.Pending;
}
