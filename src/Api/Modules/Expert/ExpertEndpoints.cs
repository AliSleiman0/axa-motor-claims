using System.Security.Claims;
using Api.Infrastructure;
using Api.Integrations.Next3;
using Api.Modules.Audit;
using Api.Modules.Claims;
using Api.Modules.Media;
using Api.Modules.Users;
using Api.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Expert;

/// <summary>
/// One row of E1 (§5.1). The claim fields are nullable because the cache may be cold — an
/// assignment that arrived while NEXT3 was down is still a real assignment.
/// </summary>
/// <param name="MediaCount">
/// §5.1: "E1 lists assignments newest-first with media counts". Deferred out of slice 2.1 because the
/// `document` table did not exist yet; it does now.
/// </param>
public sealed record ExpertAssignmentListItemDto(
    Guid Id,
    string VisaNo,
    DateTime ReceivedAt,
    DateTime? OpenedAt,
    DateTime? ArrivedAt,
    string? PlateNo,
    string? InsuredName,
    string? CarMakeModel,
    DateOnly? AccidentDate,
    int MediaCount);

/// <summary>The claim as NEXT3 owns it (§6.1), served from the §4 cache.</summary>
public sealed record ClaimDto(
    string VisaNo,
    string PolicyNo,
    string PlateNo,
    string InsuredName,
    string InsuredPhone,
    string CarMakeModel,
    string City,
    DateOnly AccidentDate);

/// <summary>
/// E2 (§5.1). <c>ClaimStatus</c> is "fresh", "stale" or "not_found": stale is §4's staleness banner,
/// and it carries <c>ClaimFetchedAt</c> so the screen can say how old the data is rather than
/// implying it is current.
/// </summary>
public sealed record ExpertAssignmentDetailDto(
    Guid Id,
    string VisaNo,
    DateTime ReceivedAt,
    DateTime? OpenedAt,
    DateTime? ArrivedAt,
    string ClaimStatus,
    DateTime? ClaimFetchedAt,
    ClaimDto? Claim);

/// <summary>
/// What the expert's phone knows about where it is. Nullable so a client that omits them gets this
/// module's own error code rather than the framework's binding failure — and so "the browser refused
/// to give us a position" is a case the endpoint answers rather than a malformed request.
/// </summary>
public sealed record ArrivalRequest(double? Latitude, double? Longitude);

/// <summary>
/// The outcome of pressing Arrived (§5.1). Deliberately not the full
/// <see cref="ExpertAssignmentDetailDto"/>: building that would re-open the claim through
/// <see cref="ClaimCache"/>, which calls NEXT3 and commits its own transaction. The screen refetches
/// E2 instead.
/// </summary>
public sealed record ArrivalDto(DateTime ArrivedAt, double? Latitude, double? Longitude);

/// <summary>
/// The expert's E1/E2 surface (design.md §5.1, §2). Capture and search land in slices 2.5 and 3.2 —
/// there is deliberately no "done" transition here, because the BRD defines no expert-side lifecycle
/// (§1), and Arrived is deliberately not a precondition for anything (§5.1's recorded
/// interpretation: a roadside expert whose GPS is slow must not be blocked from photographing).
/// </summary>
public static class ExpertEndpoints
{
    public static IEndpointRouteBuilder MapExpertEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/expert/assignments").RequireAuthorization(AuthPolicies.Expert);

        group.MapGet("/", async (ClaimsPrincipal principal, AppDbContext db, CancellationToken ct) =>
        {
            var expertUserId = principal.GetUserId();
            if (expertUserId is null)
            {
                return Results.Unauthorized();
            }

            // Scoped to the caller in the query itself, not filtered after the fact: §9's
            // resource-level rule is that an expert sees only their own assignments.
            var items = await db.ExpertAssignments.AsNoTracking()
                .Where(a => a.ExpertUserId == expertUserId.Value)
                .OrderByDescending(a => a.ReceivedAt)
                .GroupJoin(
                    db.CachedClaims.AsNoTracking(),
                    a => a.VisaNo,
                    c => c.VisaNo,
                    (a, claims) => new { Assignment = a, Claims = claims })
                .SelectMany(x => x.Claims.DefaultIfEmpty(), (x, claim) => new ExpertAssignmentListItemDto(
                    x.Assignment.Id,
                    x.Assignment.VisaNo,
                    x.Assignment.ReceivedAt,
                    x.Assignment.OpenedAt,
                    x.Assignment.ArrivedAt,
                    claim == null ? null : claim.PlateNo,
                    claim == null ? null : claim.InsuredName,
                    claim == null ? null : claim.CarMakeModel,
                    claim == null ? null : (DateOnly?)claim.AccidentDate,
                    // Counted in the same statement rather than per row: E1 is the expert's first
                    // screen at a crash site and must not fan out into one query per assignment.
                    db.Documents.Count(d =>
                        d.OwnerKind == DocumentOwnerKinds.Assignment && d.OwnerId == x.Assignment.Id)))
                .ToListAsync(ct);

            return Results.Ok(items);
        });

