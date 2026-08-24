using Api.Infrastructure;
using Api.Integrations.Email;
using Api.Integrations.Next3;
using Api.Integrations.Push;
using Api.Modules.Audit;
using Api.Modules.Claims;
using Api.Modules.Media;
using Api.Modules.Notifications;
using Api.Modules.Users;
using Api.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Declarations;

/// <summary>
/// Either the declaration a transition produced, or a refusal with the code the client branches on —
/// the <see cref="MediaUploadOutcome"/> shape, so the status mapping lives with the rule that decided
/// it rather than being re-derived in two endpoint groups.
/// </summary>
/// <param name="Declaration">Null on every refusal, including the 404.</param>
public sealed record DeclarationOutcome(int StatusCode, string? ErrorCode, Declaration? Declaration)
{
    /// <summary>Not found, or not the caller's — the 2.x convention makes those the same answer.</summary>
    public static DeclarationOutcome NotFound() =>
        new(StatusCodes.Status404NotFound, null, null);

    public static DeclarationOutcome Refused(int statusCode, string errorCode) =>
        new(statusCode, errorCode, null);

    public static DeclarationOutcome Ok(Declaration declaration) =>
        new(StatusCodes.Status200OK, null, declaration);
}

/// <summary>
/// design.md §5.2's transitions, with the transactions and side effects each one owns.
///
/// The split with <see cref="Declaration"/> is deliberate: the entity decides whether a transition is
/// *legal*, this class decides what else has to be true and what else has to happen. Neither knows the
/// other's job, and the endpoints know neither — they translate an outcome into a status code.
///
/// **The notification fan-outs are outside their transactions, and every one of them is best effort.**
/// A submitted declaration that nobody was told about is a declaration sitting in the officer's inbox;
/// a submission rolled back because a push service was unreachable is a garage that has to do the work
/// again. §5.1's <c>AssignmentHandler</c> made the same call for the same reason.
/// </summary>
public sealed partial class DeclarationService(
    AppDbContext db,
    ClaimCache claims,
    OutboxWriter outbox,
    IPushSender push,
    IEmailSender email,
    AuditWriter audit,
    TimeProvider time,
    ILogger<DeclarationService> logger)
{
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Declaration {DeclarationId} transitioned, but notifying user {UserId} failed on every channel.")]
    private static partial void LogNotifyFailed(
        ILogger logger, Guid declarationId, Guid userId, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Declaration {DeclarationId} was submitted, but no active claim officer exists to notify.")]
    private static partial void LogNoOfficers(ILogger logger, Guid declarationId);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Declaration {DeclarationId} approved under visa {VisaNo}; {Documents} document(s) queued for NEXT3.")]
    private static partial void LogApproved(
        ILogger logger, Guid declarationId, string visaNo, int documents);

    /// <summary>G2 (§5.2): a new declaration in Draft. Plate required; the rest is optional (§1).</summary>
    public async Task<Declaration> CreateDraft(
        Guid garageUserId, string plateNo, string? insuredName, string? note, CancellationToken ct)
    {
        var declaration = new Declaration
        {
            Id = Guid.CreateVersion7(),
            GarageUserId = garageUserId,
            PlateNo = plateNo,
            InsuredName = insuredName,
            Note = note,
            CreatedAt = time.GetUtcNow().UtcDateTime,
        };

        db.Declarations.Add(declaration);
        audit.Append(
            garageUserId, AuditActions.DeclarationCreated, AuditEntityKinds.Declaration, declaration.Id,
            new { declaration.PlateNo });

        await db.SaveChangesAsync(ct);
        return declaration;
    }

    /// <summary>
    /// Garage submits for review (§5.2). The transition commits first; the officers are told after,
    /// because being told is not what makes it submitted.
    /// </summary>
    public async Task<DeclarationOutcome> Submit(Guid id, Guid garageUserId, CancellationToken ct)
    {
        var declaration = await Owned(id, garageUserId, ct);
        if (declaration is null)
        {
            return DeclarationOutcome.NotFound();
        }

        var outcome = await Transition(
            declaration,
            d => d.Submit(time.GetUtcNow().UtcDateTime),
            garageUserId,
            AuditActions.DeclarationSubmitted,
            detail: new { declaration.PlateNo },
            ct);

        if (outcome.Declaration is not null)
        {
            await NotifyOfficers(declaration, ct);
        }

        return outcome;
    }

    /// <summary>Garage begins repairs (§5.2). The one transition with no notification — §5.2 defines none.</summary>
    public async Task<DeclarationOutcome> StartRepairs(Guid id, Guid garageUserId, CancellationToken ct)
    {
        var declaration = await Owned(id, garageUserId, ct);
        if (declaration is null)
        {
            return DeclarationOutcome.NotFound();
        }

        return await Transition(
            declaration,
            d => d.StartRepairs(time.GetUtcNow().UtcDateTime),
            garageUserId,
            AuditActions.DeclarationRepairsStarted,
            detail: null,
            ct);
    }

    /// <summary>
    /// Garage submits the repair paperwork (§5.2's G4) — the machine's terminal transition.
    ///
    /// **At least one document from any of the three repair buckets**, not specifically an invoice:
    /// the BRD says "documents such like discharge, invoice", so requiring one particular kind would
    /// be an invented rule (pass-2 review decision 5). The precondition is over the *repair* set
    /// rather than "any document", because every declaration that reaches this state already has the
    /// survey paperwork and the approval image attached — a check over all documents would pass with
    /// nothing from the repair at all.
    ///
    /// The uploads are already at AXA by the time this runs: the repair buckets are
    /// <see cref="PushTiming.Immediate"/>, so each queued its own push under the visa when it landed.
    /// This transition records that the garage considers the job finished; it sends nothing.
    ///
    /// **And it notifies nobody.** §5.2 records the absence of an officer notification here as a gap
    /// in the BRD rather than an oversight — the BRD specifies none, so none is added quietly.
    /// </summary>
    public async Task<DeclarationOutcome> SubmitRepairDocs(Guid id, Guid garageUserId, CancellationToken ct)
    {
        var declaration = await Owned(id, garageUserId, ct);
        if (declaration is null)
        {
            return DeclarationOutcome.NotFound();
        }

        // An early exit, not the guard — Approve's reasoning, for the same reason: it only saves an
        // already-terminal declaration a pointless count query. The guard is the entity plus the
        // `state` concurrency token inside Transition.
        if (DeclarationTransitions.Target(declaration.State, DeclarationTransition.SubmitRepairDocs) is null)
        {
            return DeclarationOutcome.Refused(StatusCodes.Status409Conflict, "illegal_transition");
        }

        var repairDocuments = await db.Documents
            .Where(d => d.OwnerKind == DocumentOwnerKinds.Declaration
                && d.OwnerId == id
                && MediaBuckets.Repair.Contains(d.Bucket))
            .CountAsync(ct);

        if (repairDocuments == 0)
        {
            return DeclarationOutcome.Refused(
                StatusCodes.Status409Conflict, "repair_documents_required");
        }

        return await Transition(
            declaration,
            d => d.SubmitRepairDocs(time.GetUtcNow().UtcDateTime),
            garageUserId,
            AuditActions.DeclarationRepairDocsSubmitted,
            detail: new { Documents = repairDocuments },
            ct);
    }

    /// <summary>
    /// Officer approves and links the declaration to a visa (§5.2) — the slice's load-bearing
    /// transition, and the only one that puts anything in the NEXT3 queue.
    ///
    /// Three gates run before the transaction opens, in this order:
    /// <list type="number">
    /// <item>An <c>approval_image</c> must already be attached (#18's "approval and comments captured
    /// as an image"). Approval is two calls precisely so a canvas render that fails takes nothing with
    /// it — §5.2's "a render failure aborts the approval cleanly".</item>
    /// <item>NEXT3 must currently agree the visa exists. A visa it does not know would otherwise
    /// surface 26 h 36 m later as a `failed` push, on A2, long after the officer has moved on.</item>
    /// <item>The entity must agree the transition is legal, and the `state` concurrency token must
    /// agree nobody beat us to it.</item>
    /// </list>
    /// </summary>
    public async Task<DeclarationOutcome> Approve(
        Guid id, Guid officerUserId, string visaNo, string? comment, CancellationToken ct)
    {
        var declaration = await db.Declarations.SingleOrDefaultAsync(d => d.Id == id, ct);
        if (declaration is null)
        {
            return DeclarationOutcome.NotFound();
        }

        // An early exit, not the guard: the guard is the entity plus the concurrency token below.
        // Checking here only saves an already-decided declaration a pointless round trip to NEXT3.
        if (DeclarationTransitions.Target(declaration.State, DeclarationTransition.Approve) is null)
        {
            return DeclarationOutcome.Refused(StatusCodes.Status409Conflict, "illegal_transition");
        }

        var hasApprovalImage = await db.Documents
            .AnyAsync(d => d.OwnerKind == DocumentOwnerKinds.Declaration
                && d.OwnerId == id
                && d.PushStatus == DocumentPushStatuses.Deferred
                && d.Bucket == MediaBuckets.ApprovalImage, ct);

        if (!hasApprovalImage)
        {
            return DeclarationOutcome.Refused(StatusCodes.Status409Conflict, "approval_image_required");
        }

        // ClaimCache rather than a bare INext3Client.GetClaim: §4's refresh rule lives there, and
        // going through it also caches the claim, which is what the garage's unlocked G3 view reads.
        // Architecture rule 3 permits the read (it names only RecordArrival and UploadDocument).
        //
        // **`Stale` counts as unavailable here**, unlike everywhere else in the app. A stale row means
        // NEXT3 was unreachable and we are looking at an answer it gave earlier; for a screen that is
        // a banner, but linking a declaration to a visa is the one irreversible act in this flow and
        // it needs NEXT3 agreeing *now*. Approval can wait for NEXT3 to come back — the garage's
        // upload could not, which is why that one is queued instead.
        var lookup = await claims.Open(visaNo, ct);
        switch (lookup.Status)
        {
            case ClaimLookupStatus.NotFound:
                return DeclarationOutcome.Refused(StatusCodes.Status422UnprocessableEntity, "visa_not_found");
            case ClaimLookupStatus.Stale:
            case ClaimLookupStatus.Unavailable:
                return DeclarationOutcome.Refused(StatusCodes.Status503ServiceUnavailable, "next3_unavailable");
            case ClaimLookupStatus.Fresh:
            default:
                break;
        }

        // **Loaded here, not before the NEXT3 call** (slice 5.1). The set this reads is what the
        // transition below queues, and every document that lands between the read and the commit is
        // stranded: `deferred` for ever, with the only transition that drains that set already run.
        // Read before `claims.Open` — as it was — that window spanned a call to a legacy core with a
        // 30-second timeout, which on a bad day is the whole time a garage spends uploading. Read
        // here it is the microseconds up to `SaveChanges`. It does not close the window (the fix for
        // the remainder is a re-queue sweep, recorded as a ticket rather than built here), but it
        // stops it being wide enough to hit by accident.
        var deferred = await Deferred(id, ct);

        var outcome = await Transition(
            declaration,
            d =>
            {
                d.Approve(officerUserId, visaNo, time.GetUtcNow().UtcDateTime);
                AddComment(d.Id, officerUserId, comment);
                QueueDeferredDocuments(deferred, visaNo);
            },
            officerUserId,
            AuditActions.DeclarationApproved,
            detail: new { VisaNo = visaNo, Documents = deferred.Count, HasComment = !string.IsNullOrWhiteSpace(comment) },
            ct);

        if (outcome.Declaration is null)
        {
            return outcome;
        }

        LogApproved(logger, id, visaNo, deferred.Count);
        await NotifyGarage(
            declaration,
            NotificationTemplates.DeclarationApproved,
            "Declaration approved",
            $"Your declaration for {declaration.PlateNo} was approved under claim {visaNo}.",
            ct);

        return outcome;
    }

    /// <summary>
    /// Officer rejects — the BRD's "Disregard Case" (§5.2). Terminal, and **nothing is pushed to
    /// NEXT3**: diagram 02 pushes only on approval, so the deferred documents stay deferred and their
    /// blobs stay put. (That last part is a known retention gap, recorded for slice 7.2.)
    /// </summary>
    public async Task<DeclarationOutcome> Reject(
        Guid id, Guid officerUserId, string? comment, CancellationToken ct)
    {
        var declaration = await db.Declarations.SingleOrDefaultAsync(d => d.Id == id, ct);
        if (declaration is null)
        {
            return DeclarationOutcome.NotFound();
        }

        var outcome = await Transition(
            declaration,
            d =>
            {
                d.Reject(officerUserId, time.GetUtcNow().UtcDateTime);
                AddComment(d.Id, officerUserId, comment);
            },
            officerUserId,
            AuditActions.DeclarationRejected,
            detail: new { HasComment = !string.IsNullOrWhiteSpace(comment) },
            ct);

        if (outcome.Declaration is null)
        {
            return outcome;
        }

        // §1: the garage sees the status and not the comments — the BRD grants comment visibility "in
        // case of confirmation" only. So the notification says no more than the screen would.
        await NotifyGarage(
            declaration,
            NotificationTemplates.DeclarationRejected,
            "Declaration rejected",
            $"Your declaration for {declaration.PlateNo} was not accepted.",
            ct);

        return outcome;
    }

    /// <summary>
    /// Runs one transition and commits everything it touched in a single <c>SaveChanges</c> — the
    /// state, its timestamp, any comment rows, any document flips, any outbox rows, and the §9 audit
    /// entry.
    ///
    /// One <c>SaveChanges</c> **is** one transaction, so there is deliberately no explicit
    /// <c>BeginTransaction</c> here. 2.4's Arrived endpoint needed one only because
    /// <c>ExecuteUpdateAsync</c> issues its own statement outside the change tracker; everything here
    /// is tracked writes, and hand-rolling a transaction would import 2.4's execution-strategy hazard
    /// for nothing.
    /// </summary>
    private async Task<DeclarationOutcome> Transition(
        Declaration declaration,
        Action<Declaration> apply,
        Guid actorUserId,
        string auditAction,
        object? detail,
        CancellationToken ct)
    {
        try
        {
            apply(declaration);
        }
        catch (IllegalTransitionException)
        {
            return DeclarationOutcome.Refused(StatusCodes.Status409Conflict, "illegal_transition");
        }

        audit.Append(actorUserId, auditAction, AuditEntityKinds.Declaration, declaration.Id, detail);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // `state` is the concurrency token, so somebody else moved this declaration between our
            // read and our write. Everything staged in this batch — the comment rows, the document
            // flips, the outbox rows and the audit entry — rolls back with it, which is the whole
            // point: a losing approval must not leave a document queued against a visa the winning
            // transition never chose.
            //
            // The answer is the same 409 an out-of-order request gets, because from the caller's side
            // it is the same fact: this declaration is no longer in the state they acted on.
            db.ChangeTracker.Clear();
            return DeclarationOutcome.Refused(StatusCodes.Status409Conflict, "illegal_transition");
        }

        return DeclarationOutcome.Ok(declaration);
    }

    /// <summary>
    /// Flips every document waiting on this declaration to <c>queued</c> and enqueues its push, now
    /// that a visa exists to address them to.
    ///
    /// <c>clientRef</c> is the document id, exactly as the immediate path uses it, so a push queued
    /// here is idempotent on the same key an expert's photo would be. Which of the two enqueue methods
    /// a document gets is read off its **bucket rule** (<see cref="Next3PushKind"/>) rather than
    /// decided here by comparing bucket names: the same wire call either way, but A2 has to tell "the
    /// approval never reached NEXT3" from "a photo never reached NEXT3", and that is a §7.1 fact.
    ///
    /// The doc type comes off the **row**, not from config, because that is the value the upload
    /// validated and stored (3.1's "accept loosely, store canonically"). Reading it again here would
    /// let a config change between upload and approval file the document under a different code.
    /// </summary>
    private void QueueDeferredDocuments(List<Document> deferred, string visaNo)
    {
        foreach (var document in deferred)
        {
            var rule = MediaBuckets.Find(document.Bucket)
                ?? throw new InvalidOperationException(
                    $"Document {document.Id} is in bucket '{document.Bucket}', which §7.1 does not define.");

            var docType = document.DocType
                ?? throw new InvalidOperationException(
                    $"Document {document.Id} is deferred for NEXT3 but carries no document type.");

            var push = new DocumentPush(
                rule.Next3Folder,
                docType,
                // Rows written before slice 4.1 have no stored name; nothing else can reconstruct one.
                document.FileName ?? $"{document.Id:N}",
                document.ContentType,
                document.BlobKey);

            var clientRef = document.Id.ToString();

            document.OutboxMessageId = rule.PushKind switch
            {
                Next3PushKind.Approval => outbox.EnqueueApproval(visaNo, push, clientRef),
                Next3PushKind.Document => outbox.EnqueueDocument(visaNo, push, clientRef),
                _ => throw new ArgumentOutOfRangeException(nameof(deferred), rule.PushKind, null),
            };

            document.PushStatus = DocumentPushStatuses.Queued;
        }
    }

    private void AddComment(Guid declarationId, Guid authorUserId, string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return;
        }

        db.DeclarationComments.Add(new DeclarationComment
        {
            Id = Guid.CreateVersion7(),
            DeclarationId = declarationId,
            AuthorUserId = authorUserId,
            Body = body.Trim(),
            CreatedAt = time.GetUtcNow().UtcDateTime,
        });
    }

    /// <summary>Tracked, and scoped to the caller: §9's resource rule, in the query rather than after it.</summary>
    private Task<Declaration?> Owned(Guid id, Guid garageUserId, CancellationToken ct) =>
        db.Declarations.SingleOrDefaultAsync(d => d.Id == id && d.GarageUserId == garageUserId, ct);

    private Task<List<Document>> Deferred(Guid declarationId, CancellationToken ct) =>
        db.Documents
            .Where(d => d.OwnerKind == DocumentOwnerKinds.Declaration
                && d.OwnerId == declarationId
                && d.PushStatus == DocumentPushStatuses.Deferred)
            .ToListAsync(ct);

    /// <summary>
    /// §5.2's submit fan-out: **every** active claim officer, because the BRD defines no per-officer
    /// assignment and inventing a queue is not ours to do.
    ///
    /// A left join, so an officer whose profile row is missing still gets the popup — they just have
    /// no address to fall back to. An inner join would have silently skipped them.
    /// </summary>
    private async Task NotifyOfficers(Declaration declaration, CancellationToken ct)
    {
        var officers = await db.Users.AsNoTracking()
            .Where(u => u.Role == UserRole.ClaimOfficer && u.Status == UserStatus.Active)
            .GroupJoin(
                db.ClaimOfficerProfiles.AsNoTracking(),
                user => user.Id,
                profile => profile.UserId,
                (user, profiles) => new { user.Id, Email = profiles.Select(p => p.Email).FirstOrDefault() })
            .ToListAsync(ct);

        if (officers.Count == 0)
        {
            // Not an error — an install with no officers yet is a configuration state, not a fault —
            // but silence here would look exactly like a working notification.
            LogNoOfficers(logger, declaration.Id);
            return;
        }

        var message = new PushMessage(
            "New declaration to review",
            $"A garage submitted a declaration for {declaration.PlateNo}.",
            $"/officer/{declaration.Id}");

        foreach (var officer in officers)
        {
            await NotifyOne(
                declaration,
                officer.Id,
                officer.Email,
                message,
                NotificationTemplates.DeclarationSubmitted,
                "New declaration to review",
                $"A garage submitted a declaration for {declaration.PlateNo}. Review it in the AXA claims app.",
                ct);
        }
    }

    private async Task NotifyGarage(
        Declaration declaration, string template, string title, string body, CancellationToken ct)
    {
        var address = await db.GarageProfiles.AsNoTracking()
            .Where(p => p.UserId == declaration.GarageUserId)
            .Select(p => p.Email)
            .FirstOrDefaultAsync(ct);

        await NotifyOne(
            declaration,
            declaration.GarageUserId,
            address,
            new PushMessage(title, body, $"/garage/{declaration.Id}"),
            template,
            title,
            body,
            ct);
    }

    /// <summary>
    /// Push one user, and fall back to email if the push does not land (§8's "+ email fallback").
    ///
    /// **The catch is deliberately broad**, matching <c>AssignmentHandler.Notify</c>. Catching only
    /// <c>PushNotDeliveredException</c> would miss the two failures that actually happen: the fake
    /// sender throws <c>FakeTransientException</c>, which is what slice 4.3's demo raises when it
    /// "kills NEXT3" with <c>Fake:FailureRate = 1.0</c>; and an HTTP client reports its own timeout as
    /// <c>TaskCanceledException</c>, the trap 3.3 fixed and 3.4 reintroduced the next day. Either one
    /// would have faulted a transition that had already committed.
    ///
    /// <c>OperationCanceledException</c> is excluded on both channels, because a genuine shutdown must
    /// stay a cancellation rather than being logged as a delivery failure.
    /// </summary>
    private async Task NotifyOne(
        Declaration declaration,
        Guid userId,
        string? emailAddress,
        PushMessage message,
        string template,
        string subject,
        string body,
        CancellationToken ct)
    {
        try
        {
            await push.Send(userId, message, template, ct);
            return;
        }
        catch (Exception pushFailure) when (pushFailure is not OperationCanceledException)
        {
            if (string.IsNullOrWhiteSpace(emailAddress))
            {
                LogNotifyFailed(logger, declaration.Id, userId, pushFailure);
                return;
            }

            try
            {
                await email.Send(emailAddress, subject, body, template, userId, ct);
            }
            catch (Exception emailFailure) when (emailFailure is not OperationCanceledException)
            {
                // Both channels are gone. The sender already logged its own `failed` notification row
                // (§8), and the transition is committed and visible on the recipient's list — so this
                // is a log line, not a rollback. One unreachable officer must not cost the others
                // their notification either, which is why this is caught per recipient.
                LogNotifyFailed(logger, declaration.Id, userId, emailFailure);
            }
        }
    }
}
