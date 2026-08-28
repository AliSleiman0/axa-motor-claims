using System.Security.Claims;
using Api.Infrastructure;
using Api.Integrations.Blob;
using Api.Modules.Claims;
using Api.Modules.Media;
using Api.Modules.Users;
using Api.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Declarations;

/// <summary>What G2 collects. Plate is required (§1); the other two are optional and nothing else exists.</summary>
public sealed record CreateDeclarationRequest(string? PlateNo, string? InsuredName, string? Note);

/// <summary>One row of G1's worklist (§5.2).</summary>
public sealed record DeclarationListItemDto(
    Guid Id,
    string State,
    string PlateNo,
    string? InsuredName,
    string? VisaNo,
    DateTime CreatedAt,
    DateTime? SubmittedAt,
    DateTime? DecidedAt,
    int MediaCount);

/// <summary>
/// G3 (§5.2), and it is state-dependent by design: "the garage view of G3 unlocks the full claim
/// detail … on approval — the declaration is deliberately detail-less until then".
/// </summary>
/// <param name="Claim">
/// Null until the declaration is approved. Before that there is no visa, so there is nothing to fetch;
/// after a rejection there is still no visa, and §5.2 grants the garage no claim detail either.
/// </param>
/// <param name="Comments">
/// **Empty unless the state is `approved`.** The BRD grants comment visibility "in case of
/// confirmation" only, so a rejected declaration shows its status and nothing else (§1). The rows are
/// stored either way — this is a rendering rule, and the scope letter has to warn that it will
/// surprise users.
/// </param>
public sealed record DeclarationDetailDto(
    Guid Id,
    string State,
    string PlateNo,
    string? InsuredName,
    string? Note,
    string? VisaNo,
    DateTime CreatedAt,
    DateTime? SubmittedAt,
    DateTime? DecidedAt,
    DateTime? RepairsStartedAt,
    DateTime? RepairDocsSubmittedAt,
    string? ClaimStatus,
    DateTime? ClaimFetchedAt,
    ClaimDto? Claim,
    IReadOnlyList<DeclarationCommentDto> Comments);

public sealed record DeclarationCommentDto(string Body, DateTime CreatedAt);

/// <summary>
/// The garage's half of design.md §5.2 — G1 (worklist), G2 (new declaration), G3 (detail and the two
/// transitions a garage owns) and the media pipeline underneath them.
///
/// Every read and every write is scoped to the calling garage **inside the query**, and not-found and
/// not-yours are the same bare 404: the 2.x convention, and the reason is the same one E2 gives — a
/// garage must not be able to discover which declaration ids exist by watching the status code change.
///
/// G4's repair submission arrived in slice 5.1: the three repair buckets, the state-dependent upload
/// gate underneath them, and the terminal transition they are the precondition for.
/// </summary>
public static class GarageDeclarationEndpoints
{
    private const int MaxPlateLength = 20;

    public static IEndpointRouteBuilder MapGarageDeclarationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/garage/declarations").RequireAuthorization(AuthPolicies.Garage);

        group.MapPost("/", async (
            CreateDeclarationRequest? request, ClaimsPrincipal principal, DeclarationService declarations,
            CancellationToken ct) =>
        {
            var garageUserId = principal.GetUserId();
            if (garageUserId is null)
            {
                return Results.Unauthorized();
            }

            var plateNo = request?.PlateNo?.Trim();
            if (string.IsNullOrEmpty(plateNo))
            {
                // §1's interpretation of a form the BRD never defines: the plate is the key the
                // officer searches NEXT3 with, so a declaration without one can never be linked to a
                // visa — which is the only thing this flow exists to do.
                return Results.BadRequest(new { error = "plate_required" });
            }

            if (plateNo.Length > MaxPlateLength)
            {
                return Results.BadRequest(new { error = "plate_too_long" });
            }

            var declaration = await declarations.CreateDraft(
                garageUserId.Value,
                plateNo,
                Trimmed(request?.InsuredName),
                Trimmed(request?.Note),
                ct);

            return Results.Created(
                $"/api/garage/declarations/{declaration.Id}",
                ToListItem(declaration, mediaCount: 0));
        });