        group.MapGet("/{id:guid}", async (
            Guid id, ClaimsPrincipal principal, AppDbContext db, ClaimCache claims,
            AuditWriter audit, TimeProvider time, CancellationToken ct) =>
        {
            var expertUserId = principal.GetUserId();
            if (expertUserId is null)
            {
                return Results.Unauthorized();
            }

            var assignment = await db.ExpertAssignments
                .SingleOrDefaultAsync(a => a.Id == id && a.ExpertUserId == expertUserId.Value, ct);

            // Not-found and not-yours are the same 404: an expert must not be able to probe which
            // assignment ids exist by watching 403s come back instead of 404s.
            if (assignment is null)
            {
                return Results.NotFound();
            }

            // Written before the refresh: the expert opened this claim whether or not NEXT3 is
            // reachable, and §5.1 wants that fact recorded.
            if (assignment.OpenedAt is null)
            {
                assignment.OpenedAt = time.GetUtcNow().UtcDateTime;
                audit.Append(
                    expertUserId, AuditActions.AssignmentOpened, AuditEntityKinds.ExpertAssignment,
                    assignment.Id, new { assignment.VisaNo });
                await db.SaveChangesAsync(ct);
            }

            var lookup = await claims.Open(assignment.VisaNo, ct);
            if (lookup.Status == ClaimLookupStatus.Unavailable)
            {
                // Nothing cached and NEXT3 is down: there is genuinely nothing to show, and saying
                // "claim not found" would blame the data for an outage.
                return Results.Json(new { error = "next3_unavailable" }, statusCode: 503);
            }

            return Results.Ok(new ExpertAssignmentDetailDto(
                assignment.Id,
                assignment.VisaNo,
                assignment.ReceivedAt,
                assignment.OpenedAt,
                assignment.ArrivedAt,
                Describe(lookup.Status),
                lookup.Claim?.FetchedAt,
                lookup.Claim is null ? null : ToDto(lookup.Claim)));
        });

