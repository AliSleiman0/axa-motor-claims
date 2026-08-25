using Api.Infrastructure;
using Api.Modules.Audit;
using Api.Modules.Broker;
using Api.Modules.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Modules.PublicSurface;

/// <summary>What the customer's device may fill in (§5.3 P1). All optional until submit validates them.</summary>
public sealed record PublicSubmissionDto(
    string? InsuredName,
    string? InsuranceType,
    string? InsuredAddress,
    decimal? CarValue,
    decimal? EstimatedPremium,
    DateOnly? EffectiveDate);

/// <summary>
/// What the page is told before submission — deliberately almost nothing (§9.1).
/// </summary>
/// <param name="BrokerDisplayName">
/// **The one identifying value this surface reveals, added deliberately in slice 5.3.** Slice 1.5
/// returned nothing at all and a test pinned that; pass-2 review decision 3 changed the answer, and
/// design.md §9.1's "what the page exposes" always named the broker's display name. The reason is not
/// friendliness: an anonymous page asking a member of the public to photograph their identity card is
/// the exact shape of a phishing page, and a customer who cannot tell whose form this is has no way
/// to decide whether to trust it.
///
/// It is read from <c>broker_request.broker_display_name</c> — the **snapshot** slice 5.2 writes at
/// link creation — and never from <c>app_user</c>, which architecture rule 2 puts out of this
/// module's reach entirely. Null on any link issued before 5.2 existed, and P1 renders no name at all
/// in that case rather than a placeholder.
/// </param>
/// <param name="InsuranceTypes">
/// #14's placeholder list, so P1's select has options. The list a client-side form offers must be the
/// list the server validates against, or the customer picks a value their submission is then refused
/// for — and CLAUDE.md's placeholder rule forbids writing it into TypeScript. `/api/broker/config`
/// serves the same list to B2, but it sits behind the broker policy and P1 holds no session, so the
/// values ride here instead. It reveals nothing a live-token holder is not already being shown.
/// </param>
public sealed record PublicLinkView(
    string State,
    DateTime ExpiresAt,
    int MaxFiles,
    int MaxFileMb,
    string? BrokerDisplayName,
    IReadOnlyList<string> InsuranceTypes);

/// <summary>
/// What the customer sees of a file they have already attached (§5.3 P1Capture): enough to recognise
/// it and count it, and nothing else.
/// </summary>
/// <remarks>
/// Deliberately **not** <c>DocumentDto</c>. That record carries the document type, the clarity
/// verdict, the push status and whether the blob is retained — every one of them a question this page
/// has no reason to ask, and §9.1's rule for this surface is that it learns nothing it does not need.
/// A public bucket is <c>PushTiming.Never</c> anyway, so three of those four fields are constants.
/// </remarks>
public sealed record PublicDocumentDto(Guid Id, string Bucket, string? FileName, long SizeBytes);

/// <summary>
/// The only unauthenticated surface in the system (design.md §3). The boundary is structural: this
/// namespace cannot reference <c>INext3Client</c> or any user/profile type, and architecture rule 2
/// fails the build if that ever changes.
/// </summary>
/// <remarks>
/// Every failure mode — unknown token, malformed token, expired, already submitted — returns the
/// same bare 404. There are no distinguishable states to enumerate, so a scraper holding a bad link
/// learns nothing, not even whether the link ever existed.
/// </remarks>
public static class PublicEndpoints
{
    public static IEndpointRouteBuilder MapPublicEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(PublicRateLimiting.PathPrefix);

