using System.Security.Claims;
using Api.Infrastructure;
using Api.Modules.Media;
using Api.Modules.Users;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Expert;

/// <summary>
/// E3's write surface (design.md §5.1's "Capture media on E3" row) and its read side.
///
/// The POST is `multipart/form-data` and is **streamed** — see <see cref="MediaUploadService"/> for
/// the ordering rules and for why the metadata parts must arrive before the file part. The endpoint
/// itself only resolves and authorizes the assignment; every §7 rule lives in the media module, so
/// that 2.5's capture component, 5.1's garage flow and 5.3's public page all get the same behaviour
/// rather than three near-copies of it.
/// </summary>
public static class ExpertDocumentEndpoints
{
    public static IEndpointRouteBuilder MapExpertDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/expert/assignments/{id:guid}/documents")
            .RequireAuthorization(AuthPolicies.Expert);

        group.MapPost("/", async (
            Guid id, HttpRequest request, ClaimsPrincipal principal, AppDbContext db,
            MediaUploadService uploads, CancellationToken ct) =>
        {
            var expertUserId = principal.GetUserId();
            if (expertUserId is null)
            {
                return Results.Unauthorized();
            }

            var assignment = await Find(db, id, expertUserId.Value, ct);
            if (assignment is null)
            {
                // Same 404 as E2 for not-found and not-yours: an expert must not be able to discover
                // which assignment ids exist by watching the status code change.
                return Results.NotFound();
            }

            var outcome = await uploads.Upload(
                request,
                new MediaUploadTarget(
                    DocumentOwnerKinds.Assignment, assignment.Id, assignment.VisaNo, expertUserId),
                ct);

            return outcome.Document is null
                ? Results.Json(new { error = outcome.ErrorCode }, statusCode: outcome.StatusCode)
                : Results.Created(
                    $"/api/expert/assignments/{id}/documents/{outcome.Document.Id}",
                    DocumentDto.From(outcome.Document));
        });

        group.MapGet("/", async (
            Guid id, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct) =>
        {
            var expertUserId = principal.GetUserId();
            if (expertUserId is null)
            {
                return Results.Unauthorized();
            }

            var assignment = await Find(db, id, expertUserId.Value, ct);
            if (assignment is null)
            {
                return Results.NotFound();
            }

            var documents = await db.Documents.AsNoTracking()
                .Where(d => d.OwnerKind == DocumentOwnerKinds.Assignment && d.OwnerId == assignment.Id)
                .OrderByDescending(d => d.CreatedAt)
                .Select(d => new DocumentDto(
                    d.Id, d.Bucket, d.DocType, d.Origin, d.ClarityResult, d.ContentType,
                    d.FileName, d.SizeBytes, d.PushStatus, d.BlobDeletedAt == null, d.CreatedAt))
                .ToListAsync(ct);

            return Results.Ok(documents);
        });

        return app;
    }

    private static Task<ExpertAssignment?> Find(
        AppDbContext db, Guid id, Guid expertUserId, CancellationToken ct) =>
        db.ExpertAssignments.AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == id && a.ExpertUserId == expertUserId, ct);
}
