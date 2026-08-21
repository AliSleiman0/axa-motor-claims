using System.Security.Claims;
using Api.Infrastructure;
using Api.Integrations.Next3;
using Api.Modules.Media;
using Api.Modules.Users;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Declarations;

/// <summary>One row of O1's inbox (§5.2), with the garage contact the officer needs to chase it.</summary>
public sealed record OfficerDeclarationListItemDto(
    Guid Id,
    string State,
    string PlateNo,
    string? InsuredName,
    string? GarageName,
    string? GarageEmail,
    string? GaragePhone,
    DateTime CreatedAt,
    DateTime? SubmittedAt,
    int MediaCount);

/// <summary>O2's review screen (§5.2). The officer sees everything — including the garage's note.</summary>
public sealed record OfficerDeclarationDetailDto(
    Guid Id,
    string State,
    string PlateNo,
    string? InsuredName,
    string? Note,
    string? VisaNo,
    DateTime CreatedAt,
    DateTime? SubmittedAt,
    DateTime? DecidedAt,
    string? GarageName,
    string? GarageEmail,
    string? GaragePhone,
    IReadOnlyList<DeclarationCommentDto> Comments);

/// <summary>One NEXT3 search hit (§6.1's claim search), as O2's "use this visa" table renders it.</summary>
public sealed record ClaimSearchResultDto(string VisaNo, string PlateNo, string InsuredName);

public sealed record ApproveDeclarationRequest(string? VisaNo, string? Comment);

public sealed record RejectDeclarationRequest(string? Comment);

/// <summary>
/// The claim officer's half of design.md §5.2 — O1 (inbox) and O2 (review, visa search, approve or
/// reject).
///
/// **Nothing here is ownership-scoped, and that is the design**: §5.2 says "no per-officer assignment
/// — any officer may pick it up; the BRD defines no queueing and we do not invent one". The policy on
/// the group is the whole authorization story.
/// </summary>
public static class OfficerEndpoints
{
    public static IEndpointRouteBuilder MapOfficerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/officer").RequireAuthorization(AuthPolicies.ClaimOfficer);

        group.MapGet("/declarations", async (
            string? state, AppDbContext db, CancellationToken ct) =>
        {
            // Defaults to the inbox §5.2 describes — the ones actually waiting on an officer. An
            // explicit `state` widens it to any single state so O1 can show a decided declaration
            // back, but an unrecognised value is refused rather than silently returning everything:
            // a typo that quietly showed drafts from every garage would be a disclosure, not a bug.
            var wanted = state?.Trim();
            DeclarationState filter;
            try
            {
                filter = string.IsNullOrEmpty(wanted)
                    ? DeclarationState.Submitted
                    : DeclarationStates.FromDbValue(wanted);
            }
            catch (ArgumentOutOfRangeException)
            {
                return Results.BadRequest(new { error = "unknown_state" });
            }

            // Oldest first, deliberately unlike every other list in the app: an officer works a queue,
            // so the declaration that has been waiting longest is the one they should see first. The
            // (state, submitted_at) index serves exactly this.
            var items = await db.Declarations.AsNoTracking()
                .Where(d => d.State == filter)
                .OrderBy(d => d.SubmittedAt)
                .ThenBy(d => d.CreatedAt)
                .GroupJoin(
                    db.GarageProfiles.AsNoTracking(),
                    declaration => declaration.GarageUserId,
                    profile => profile.UserId,
                    (declaration, profiles) => new { Declaration = declaration, Profiles = profiles })
                .SelectMany(
                    x => x.Profiles.DefaultIfEmpty(),
                    (x, profile) => new OfficerDeclarationListItemDto(
                        x.Declaration.Id,
                        x.Declaration.State.ToDbValue(),
                        x.Declaration.PlateNo,
                        x.Declaration.InsuredName,
                        profile == null ? null : profile.ContactName,
                        profile == null ? null : profile.Email,
                        profile == null ? null : (profile.Mobile ?? profile.Phone),
                        x.Declaration.CreatedAt,
                        x.Declaration.SubmittedAt,
                        db.Documents.Count(doc =>
                            doc.OwnerKind == DocumentOwnerKinds.Declaration
                            && doc.OwnerId == x.Declaration.Id)))
                .ToListAsync(ct);

            return Results.Ok(items);
        });

        group.MapGet("/declarations/{id:guid}", async (
            Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var declaration = await db.Declarations.AsNoTracking()
                .SingleOrDefaultAsync(d => d.Id == id, ct);

            if (declaration is null)
            {
                return Results.NotFound();
            }

            var garage = await db.GarageProfiles.AsNoTracking()
                .SingleOrDefaultAsync(p => p.UserId == declaration.GarageUserId, ct);

            // The officer sees every comment in every state — they wrote them, and O2 is where a
            // second officer reads what a first one said. §5.2's visibility rule constrains the
            // *garage* view only.
            var comments = await db.DeclarationComments.AsNoTracking()
                .Where(c => c.DeclarationId == id)
                .OrderBy(c => c.CreatedAt)
                .Select(c => new DeclarationCommentDto(c.Body, c.CreatedAt))
                .ToListAsync(ct);

            return Results.Ok(new OfficerDeclarationDetailDto(
                declaration.Id,
                declaration.State.ToDbValue(),
                declaration.PlateNo,
                declaration.InsuredName,
                declaration.Note,
                declaration.VisaNo,
                declaration.CreatedAt,
                declaration.SubmittedAt,
                declaration.DecidedAt,
                garage?.ContactName,
                garage?.Email,
                garage?.Mobile ?? garage?.Phone,
                comments));
        });