        group.MapPost("/{id:guid}/arrival", async (
            Guid id, ArrivalRequest? request, ClaimsPrincipal principal, AppDbContext db,
            OutboxWriter outbox, AuditWriter audit, TimeProvider time, CancellationToken ct) =>
        {
            var expertUserId = principal.GetUserId();
            if (expertUserId is null)
            {
                return Results.Unauthorized();
            }

            // §5.1, verbatim: "arrival without location is not sent — the BRD requires all three
            // values". The screen blocks on a geolocation denial, but the rule lives here as well,
            // for the same reason §7.2 item 5 re-validates images server-side: a client check is UX.
            if (request?.Latitude is not { } latitude || request.Longitude is not { } longitude)
            {
                return Results.BadRequest(new { error = "location_required" });
            }

            // Written as "not inside the range" rather than "outside it" on purpose: NaN fails every
            // comparison, so `is < -90 or > 90` would wave it through and store a coordinate that is
            // not a place.
            if (latitude is not (>= -90 and <= 90) || longitude is not (>= -180 and <= 180))
            {
                return Results.BadRequest(new { error = "invalid_location" });
            }

            var assignment = await db.ExpertAssignments.AsNoTracking()
                .SingleOrDefaultAsync(a => a.Id == id && a.ExpertUserId == expertUserId.Value, ct);

            // The same 404 as E2 for not-found and not-yours.
            if (assignment is null)
            {
                return Results.NotFound();
            }

            if (assignment.ArrivedAt is not null)
            {
                return Already(assignment);
            }

            var arrivedAt = time.GetUtcNow();
            var stamp = arrivedAt.UtcDateTime;

            // The only user-initiated transaction in the codebase, so it is also the only place that
            // needs this. The day someone enables EnableRetryOnFailure — the standard Azure SQL fix
            // for dropped connections, and §10 puts production on Azure SQL — EF refuses to run a
            // hand-rolled transaction outside the strategy and throws on *every* press. Today the
            // strategy is the non-retrying one and this costs a delegate call.
            var strategy = db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                // A retry re-enters this delegate with the same change tracker, so the outbox and
                // audit rows added below would be inserted twice on the second attempt.
                db.ChangeTracker.Clear();

                // ExecuteUpdate issues its own statement immediately, outside SaveChanges, so the
                // transaction has to be explicit — otherwise §4's "the domain row and its outbox row
                // commit in one transaction" would be broken by the first endpoint to depend on it.
                await using var tx = await db.Database.BeginTransactionAsync(ct);

                // The guard is `arrived_at IS NULL` in the WHERE clause, not the `if` above it. Slice
                // 1.5's lesson: a read-then-write on a state column is not a state machine — two
                // simultaneous presses both pass the read and both stamp, and NEXT3 is then told
                // about one arrival twice, at two different times. The `if` is only an early exit.
                var claimed = await db.ExpertAssignments
                    .Where(a => a.Id == id && a.ExpertUserId == expertUserId.Value && a.ArrivedAt == null)
                    .ExecuteUpdateAsync(
                        s => s
                            .SetProperty(a => a.ArrivedAt, (DateTime?)stamp)
                            .SetProperty(a => a.ArrivalLat, (double?)latitude)
                            .SetProperty(a => a.ArrivalLng, (double?)longitude),
                        ct);

                if (claimed == 0)
                {
                    // Lost the race. Nothing was written, so there is no second outbox row and no
                    // second audit row; report the arrival that did happen rather than an error for
                    // something that succeeded, since the expert pressed a button and it worked.
                    // Safe to re-read on this context: the rollback returns the connection to
                    // autocommit, and read-committed cannot see another transaction's uncommitted
                    // stamp — so a row that exists here has a non-null ArrivedAt.
                    await tx.RollbackAsync(ct);
                    var settled = await db.ExpertAssignments.AsNoTracking()
                        .SingleOrDefaultAsync(a => a.Id == id && a.ExpertUserId == expertUserId.Value, ct);
                    return settled is null ? Results.NotFound() : Already(settled);
                }

                // clientRef = the assignment id: stable across retries (§6.3), and an assignment has
                // exactly one arrival, so it is also the right dedupe key on NEXT3's side (#32).
                outbox.EnqueueArrival(
                    assignment.VisaNo,
                    new ArrivalInfo(arrivedAt, latitude, longitude),
                    assignment.Id.ToString());

                // §9: "Arrived presses with coordinates".
                audit.Append(
                    expertUserId, AuditActions.AssignmentArrived, AuditEntityKinds.ExpertAssignment,
                    assignment.Id, new { assignment.VisaNo, Latitude = latitude, Longitude = longitude });

                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                return Results.Ok(new ArrivalDto(stamp, latitude, longitude));
            });
        });

        return app;
    }

    /// <summary>
    /// A press against an assignment that has already arrived. §5.1 disables the button after the
    /// first press, so this is a double-tap or a replayed request: idempotent, never a second stamp.
    /// </summary>
    private static IResult Already(ExpertAssignment assignment) =>
        Results.Ok(new ArrivalDto(assignment.ArrivedAt!.Value, assignment.ArrivalLat, assignment.ArrivalLng));

    private static string Describe(ClaimLookupStatus status) => status switch
    {
        ClaimLookupStatus.Fresh => "fresh",
        ClaimLookupStatus.Stale => "stale",
        _ => "not_found",
    };

    private static ClaimDto ToDto(CachedClaim claim) => new(
        claim.VisaNo,
        claim.PolicyNo,
        claim.PlateNo,
        claim.InsuredName,
        claim.InsuredPhone,
        claim.CarMakeModel,
        claim.City,
        claim.AccidentDate);
}