        group.MapGet("/", async (ClaimsPrincipal principal, AppDbContext db, CancellationToken ct) =>
        {
            var garageUserId = principal.GetUserId();
            if (garageUserId is null)
            {
                return Results.Unauthorized();
            }

            // Newest first, and the media count in the same statement — E1's shape, and the index
            // (garage_user_id, created_at) is there for exactly this.
            var items = await db.Declarations.AsNoTracking()
                .Where(d => d.GarageUserId == garageUserId.Value)
                .OrderByDescending(d => d.CreatedAt)
                // Newest first, so the cap drops the oldest — the right end for a worklist (7.2).
                .Take(ListLimits.MaxRows)
                .Select(d => new DeclarationListItemDto(
                    d.Id,
                    d.State.ToDbValue(),
                    d.PlateNo,
                    d.InsuredName,
                    d.VisaNo,
                    d.CreatedAt,
                    d.SubmittedAt,
                    d.DecidedAt,
                    db.Documents.Count(doc =>
                        doc.OwnerKind == DocumentOwnerKinds.Declaration && doc.OwnerId == d.Id)))
                .ToListAsync(ct);

            return Results.Ok(items);
        });

        group.MapGet("/{id:guid}", async (
            Guid id, ClaimsPrincipal principal, AppDbContext db, ClaimCache claims, CancellationToken ct) =>
        {
            var garageUserId = principal.GetUserId();
            if (garageUserId is null)
            {
                return Results.Unauthorized();
            }

            var declaration = await Find(db, id, garageUserId.Value, ct);
            if (declaration is null)
            {
                return Results.NotFound();
            }

            var approved = declaration.State
                is DeclarationState.Approved
                or DeclarationState.RepairsInProgress
                or DeclarationState.RepairDocsSubmitted;

            // §5.2: comment visibility is granted "in case of confirmation" only. Filtered in the
            // query rather than after it, so a rejected declaration's comments never leave the
            // database at all — a DTO that carries them and hides them is one refactor from leaking.
            var comments = approved
                ? await db.DeclarationComments.AsNoTracking()
                    .Where(c => c.DeclarationId == id)
                    .OrderBy(c => c.CreatedAt)
                    .Select(c => new DeclarationCommentDto(c.Body, c.CreatedAt))
                    .ToListAsync(ct)
                : [];

            // Only once there is a visa, and only once the garage is allowed to see the claim. Before
            // approval `visa_no` is null and there is nothing to open; after a rejection it is still
            // null, which is §5.2's "deliberately detail-less" state holding by construction rather
            // than by an `if` somebody has to remember.
            ClaimLookup? lookup = declaration.VisaNo is { Length: > 0 } visaNo && approved
                ? await claims.Open(visaNo, ct)
                : null;

            return Results.Ok(new DeclarationDetailDto(
                declaration.Id,
                declaration.State.ToDbValue(),
                declaration.PlateNo,
                declaration.InsuredName,
                declaration.Note,
                declaration.VisaNo,
                declaration.CreatedAt,
                declaration.SubmittedAt,
                declaration.DecidedAt,
                declaration.RepairsStartedAt,
                declaration.RepairDocsSubmittedAt,
                lookup?.Status.Describe(),
                lookup?.Claim?.FetchedAt,
                lookup?.Claim is null ? null : ClaimDto.From(lookup.Claim),
                comments));
        });

        group.MapPost("/{id:guid}/submit", async (
            Guid id, ClaimsPrincipal principal, DeclarationService declarations, CancellationToken ct) =>
        {
            var garageUserId = principal.GetUserId();
            return garageUserId is null
                ? Results.Unauthorized()
                : Answer(await declarations.Submit(id, garageUserId.Value, ct));
        });

        group.MapPost("/{id:guid}/start-repairs", async (
            Guid id, ClaimsPrincipal principal, DeclarationService declarations, CancellationToken ct) =>
        {
            var garageUserId = principal.GetUserId();
            return garageUserId is null
                ? Results.Unauthorized()
                : Answer(await declarations.StartRepairs(id, garageUserId.Value, ct));
        });

        // §5.2's last row (G4). Terminal: there is no closure or settlement state, because the BRD
        // defines none and §1 forbids inventing one.
        group.MapPost("/{id:guid}/submit-repair-docs", async (
            Guid id, ClaimsPrincipal principal, DeclarationService declarations, CancellationToken ct) =>
        {
            var garageUserId = principal.GetUserId();
            return garageUserId is null
                ? Results.Unauthorized()
                : Answer(await declarations.SubmitRepairDocs(id, garageUserId.Value, ct));
        });

        MapDocuments(group);