        group.MapGet("/{token}", async (
            string token, PublicLinkTokenService links, AppDbContext db, AuditWriter audit,
            IOptionsMonitor<PublicLinkOptions> options, IOptionsMonitor<BrokerOptions> broker,
            CancellationToken ct) =>
        {
            var (_, link) = await links.Resolve(token, ct);
            if (link is null)
            {
                return NotFound();
            }

            if (links.MarkOpened(link))
            {
                // Actor is null: a member of the public, not a user (§9). The token id identifies
                // the session without putting the credential itself in the log.
                audit.Append(
                    null, AuditActions.PublicLinkOpened, AuditEntityKinds.PublicLinkToken, link.Token.Id,
                    new { BrokerRequestId = link.Request.Id });

                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Slice 5.2 made `broker_request.state` a concurrency token for the submit path,
                    // and this write is the one place where losing that race means **nothing at all**:
                    // two simultaneous opens of the same link — a double tap on an SMS, a prefetch, a
                    // pull-to-refresh — both move `link_issued` to `customer_in_progress`, and the
                    // loser's row already says what it was trying to say. Swallowed rather than
                    // rethrown because there is no global exception handler and §9.1 requires this
                    // surface to answer uniformly; a 500 here would be a new way to distinguish one
                    // token's state from another's.
                    db.ChangeTracker.Clear();
                }
            }

            var current = options.CurrentValue;
            return Results.Ok(new PublicLinkView(
                link.Request.State.ToDbValue(),
                link.Token.ExpiresAt,
                current.MaxFiles,
                current.MaxFileMb,
                link.Request.BrokerDisplayName,
                [.. broker.CurrentValue.InsuranceTypes]));
        });

        MapDocuments(group);

        group.MapPost("/{token}/submit", async (
            string token, PublicSubmissionDto dto, PublicLinkTokenService links, AppDbContext db,
            AuditWriter audit, IOptionsMonitor<BrokerOptions> broker, BrokerRequestNotifier notifier,
            CancellationToken ct) =>
        {
            var (_, link) = await links.Resolve(token, ct);
            if (link is null)
            {
                return NotFound();
            }

            // Every refusal below is a 400 and **every one of them leaves the token alive**, which is
            // slice 1.5's rule and matters more here than the codes do: a customer who forgets a
            // document must be able to add it and press Send again, and a link that burned on a failed
            // validation would send them back to their broker for a new one.
            if (!IsComplete(dto))
            {
                // A 400 here is safe: the caller already proved it holds a live token, so this
                // reveals nothing a valid holder does not already know.
                return Results.BadRequest(new { error = "incomplete_submission" });
            }

            // The same check `BrokerRequestService.Validate` makes on Option 1, for the same reason:
            // #14's list is configuration, and the recipient B4 later resolves comes from the routing
            // table keyed on it. A type that is not on the list has no route, so accepting it here
            // would produce a `ready_to_send` request the broker's own Send email cannot deliver.
            if (!broker.CurrentValue.InsuranceTypes.Contains(dto.InsuranceType, StringComparer.Ordinal))
            {
                return Results.BadRequest(new { error = "unknown_insurance_type" });
            }

            // §5.3's two media preconditions, from one read.
            //
            // **Which buckets hold something**, rather than how many rows each holds: both rules are
            // existence questions, and asking them as one `Distinct` keeps them provably disjoint —
            // the alternative is two counts that a later edit can quietly point at the same set.
            // Scoped to the customer's own buckets, not to the owner: `broker_document` shares this
            // owner kind, so a request the broker had attached a file to would otherwise satisfy a
            // rule about what the *customer* provided.
            var filled = await db.Documents.AsNoTracking()
                .Where(d => d.OwnerKind == DocumentOwnerKinds.BrokerRequest
                    && d.OwnerId == link.Request.Id
                    && MediaBuckets.PublicCustomer.Contains(d.Bucket))
                .Select(d => d.Bucket)
                .Distinct()
                .ToListAsync(ct);

            // The supporting documents (§5.3, slice 5.3). Still `public_document` alone: **a car shot
            // never satisfies this**, which is the whole of the disjointness the card asks for — a
            // customer who photographs their car has not thereby sent their identity card.
            if (!filled.Contains(MediaBuckets.PublicDocument, StringComparer.Ordinal))
            {
                return Results.BadRequest(new { error = "documents_required" });
            }

            // §5.3's five mandatory car sides (slice 6.1) — "car photos are mandatory, the BRD's hard
            // rule". Checked after the documents so the two refusals arrive in the order P1 asks for
            // them, and **every one of the five must be present**: four sides and a missing roof is a
            // quotation AXA cannot price.
            //
            // It deliberately does **not** name the missing sides. The page holds the same list and
            // computes them from its own document list, and §9.1's surface says as little as it can.
            if (MediaBuckets.PublicCarShots.Except(filled, StringComparer.Ordinal).Any())
            {
                return Results.BadRequest(new { error = "car_photos_required" });
            }

            Apply(dto, link);
            links.Lock(link);
            audit.Append(
                null, AuditActions.PublicLinkSubmitted, AuditEntityKinds.PublicLinkToken, link.Token.Id,
                new { BrokerRequestId = link.Request.Id });

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another submission locked the token between this request's read and its write
                // (§9.1: exactly one submission per link). The loser is holding a token that is now
                // locked, and a locked token is a 404 — so it leaves through the same door as every
                // other dead token, and the field values and audit row roll back with the batch.
                return NotFound();
            }

            // **After the commit, and best effort.** §5.3's ordering, third outing: the submission is
            // committed and the token is spent, so a push service that is down must not turn any of
            // that into an error on the one screen a member of the public ever sees. The notifier
            // lives in `Api.Modules.Broker` and takes an id, because resolving the broker's devices
            // and email address means reading `Users` — which architecture rule 2 forbids this module
            // from doing at all.
            await notifier.NotifyReadyToSend(link.Request.Id, ct);

            return Results.Ok();
        });

        return app;
    }

    /// <summary>
    /// §5.3's "uploads supporting documents", and the only route on this surface that stores anything.
    ///
    /// **One file per request**, which is a design decision rather than a limitation. Slice 1.5's
    /// <see cref="PublicBodySizeMiddleware"/> caps a <c>/public/*</c> body at one file's worth, and
    /// `scope-decisions.md` recorded that the ceiling "must rise to ~`MaxFiles x MaxFileMb` the moment
    /// 5.3/6.1 posts a multipart submission of up to `MaxFiles` files". It does not: a request that
    /// carries one file needs one file's worth of ceiling, so the cap that already exists is the right
    /// one and the caveat is resolved rather than raised. It is also the cheaper failure — a customer
    /// on a phone who loses their connection loses one photograph rather than fifteen.
    /// </summary>
    private static void MapDocuments(RouteGroupBuilder group)
    {
        group.MapPost("/{token}/documents", async (
            string token, HttpRequest request, PublicLinkTokenService links, AppDbContext db,
            MediaUploadService uploads, IOptionsMonitor<PublicLinkOptions> options,
            CancellationToken ct) =>
        {
            var (_, link) = await links.Resolve(token, ct);
            if (link is null)
            {
                return NotFound();
            }

            // §9.1's "hard caps on file count and size per submission", and `PublicUploadCaps`'
            // **first production caller** — it has been a tested pure guard with no call site since
            // slice 1.5, which is exactly the arrangement that survives being wired up wrongly.
            //
            // Counted against what is already stored plus this one, so fifteen requests each carrying
            // one legal file are refused at the sixteenth rather than each being judged in isolation.
            // The size argument is `Content-Length`, which is the multipart envelope and therefore an
            // **upper bound** on the file inside it: passing this proves the file is within cap. Said
            // plainly because it makes that branch a second layer rather than the only one —
            // `PublicBodySizeMiddleware` already rejects the same predicate before this handler runs,
            // and lowers `IHttpMaxRequestBodySizeFeature` so a caller who lies about `Content-Length`
            // is cut off by the server. The **count** is the half only this call can make.
            var stored = await db.Documents.AsNoTracking()
                .CountAsync(
                    d => d.OwnerKind == DocumentOwnerKinds.BrokerRequest && d.OwnerId == link.Request.Id,
                    ct);

            var cap = PublicUploadCaps.Validate(
                stored + 1, [request.ContentLength ?? 0], options.CurrentValue);

            if (cap is not UploadCapResult.Ok)
            {
                return cap is UploadCapResult.TooManyFiles
                    ? Results.BadRequest(new { error = "too_many_files" })
                    : Results.Json(
                        new { error = "file_too_large" },
                        statusCode: StatusCodes.Status413PayloadTooLarge);
            }

            // **5.2's guard, carried across rather than re-learned** (CLAUDE.md: "a lesson learned in
            // one adapter is not learned until it is checked in the others"). `Resolve` returns the
            // request *tracked*, and writing `state` back unchanged enlists this upload in the same
            // concurrency check the submit uses. Without it, a file admitted while the link was open
            // could commit after the submit had locked the token: attached to a request the broker has
            // already reviewed, carried by no email, and then deleted by `BrokerMediaCleanupTask` on
            // the send's clock — bytes the customer watched upload, destroyed silently.
            db.Entry(link.Request).Property(r => r.State).IsModified = true;

            try
            {
                var outcome = await uploads.Upload(
                    request,
                    // `ActorUserId: null` is the whole of §5.3's public customer in the data model —
                    // `document.created_by` has been nullable since slice 2.3 for this caller and no
                    // other. `VisaNo: null` is structural: a `PushTiming.Never` bucket never reaches
                    // `RequireVisa`, and the broker module has no visa to give.
                    new MediaUploadTarget(
                        DocumentOwnerKinds.BrokerRequest, link.Request.Id, VisaNo: null, ActorUserId: null),
                    ct,
                    // The six buckets a customer may write — the supporting documents and §5.3's five
                    // car sides (widened in slice 6.1). The allow-list is load-bearing rather than
                    // tidy: `broker_document` shares this owner kind, so the bucket rules alone would
                    // let an anonymous caller file a document as the broker's own — and `origin` is a
                    // claim the client makes, so nothing else would catch it. `MediaBuckets`
                    // deliberately names this set rather than `BrokerRequest`, which includes the one
                    // bucket that must stay out.
                    gate: BucketGates.Only(MediaBuckets.PublicCustomer),
                    // §5.3's sides are one photograph each, so a retake **replaces** rather than
                    // adds (slice 6.1). Staged here rather than after the upload because the unique
                    // index on `(owner_kind, owner_id, bucket)` would see both rows otherwise; one
                    // `SaveChanges` carries the delete and the insert together, so a failed upload
                    // leaves the customer's earlier photograph exactly where it was.
                    //
                    // The **row** goes and the blob does not: §7.3's safe failure order says a blob
                    // without a row is garbage the orphan sweep collects, while deleting bytes whose
                    // row then survives a rollback is unrecoverable. The old photograph is unclaimed
                    // the moment this commits, and `Retention:OrphanBlobHours` takes it from there.
                    accepted: (rule, token) => ReplacePreviousCarShot(db, link.Request.Id, rule, token));

                return outcome.Document is null
                    ? Results.Json(new { error = outcome.ErrorCode }, statusCode: outcome.StatusCode)
                    : Results.Created(
                        $"{PublicRateLimiting.PathPrefix}/{token}/documents/{outcome.Document.Id}",
                        Project(outcome.Document));
            }
            catch (DbUpdateConcurrencyException)
            {
                // The submit committed while this file was still being read, so the token is now
                // locked — and a locked token is the uniform 404, not a coded 409 like the broker's
                // equivalent. There is nothing to tell a caller whose link has just died that §9.1
                // permits telling them. The blob is written and is an orphan for §7.3's sweep (the
                // safe failure order); the document row is not.
                db.ChangeTracker.Clear();
                return NotFound();
            }
        });

        group.MapGet("/{token}/documents", async (
            string token, PublicLinkTokenService links, AppDbContext db, CancellationToken ct) =>
        {
            var (_, link) = await links.Resolve(token, ct);
            if (link is null)
            {
                return NotFound();
            }

            // Scoped to the customer's own buckets, so a broker's own attachments are never listed
            // back to a member of the public — the same reason submit counts on the bucket rather than
            // the owner. **Widened from the one supporting-document bucket in slice 6.1**: the five car
            // sides come back here too, each carrying its `Bucket`, which is how P1 renders its done
            // marks and its "N of 5" without a second endpoint or a count the server has to compute.
            var documents = await db.Documents.AsNoTracking()
                .Where(d => d.OwnerKind == DocumentOwnerKinds.BrokerRequest
                    && d.OwnerId == link.Request.Id
                    && MediaBuckets.PublicCustomer.Contains(d.Bucket))
                .OrderByDescending(d => d.CreatedAt)
                .Select(d => new PublicDocumentDto(d.Id, d.Bucket, d.FileName, d.SizeBytes))
                .ToListAsync(ct);

            return Results.Ok(documents);
        });
    }

    /// <summary>
    /// Marks the side's previous photograph for deletion, so the upload in flight replaces it
    /// (slice 6.1). A no-op for the supporting-document bucket, which is genuinely many-per-request.
    /// </summary>
    private static async Task ReplacePreviousCarShot(
        AppDbContext db, Guid requestId, BucketRule rule, CancellationToken ct)
    {
        if (!MediaBuckets.PublicCarShots.Contains(rule.Bucket, StringComparer.Ordinal))
        {
            return;
        }

        // `ToListAsync` rather than `SingleOrDefault`: the unique index makes more than one
        // impossible from here on, but rows written before it existed are not covered by it, and a
        // sweep that assumed one would throw on exactly the data it was added to clean up.
        var previous = await db.Documents
            .Where(d => d.OwnerKind == DocumentOwnerKinds.BrokerRequest
                && d.OwnerId == requestId
                && d.Bucket == rule.Bucket)
            .ToListAsync(ct);

        db.Documents.RemoveRange(previous);
    }

    private static PublicDocumentDto Project(Document document) =>
        new(document.Id, document.Bucket, document.FileName, document.SizeBytes);

    /// <summary>
    /// The uniform 404 of §9.1. One helper, one shape — invalid, expired and locked must be
    /// byte-identical, and they stay that way only if there is exactly one place that writes them.
    /// </summary>
    private static IResult NotFound() => Results.NotFound();

    /// <summary>
    /// The six fields of §5.3, including the customer-entered premium (§1's recorded decision, #24c).
    /// The media preconditions are separate and live in the handler above: the supporting documents
    /// since slice 5.3, the five car sides since 6.1. Neither could be enforced before there was
    /// anywhere to put a file, which is why they arrived slices apart from the fields they accompany.
    /// </summary>
    private static bool IsComplete(PublicSubmissionDto dto) =>
        !string.IsNullOrWhiteSpace(dto.InsuredName)
        && !string.IsNullOrWhiteSpace(dto.InsuranceType)
        && !string.IsNullOrWhiteSpace(dto.InsuredAddress)
        && dto.CarValue is > 0
        && dto.EstimatedPremium is > 0
        && dto.EffectiveDate is not null;

    private static void Apply(PublicSubmissionDto dto, ResolvedPublicLink link)
    {
        link.Request.InsuredName = dto.InsuredName;
        link.Request.InsuranceType = dto.InsuranceType;
        link.Request.InsuredAddress = dto.InsuredAddress;
        link.Request.CarValue = dto.CarValue;
        link.Request.EstimatedPremium = dto.EstimatedPremium;
        link.Request.EffectiveDate = dto.EffectiveDate;
    }
}
