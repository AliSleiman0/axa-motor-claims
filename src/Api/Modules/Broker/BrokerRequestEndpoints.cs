using System.Security.Claims;
using Api.Infrastructure;
using Api.Integrations.Blob;
using Api.Modules.Media;
using Api.Modules.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Modules.Broker;

/// <summary>What B2 collects — design.md §5.3's six fields, and nothing the BRD does not name.</summary>
public sealed record CreateBrokerRequestDto(
    string? InsuredName,
    string? InsuranceType,
    string? InsuredAddress,
    decimal? CarValue,
    decimal? EstimatedPremium,
    DateOnly? EffectiveDate);

/// <summary>One row of B1's worklist (§5.3).</summary>
/// <param name="State">
/// The stored state, **except** that a lapsed Option 2 link renders as `expired`. That value is
/// computed in the projection below and never written: §9.1 makes the token's own `expires_at` the
/// authority, and a state column that had to be swept into agreement with it would be a second answer
/// that can disagree with the one the public page enforces.
/// </param>
/// <param name="EmailedAt">
/// Null on a submitted row means the routed email has not gone (§5.3's ordering). B1 shows that and
/// offers Resend, rather than reporting a delivery that did not happen.
/// </param>
public sealed record BrokerRequestListItemDto(
    Guid Id,
    int Option,
    string State,
    string? InsuredName,
    string? InsuranceType,
    string? CustomerMobile,
    DateTime CreatedAt,
    DateTime? SubmittedAt,
    DateTime? EmailedAt,
    string? EmailRecipient,
    DateTime? LinkExpiresAt,
    int DocumentCount);

/// <summary>
/// What B2 and B3 need from Appendix A's `Broker` section (slice 5.2).
///
/// It exists for the same reason `GET /api/config/media` does: the insurance types are #14's
/// placeholder list and the routing they drive is #13's, so a list written into TypeScript would be a
/// client literal outside the placeholder file **and** a second source of truth that drifts from the
/// values the server validates against. Read through <c>IOptionsMonitor</c>, so an edit reaches the
/// next page load.
///
/// Behind the broker policy rather than anonymous: unlike the media thresholds, which §5.3's public
/// page needs with no token, nothing unauthenticated needs this today. 5.3 decides what P1 sees.
/// </summary>
/// <param name="EmailRouting">
/// #13's routing table (slice 5.3). B4 has to name the desk a submission will go to **before** the
/// broker presses Send, which is the B4 artboard's own argument: "the address is shown, not hidden
/// behind 'sent successfully' — if the routing table is wrong, this is the screen where somebody
/// notices". `broker_request.email_recipient` cannot answer it, because the send is what writes it.
///
/// Behind the broker policy, like the type list beside it. These are placeholder addresses today
/// (#13) and the server remains the only thing that resolves a recipient for real — this is what the
/// screen displays, never what it sends to.
/// </param>
public sealed record BrokerConfigDto(
    IReadOnlyList<string> InsuranceTypes,
    IReadOnlyDictionary<string, string> EmailRouting);

/// <summary>B2's detail, and B3's "Afterwards" card. The raw link is never here — only its expiry.</summary>
public sealed record BrokerRequestDetailDto(
    Guid Id,
    int Option,
    string State,
    string? InsuredName,
    string? InsuranceType,
    string? InsuredAddress,
    decimal? CarValue,
    decimal? EstimatedPremium,
    DateOnly? EffectiveDate,
    string? CustomerMobile,
    DateTime CreatedAt,
    DateTime? SubmittedAt,
    DateTime? EmailedAt,
    string? EmailRecipient,
    DateTime? LinkExpiresAt);