        group.MapGet("/claims/search", async (
            string? plateNo, string? visaNo, INext3Client next3, CancellationToken ct) =>
        {
            var plate = Trimmed(plateNo);
            var visa = Trimmed(visaNo);

            if (plate is null && visa is null)
            {
                // §6.1: "neither term supplied returns empty, never every claim". Refused at the edge
                // so the rule does not depend on the client — or on the far end being well behaved.
                return Results.BadRequest(new { error = "search_terms_required" });
            }

            try
            {
                // §6.1's claim search — the officer's visa lookup, and its **first production
                // caller**: 3.2 removed the expert's, because a NEXT3-wide search there would have
                // shown an expert claims they were never assigned. Here it is exactly the point: the
                // officer is looking for a visa nobody has linked yet.
                //
                // Architecture rule 3 permits this. It is IL-level and names only RecordArrival and
                // UploadDocument — reads are legitimate feature-code calls.
                var results = await next3.SearchClaims(plate, visa, ct);

                return Results.Ok(results
                    .Select(c => new ClaimSearchResultDto(c.VisaNo, c.PlateNo, c.InsuredName))
                    .ToList());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // NEXT3 being unreachable is not the officer's fault and not a missing claim. 503
                // says "try again", which is exactly right: unlike the garage's upload, this lookup
                // has nothing to queue and nothing is lost by waiting.
                return Results.Json(new { error = "next3_unavailable" }, statusCode: 503);
            }
        });

        group.MapPost("/declarations/{id:guid}/approve", async (
            Guid id, ApproveDeclarationRequest? request, ClaimsPrincipal principal,
            DeclarationService declarations, CancellationToken ct) =>
        {
            var officerUserId = principal.GetUserId();
            if (officerUserId is null)
            {
                return Results.Unauthorized();
            }

            var visaNo = Trimmed(request?.VisaNo);
            if (visaNo is null)
            {
                return Results.BadRequest(new { error = "visa_required" });
            }

            return GarageDeclarationEndpoints.Answer(
                await declarations.Approve(id, officerUserId.Value, visaNo, request?.Comment, ct));
        });

        group.MapPost("/declarations/{id:guid}/reject", async (
            Guid id, RejectDeclarationRequest? request, ClaimsPrincipal principal,
            DeclarationService declarations, CancellationToken ct) =>
        {
            var officerUserId = principal.GetUserId();
            if (officerUserId is null)
            {
                return Results.Unauthorized();
            }

            return GarageDeclarationEndpoints.Answer(
                await declarations.Reject(id, officerUserId.Value, request?.Comment, ct));
        });

        MapDocuments(group);

        return app;
    }

    private static void MapDocuments(RouteGroupBuilder group)
    {
        group.MapPost("/declarations/{id:guid}/documents", async (
            Guid id, HttpRequest request, ClaimsPrincipal principal, AppDbContext db,
            MediaUploadService uploads, CancellationToken ct) =>
        {
            var officerUserId = principal.GetUserId();
            if (officerUserId is null)
            {
                return Results.Unauthorized();
            }

            var declaration = await db.Declarations.AsNoTracking()
                .SingleOrDefaultAsync(d => d.Id == id, ct);

            if (declaration is null)
            {
                return Results.NotFound();
            }

            // Same rule as the garage side, same reason: an OnApproval bucket uploaded after the
            // approve transition has run is a `deferred` row nothing will ever queue — retained for
            // ever, absent from A2, and silently never delivered to AXA.
            if (declaration.DecidedAt is not null)
            {
                return Results.Json(
                    new { error = "declaration_already_decided" }, statusCode: StatusCodes.Status409Conflict);
            }

            // The officer's only write to the media pipeline is #18's approval image. The allow-list
            // goes into the pipeline rather than being checked on the way out, because the bucket
            // arrives inside a streamed body: checking afterwards would mean the blob and the row were
            // already written and had to be unpicked, and an unpicking that half-fails leaves exactly
            // the orphan §7.3 spends a sweep collecting.
            var outcome = await uploads.Upload(
                request,
                new MediaUploadTarget(DocumentOwnerKinds.Declaration, id, VisaNo: null, officerUserId),
                ct,
                allowedBuckets: [MediaBuckets.ApprovalImage]);

            if (outcome.Document is null)
            {
                return Results.Json(new { error = outcome.ErrorCode }, statusCode: outcome.StatusCode);
            }

            return Results.Created(
                $"/api/officer/declarations/{id}/documents/{outcome.Document.Id}",
                DocumentDto.From(outcome.Document));
        });

        group.MapGet("/declarations/{id:guid}/documents", async (
            Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var exists = await db.Declarations.AsNoTracking().AnyAsync(d => d.Id == id, ct);
            return exists
                ? Results.Ok(await GarageDeclarationEndpoints.DocumentsFor(db, id, ct))
                : Results.NotFound();
        });
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
