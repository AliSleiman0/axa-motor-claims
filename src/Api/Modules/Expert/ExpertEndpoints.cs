using System.Security.Claims;
using Api.Infrastructure;
using Api.Modules.Audit;
using Api.Modules.Claims;
using Api.Modules.Media;
using Api.Modules.Users;
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
/// The expert's read surface (design.md §5.1, §2's E1/E2). Arrived, capture and search land in
/// slices 2.4, 2.5 and 3.2 — there is deliberately no "done" transition here, because the BRD
/// defines no expert-side lifecycle (§1).
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

        return app;
    }

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