/// <summary>
/// B1, B2 and the Option 1 pipeline of design.md §5.3 (slice 5.2).
///
/// **Ownership is on every route, and a request that is not the caller's is a 404** rather than a 403:
/// a 403 confirms the id exists, and these ids appear in URLs a broker can share.
///
/// Nothing here touches NEXT3 — no client, no outbox row. That is the module's defining property
/// (§5.3) and it is what keeps architecture rules 3 and 4 green without a new rule.
/// </summary>
public static class BrokerRequestEndpoints
{
    public static IEndpointRouteBuilder MapBrokerRequestEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/broker/config", (IOptionsMonitor<BrokerOptions> options) =>
                Results.Ok(new BrokerConfigDto(
                    [.. options.CurrentValue.InsuranceTypes],
                    options.CurrentValue.EmailRouting.AsReadOnly())))
            .RequireAuthorization(AuthPolicies.Broker);

        var group = app.MapGroup("/api/broker/requests").RequireAuthorization(AuthPolicies.Broker);

        group.MapGet("/", async (
            ClaimsPrincipal principal, AppDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var brokerUserId = principal.GetUserId();
            if (brokerUserId is null)
            {
                return Results.Unauthorized();
            }

            var now = time.GetUtcNow().UtcDateTime;

            // One statement: the token's expiry and the document count are correlated subqueries
            // rather than a second round trip per row, and the `expired` decision is made where the
            // data is.
            var rows = await db.BrokerRequests.AsNoTracking()
                .Where(r => r.BrokerUserId == brokerUserId.Value)
                .OrderByDescending(r => r.CreatedAt)
                // Newest first, so the cap drops the oldest (7.2). Applied before the projection, so
                // the two correlated subqueries below run over at most `ListLimits.MaxRows` rows.
                .Take(ListLimits.MaxRows)
                .Select(r => new
                {
                    Request = r,
                    LinkExpiresAt = db.PublicLinkTokens
                        .Where(t => t.BrokerRequestId == r.Id && t.LockedAt == null)
                        .Max(t => (DateTime?)t.ExpiresAt),
                    Documents = db.Documents.Count(d =>
                        d.OwnerKind == DocumentOwnerKinds.BrokerRequest && d.OwnerId == r.Id),
                })
                .ToListAsync(ct);

            return Results.Ok(rows
                .Select(row => new BrokerRequestListItemDto(
                    row.Request.Id,
                    row.Request.Option,
                    Displayed(row.Request.State, row.LinkExpiresAt, now),
                    row.Request.InsuredName,
                    row.Request.InsuranceType,
                    row.Request.CustomerMobile,
                    row.Request.CreatedAt,
                    row.Request.SubmittedAt,
                    row.Request.EmailedAt,
                    row.Request.EmailRecipient,
                    row.LinkExpiresAt,
                    row.Documents))
                .ToList());
        });

        group.MapPost("/", async (
            CreateBrokerRequestDto dto, ClaimsPrincipal principal, AppDbContext db,
            BrokerRequestService requests, CancellationToken ct) =>
        {
            var brokerUserId = principal.GetUserId();
            if (brokerUserId is null)
            {
                return Results.Unauthorized();
            }

            ArgumentNullException.ThrowIfNull(dto);

            var outcome = await requests.Create(
                brokerUserId.Value,
                await DisplayName(db, brokerUserId.Value, ct),
                new BrokerRequestFields(
                    Trimmed(dto.InsuredName), Trimmed(dto.InsuranceType), Trimmed(dto.InsuredAddress),
                    dto.CarValue, dto.EstimatedPremium, dto.EffectiveDate),
                ct);

            return outcome.Request is null
                ? Answer(outcome)
                : Results.Created($"/api/broker/requests/{outcome.Request.Id}", Detail(outcome.Request, null));
        });

        group.MapGet("/{id:guid}", async (
            Guid id, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct) =>
        {
            var brokerUserId = principal.GetUserId();
            if (brokerUserId is null)
            {
                return Results.Unauthorized();
            }

            var request = await Find(db, id, brokerUserId.Value, ct);
            if (request is null)
            {
                return Results.NotFound();
            }

            var expiresAt = await db.PublicLinkTokens.AsNoTracking()
                .Where(t => t.BrokerRequestId == id && t.LockedAt == null)
                .MaxAsync(t => (DateTime?)t.ExpiresAt, ct);

            return Results.Ok(Detail(request, expiresAt));
        });

        group.MapPost("/{id:guid}/submit", async (
            Guid id, ClaimsPrincipal principal, BrokerRequestService requests, CancellationToken ct) =>
        {
            var brokerUserId = principal.GetUserId();
            return brokerUserId is null
                ? Results.Unauthorized()
                : Answer(await requests.Submit(id, brokerUserId.Value, ct));
        });

        // §5.3's B4 (slice 5.3). Its own route rather than an argument to submit: Option 2 walks a
        // different edge (`ready_to_send -> sent`) on fields somebody else filled in, and a broker
        // pressing this button is releasing a customer's submission, not filing their own.
        group.MapPost("/{id:guid}/send", async (
            Guid id, ClaimsPrincipal principal, BrokerRequestService requests, CancellationToken ct) =>
        {
            var brokerUserId = principal.GetUserId();
            return brokerUserId is null
                ? Results.Unauthorized()
                : Answer(await requests.Send(id, brokerUserId.Value, ct));
        });

        group.MapPost("/{id:guid}/resend", async (
            Guid id, ClaimsPrincipal principal, BrokerRequestService requests, CancellationToken ct) =>
        {
            var brokerUserId = principal.GetUserId();
            return brokerUserId is null
                ? Results.Unauthorized()
                : Answer(await requests.Resend(id, brokerUserId.Value, ct));
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
            var brokerUserId = principal.GetUserId();
            if (brokerUserId is null)
            {
                return Results.Unauthorized();
            }

            // **Tracked, unlike every other read on this group**, and that is the guard rather than a
            // detail — 5.1's lesson carried across, which is CLAUDE.md's "a lesson learned in one
            // adapter is not learned until it is checked in the others". The gate below decides from a
            // snapshot, but the document row commits in a later `SaveChanges` inside the pipeline, so
            // an upload admitted while the request was a draft can land *after* submit has already
            // built the email. That document is then in no email, refused by Resend
            // (`already_emailed`), and swept by `BrokerMediaCleanupTask` `BrokerBlobDays` after the
            // send: bytes the broker believes AXA holds, destroyed silently. So `state` — the same
            // concurrency token submit uses — is made part of the upload's transaction.
            var brokerRequest = await db.BrokerRequests
                .SingleOrDefaultAsync(r => r.Id == id && r.BrokerUserId == brokerUserId.Value, ct);

            if (brokerRequest is null)
            {
                return Results.NotFound();
            }

            // Writes `state` back unchanged, so two concurrent uploads never fight — the token's
            // *value* is what a transition changes — while a submit committing in between makes this
            // `SaveChanges` match no row and rolls the document back.
            db.Entry(brokerRequest).Property(r => r.State).IsModified = true;

            MediaUploadOutcome outcome;
            try
            {
                // `VisaNo: null` is structural, not an omission: `broker_document` is
                // PushTiming.Never, so `RequireVisa` is never reached and there is no outbox row to
                // address. The broker module has no visa to give.
                outcome = await uploads.Upload(
                    request,
                    new MediaUploadTarget(
                        DocumentOwnerKinds.BrokerRequest, brokerRequest.Id, VisaNo: null, brokerUserId),
                    ct,
                    gate: BrokerBucketGate(brokerRequest));
            }
            catch (DbUpdateConcurrencyException)
            {
                // The request was submitted while the file was still being read. The blob is written
                // and is now an orphan for §7.3's sweep — the safe failure order — and the document row
                // is not. Its own code rather than the gate's, for 5.1's reason: the gate refuses a
                // request that arrived too late, this refuses one that *became* too late.
                db.ChangeTracker.Clear();
                return Results.Json(
                    new { error = "request_changed" }, statusCode: StatusCodes.Status409Conflict);
            }

            return outcome.Document is null
                ? Results.Json(new { error = outcome.ErrorCode }, statusCode: outcome.StatusCode)
                : Results.Created(
                    $"/api/broker/requests/{id}/documents/{outcome.Document.Id}",
                    DocumentDto.From(outcome.Document));
        });

        group.MapGet("/{id:guid}/documents", async (
            Guid id, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct) =>
        {
            var brokerUserId = principal.GetUserId();
            if (brokerUserId is null)
            {
                return Results.Unauthorized();
            }

            var request = await Find(db, id, brokerUserId.Value, ct);
            if (request is null)
            {
                return Results.NotFound();
            }

            // No `OutboxSentQuery` join, unlike the declaration lists: a broker document has no outbox
            // row by construction, so `pushConfirmed` is false for the same reason `push_status` is
            // `n/a`, and joining would be asking a question whose answer is fixed.
            var documents = await db.Documents.AsNoTracking()
                .Where(d => d.OwnerKind == DocumentOwnerKinds.BrokerRequest && d.OwnerId == id)
                .OrderByDescending(d => d.CreatedAt)
                .Select(d => new DocumentDto(
                    d.Id, d.Bucket, d.DocType, d.Origin, d.ClarityResult, d.ContentType,
                    d.FileName, d.SizeBytes, d.PushStatus, false, d.BlobDeletedAt == null, d.CreatedAt))
                .ToListAsync(ct);

            return Results.Ok(documents);
        });

        // B4's photo review (slice 6.1). The broker has to be able to *look* at what the customer
        // sent before releasing it to AXA — five car sides and their supporting documents — and until
        // now the only content routes in the application were the declaration's.
        //
        // Ownership first, through the same `Find` the list above uses, so a broker learns nothing
        // about another broker's request; then the shared helper, which matches the document on its
        // owner as well as its id. Both halves matter: a lookup by `docId` alone would let a broker
        // read any document in the system by quoting its id under a request of their own.
        //
        // A blob `BrokerMediaCleanupTask` has already swept answers **404, and that is expected rather
        // than a fault** — §7.3 deletes a broker request's bytes `Retention:BrokerBlobDays` after the
        // email went, and `DocumentDto.BlobRetained` is how B4 knows never to link one.
        group.MapGet("/{id:guid}/documents/{docId:guid}/content", async (
            Guid id, Guid docId, ClaimsPrincipal principal, AppDbContext db, IBlobStore blobs,
            CancellationToken ct) =>
        {
            var brokerUserId = principal.GetUserId();
            if (brokerUserId is null)
            {
                return Results.Unauthorized();
            }

            var request = await Find(db, id, brokerUserId.Value, ct);
            if (request is null)
            {
                return Results.NotFound();
            }

            return await DocumentContent.Serve(
                db, blobs, DocumentOwnerKinds.BrokerRequest, id, docId, ct);
        });
    }

    /// <summary>
    /// 5.1's callback, for 5.1's reason: the refusal depends on which bucket arrived, and the bucket is
    /// not known until the multipart metadata has been read. Two rules, both consulted before the file
    /// is stored so a rejection costs no blob and leaves no row.
    /// </summary>
    private static BucketGate BrokerBucketGate(BrokerRequest request) => rule =>
    {
        if (!string.Equals(rule.Bucket, MediaBuckets.BrokerDocument, StringComparison.Ordinal))
        {
            return MediaUploadOutcome.Refused(
                StatusCodes.Status400BadRequest, "bucket_not_allowed_for_caller");
        }

        // Draft only. The submit sends every attached document as an email attachment, so a file added
        // afterwards is one nobody receives and nothing ever will — the broker equivalent of 4.1's
        // stranded `deferred` row, and refused for the same reason.
        return request.State == BrokerRequestState.Draft
            ? null
            : MediaUploadOutcome.Refused(
                StatusCodes.Status409Conflict, "request_already_submitted");
    };

    /// <summary>
    /// B1's lazy `expired` (§5.3). An Option 2 request whose token has lapsed without ever being
    /// locked is dead, and the broker's only move is to reissue.
    /// </summary>
    private static string Displayed(BrokerRequestState state, DateTime? linkExpiresAt, DateTime now) =>
        state is BrokerRequestState.LinkIssued or BrokerRequestState.CustomerInProgress
        && linkExpiresAt is { } expiry && expiry <= now
            ? BrokerRequestStates.Expired
            : state.ToDbValue();

    private static Task<BrokerRequest?> Find(
        AppDbContext db, Guid id, Guid brokerUserId, CancellationToken ct) =>
        db.BrokerRequests.AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == id && r.BrokerUserId == brokerUserId, ct);

    /// <summary>
    /// The broker's own name, snapshotted onto the request (§4). Read here rather than in the service
    /// so the service has no reason to know about `Users` at all.
    /// </summary>
    internal static Task<string?> DisplayName(AppDbContext db, Guid brokerUserId, CancellationToken ct) =>
        db.Users.AsNoTracking()
            .Where(u => u.Id == brokerUserId)
            .Select(u => (string?)u.DisplayName)
            .SingleOrDefaultAsync(ct);

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static BrokerRequestDetailDto Detail(BrokerRequest request, DateTime? linkExpiresAt) =>
        new(request.Id, request.Option, request.State.ToDbValue(), request.InsuredName,
            request.InsuranceType, request.InsuredAddress, request.CarValue, request.EstimatedPremium,
            request.EffectiveDate, request.CustomerMobile, request.CreatedAt, request.SubmittedAt,
            request.EmailedAt, request.EmailRecipient, linkExpiresAt);

    /// <summary>
    /// `GarageDeclarationEndpoints.Answer`'s shape, with one addition: a submit whose email failed is
    /// a **200 carrying `emailFailed`**, not an error. The transition happened; the send is what did
    /// not, and the screen needs to say both.
    /// </summary>
    internal static IResult Answer(BrokerOutcome outcome)
    {
        if (outcome.Request is not null)
        {
            return Results.Ok(new
            {
                state = outcome.Request.State.ToDbValue(),
                emailFailed = outcome.EmailFailed,
                recipient = outcome.Request.EmailRecipient,
            });
        }

        return outcome.ErrorCode is null
            ? Results.NotFound()
            : Results.Json(new { error = outcome.ErrorCode }, statusCode: outcome.StatusCode);
    }
}