        return app;
    }

    private static void MapDocuments(RouteGroupBuilder group)
    {
        group.MapPost("/{id:guid}/documents", async (
            Guid id, HttpRequest request, ClaimsPrincipal principal, AppDbContext db,
            MediaUploadService uploads, CancellationToken ct) =>
        {
            var garageUserId = principal.GetUserId();
            if (garageUserId is null)
            {
                return Results.Unauthorized();
            }

            // **Tracked, unlike every other read on this group**, and that is the guard rather than a
            // detail. The gate below decides from a snapshot, but the document row commits in a later
            // `SaveChanges` inside the pipeline, so an upload admitted while the declaration was
            // undecided can land *after* the officer's approve transition has already swept the
            // deferred set — a `deferred` row nothing will ever queue: never pushed, absent from A2,
            // excluded from §7.3's sweep 1, blob retained for ever, and nothing thrown. The gate's own
            // rule with a race left in it. So `state` — the same concurrency token every transition
            // uses — is made part of the upload's transaction below, which turns the check into the
            // `UPDATE … WHERE state = @expected` CLAUDE.md asks for instead of an `if`.
            var declaration = await db.Declarations
                .SingleOrDefaultAsync(d => d.Id == id && d.GarageUserId == garageUserId.Value, ct);

            if (declaration is null)
            {
                return Results.NotFound();
            }

            // Marking `state` modified writes it back unchanged, so two concurrent uploads never
            // fight — the token's *value* is what a transition changes — while any transition that
            // commits in between makes this `SaveChanges` match no row and roll the document back.
            db.Entry(declaration).Property(d => d.State).IsModified = true;

            MediaUploadOutcome outcome;
            try
            {
                // **The visa is read off the row, not decided here.** `CK_declaration_decision` makes
                // it exactly the two cases §5.2 needs: null before a decision, and the officer's visa
                // from `approved` onwards. So `RequireVisa` on the Immediate repair buckets is
                // satisfied by the schema rather than by this line remembering to be right, and a
                // pre-decision upload still stores `deferred` with no outbox row for the approve
                // transition to queue later.
                outcome = await uploads.Upload(
                    request,
                    new MediaUploadTarget(
                        DocumentOwnerKinds.Declaration, declaration.Id, declaration.VisaNo, garageUserId),
                    ct,
                    gate: GarageBucketGate(declaration));
            }
            catch (DbUpdateConcurrencyException)
            {
                // The declaration moved while the file was still being read. The blob is written and
                // is now an orphan for §7.3's sweep — the safe failure order — and the document row is
                // not, which is the whole point. Its own code rather than the gate's: the gate refuses
                // a request that arrived too late, this refuses one that *became* too late, and the
                // screen's answer differs — reopen and look, rather than "nothing more can be
                // attached".
                db.ChangeTracker.Clear();
                return Results.Json(
                    new { error = "declaration_changed" }, statusCode: StatusCodes.Status409Conflict);
            }

            return outcome.Document is null
                ? Results.Json(new { error = outcome.ErrorCode }, statusCode: outcome.StatusCode)
                : Results.Created(
                    $"/api/garage/declarations/{id}/documents/{outcome.Document.Id}",
                    DocumentDto.From(outcome.Document));
        });

        group.MapGet("/{id:guid}/documents", async (
            Guid id, ClaimsPrincipal principal, AppDbContext db, OutboxSentQuery sentPushes,
            CancellationToken ct) =>
        {
            var garageUserId = principal.GetUserId();
            if (garageUserId is null)
            {
                return Results.Unauthorized();
            }

            var declaration = await Find(db, id, garageUserId.Value, ct);
            if (declaration is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(await DocumentsFor(db, sentPushes, id, ct));
        });

        // G3's previews (slice 4.2). Scoped to the caller's own declaration first, then to that
        // declaration's own documents — see `DeclarationDocumentContent` for why both halves matter.
        group.MapGet("/{id:guid}/documents/{docId:guid}/content", async (
            Guid id, Guid docId, ClaimsPrincipal principal, AppDbContext db, IBlobStore blobs,
            CancellationToken ct) =>
        {
            var garageUserId = principal.GetUserId();
            if (garageUserId is null)
            {
                return Results.Unauthorized();
            }

            var declaration = await Find(db, id, garageUserId.Value, ct);
            if (declaration is null)
            {
                return Results.NotFound();
            }

            return await DeclarationDocumentContent.Serve(db, blobs, id, docId, ct);
        });
    }

    /// <summary>
    /// The documents on one declaration, newest first — shared by both views (§5.2).
    ///
    /// The `Contains` over <see cref="OutboxSentQuery"/> composes into this one statement, so "has
    /// NEXT3 acknowledged this?" is answered by the database from the outbox row itself rather than
    /// from a denormalised copy of it (§4's rule; the same join §7.3's sweep uses, and the reason
    /// architecture rule 4 survives it).
    /// </summary>
    internal static Task<List<DocumentDto>> DocumentsFor(
        AppDbContext db, OutboxSentQuery sentPushes, Guid declarationId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sentPushes);
        var sent = sentPushes.MessageIdsSent();

        return db.Documents.AsNoTracking()
            .Where(d => d.OwnerKind == DocumentOwnerKinds.Declaration && d.OwnerId == declarationId)
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new DocumentDto(
                d.Id, d.Bucket, d.DocType, d.Origin, d.ClarityResult, d.ContentType,
                d.FileName, d.SizeBytes, d.PushStatus,
                d.OutboxMessageId != null && sent.Contains(d.OutboxMessageId.Value),
                d.BlobDeletedAt == null, d.CreatedAt))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Turns a service outcome into a response. The status and the code are decided where the rule
    /// lives; this only chooses between a body and the bare 404 the ownership convention wants.
    /// </summary>
    internal static IResult Answer(DeclarationOutcome outcome)
    {
        if (outcome.Declaration is not null)
        {
            return Results.Ok(new { state = outcome.Declaration.State.ToDbValue() });
        }

        return outcome.ErrorCode is null
            ? Results.NotFound()
            : Results.Json(new { error = outcome.ErrorCode }, statusCode: outcome.StatusCode);
    }

    /// <summary>
    /// Which of §7.1's declaration buckets this garage may write to, in the state this declaration is
    /// actually in (§5.2, slice 5.1). Three answers, and each refusal says something true:
    ///
    /// <list type="bullet">
    /// <item><b>The pre-decision buckets, while undecided.</b> They are OnApproval, so an upload
    /// writes a `deferred` row with no outbox row and relies on the approve transition to queue it.
    /// Upload after that transition has run and nothing ever will: invisible on A2 (no outbox row to
    /// list), structurally excluded from §7.3's sweep 1, blob retained for ever — a document that
    /// silently never reaches AXA, which is the one outcome this project exists to prevent. Nothing
    /// throws and nothing logs, so it would surface as a support ticket. Note the test is
    /// <c>DecidedAt</c>, not "state is Draft": while an officer is reviewing, a garage may still add
    /// the photo it forgot, and nothing about a `submitted` declaration has been decided yet.</item>
    /// <item><b>G4's repair buckets, at `repairs_in_progress` only.</b> They are Immediate, so each
    /// one queues a push at upload under the declaration's visa — which exists from `approved`
    /// onwards, but before Start Repairs there is no repair to document, and after
    /// `repair_docs_submitted` §5.2 is terminal.</item>
    /// <item><b>Anything else — `approval_image` — refused outright</b>, as in 4.1. It shares this
    /// owner kind, so without this a garage could attach its own "approval" and satisfy the very gate
    /// that exists to make an officer sign the decision (§5.2's `approval_image_required`).
    /// Capture-only would not stop it: `origin` is a claim the client makes.</item>
    /// </list>
    /// </summary>
    private static BucketGate GarageBucketGate(Declaration declaration) => rule =>
    {
        if (MediaBuckets.GarageDeclaration.Contains(rule.Bucket, StringComparer.Ordinal))
        {
            return declaration.DecidedAt is null
                ? null
                : MediaUploadOutcome.Refused(
                    StatusCodes.Status409Conflict, "declaration_already_decided");
        }

        if (MediaBuckets.Repair.Contains(rule.Bucket, StringComparer.Ordinal))
        {
            return declaration.State == DeclarationState.RepairsInProgress
                ? null
                : MediaUploadOutcome.Refused(
                    StatusCodes.Status409Conflict, "repairs_not_in_progress");
        }

        return MediaUploadOutcome.Refused(
            StatusCodes.Status400BadRequest, "bucket_not_allowed_for_caller");
    };

    private static Task<Declaration?> Find(
        AppDbContext db, Guid id, Guid garageUserId, CancellationToken ct) =>
        db.Declarations.AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == id && d.GarageUserId == garageUserId, ct);

    private static DeclarationListItemDto ToListItem(Declaration declaration, int mediaCount) => new(
        declaration.Id,
        declaration.State.ToDbValue(),
        declaration.PlateNo,
        declaration.InsuredName,
        declaration.VisaNo,
        declaration.CreatedAt,
        declaration.SubmittedAt,
        declaration.DecidedAt,
        mediaCount);

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
