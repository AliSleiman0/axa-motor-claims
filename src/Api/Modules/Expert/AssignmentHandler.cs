using Api.Infrastructure;
using Api.Integrations.Next3;
using Api.Integrations.Push;
using Api.Modules.Audit;
using Api.Modules.Claims;
using Api.Modules.Notifications;
using Api.Modules.Users;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Expert;

/// <summary>
/// The single idempotent handler every assignment is delivered to (design.md §6.2). Webhook, poller
/// and fake all funnel through here, so "a replayed assignment is a no-op" is proved once.
///
/// Write order is design.md §5.1's, and the ordering is the design rather than an accident: the
/// assignment row commits first because it is the only authoritative part. The claim cache and the
/// push are both best-effort on top of it — NEXT3 being unreachable, or a push failing, must never
/// cost an expert the job itself.
/// </summary>
public sealed partial class AssignmentHandler(
    AppDbContext db,
    ClaimCache claims,
    IPushSender push,
    AuditWriter audit,
    TimeProvider time,
    ILogger<AssignmentHandler> logger)
{
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Assignment {Ref} names NEXT3 expert {ExpertNext3Id}, which maps to no profile.")]
    private static partial void LogUnmappedExpert(ILogger logger, string @ref, string expertNext3Id);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Assignment {Ref} recorded, but notifying the expert failed.")]
    private static partial void LogPushFailed(ILogger logger, string @ref, Exception exception);

    public async Task<AssignmentIngestionResult> Handle(AssignmentReceived assignment, CancellationToken ct)
    {
        var expertUserId = await db.ExpertProfiles.AsNoTracking()
            .Where(p => p.Next3Id == assignment.ExpertNext3Id)
            .Select(p => (Guid?)p.UserId)
            .SingleOrDefaultAsync(ct);

        if (expertUserId is null)
        {
            return await RejectUnmapped(assignment, ct);
        }

        var row = await Record(assignment, expertUserId.Value, ct);
        if (row is null)
        {
            // Already seen. Returning before the cache fetch and the push is what makes a replay
            // genuinely free: no second popup on the expert's phone, no second call to NEXT3.
            return AssignmentIngestionResult.DuplicateIgnored;
        }

        // Best effort, and deliberately not awaited into the same transaction: ClaimCache commits
        // its own work, and a failure here is already logged and rendered as §4's staleness banner
        // when the expert opens the claim.
        await claims.Open(assignment.VisaNo, ct);

        await Notify(row, ct);
        return AssignmentIngestionResult.Created;
    }

    private async Task<AssignmentIngestionResult> RejectUnmapped(AssignmentReceived assignment, CancellationToken ct)
    {
        LogUnmappedExpert(logger, assignment.Next3AssignmentRef, assignment.ExpertNext3Id);

        // Actor null (this is NEXT3, not a user) and entity id null (no row was created — the §4
        // nullability recorded in slice 1.3). Dropping it silently would look like a working
        // integration right up until an expert asks why they never got the claim.
        audit.Append(
            null,
            AuditActions.AssignmentUnmappedExpert,
            AuditEntityKinds.ExpertAssignment,
            null,
            new { assignment.VisaNo, assignment.ExpertNext3Id, assignment.Next3AssignmentRef });
        await db.SaveChangesAsync(ct);

        return AssignmentIngestionResult.UnknownExpert;
    }

    /// <summary>Inserts the assignment, or returns null if this NEXT3 ref is already recorded.</summary>
    private async Task<ExpertAssignment?> Record(AssignmentReceived assignment, Guid expertUserId, CancellationToken ct)
    {
        var row = new ExpertAssignment
        {
            Id = Guid.CreateVersion7(),
            VisaNo = assignment.VisaNo,
            ExpertUserId = expertUserId,
            Next3AssignmentRef = assignment.Next3AssignmentRef,
            ReceivedAt = time.GetUtcNow().UtcDateTime,
        };
        db.ExpertAssignments.Add(row);
        audit.Append(
            null,
            AuditActions.AssignmentReceived,
            AuditEntityKinds.ExpertAssignment,
            row.Id,
            new { assignment.VisaNo, assignment.Next3AssignmentRef, ExpertUserId = expertUserId });

        try
        {
            // Assignment row and audit row commit together, so the trail can never claim an
            // assignment that does not exist.
            await db.SaveChangesAsync(ct);
            return row;
        }
        catch (DbUpdateException)
        {
            // The unique index on next3_assignment_ref is the dedupe guard (§6.2) — checking first
            // and inserting second would let two simultaneous deliveries both pass the check.
            // Re-read rather than inspecting SQL error numbers: if the ref is now present, the
            // duplicate is exactly what happened; anything else is a real fault and must surface.
            db.ChangeTracker.Clear();
            var exists = await db.ExpertAssignments.AsNoTracking()
                .AnyAsync(a => a.Next3AssignmentRef == assignment.Next3AssignmentRef, ct);

            if (!exists)
            {
                throw;
            }

            return null;
        }
    }

    private async Task Notify(ExpertAssignment row, CancellationToken ct)
    {
        try
        {
            await push.Send(
                row.ExpertUserId,
                "New claim assigned",
                $"Claim {row.VisaNo} has been assigned to you.",
                NotificationTemplates.AssignmentReceived,
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The send already logged its own `failed` row (§8), and the assignment is recorded and
            // visible in the expert's list. notified_at stays null, which is the honest record:
            // nobody told them yet.
            LogPushFailed(logger, row.Next3AssignmentRef, ex);
            return;
        }

        row.NotifiedAt = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
    }
}
