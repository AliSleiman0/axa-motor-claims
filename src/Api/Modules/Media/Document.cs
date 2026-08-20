namespace Api.Modules.Media;

/// <summary>
/// Every media item in the system (design.md §4). Authoritative for the metadata; the binary is
/// transit-only, so a row outlives its blob by design — §7.3 deletes the bytes once NEXT3 has them
/// and NEXT3 becomes the system of record from that moment.
///
/// Two columns are not in §4's list and were added here, both recorded in scope-decisions.md:
/// <list type="bullet">
/// <item><see cref="OutboxMessageId"/> — §7.3 requires the cleanup query to join on outbox `sent`
/// "structurally, not by convention", and the only other join key available is the clientRef buried
/// in the outbox row's JSON payload, which is exactly a convention. No FK: that would put
/// <c>Next3OutboxMessage</c> into this module's EF configuration and break architecture rule 4.</item>
/// <item><see cref="BlobDeletedAt"/> — the sweep must be idempotent, and "has this photo's blob been
/// cleaned up yet?" is the support question A2 and the runbook both ask.</item>
/// </list>
/// </summary>
public sealed class Document
{
    public Guid Id { get; set; }

    /// <summary>One of <see cref="DocumentOwnerKinds"/>.</summary>
    public required string OwnerKind { get; set; }

    /// <summary>
    /// The assignment / declaration / broker request this belongs to. Polymorphic, so no FK — the
    /// owner kind decides which table the id points at.
    /// </summary>
    public Guid OwnerId { get; set; }

    /// <summary>A §7.1 bucket — see <see cref="MediaBuckets"/>, which is where its rules live.</summary>
    public required string Bucket { get; set; }

    /// <summary>
    /// NEXT3's document-type code, resolved from the `Next3:DocTypes` placeholder map (#12). Null for
    /// media that never reaches NEXT3 (the broker module, §5.3).
    /// </summary>
    public string? DocType { get; set; }

    /// <summary>
    /// The BRD's provenance flag — one of <see cref="DocumentOrigins"/>. Kept on every row, not only
    /// where it is enforced: §7.1's capture-only rule is checked at upload, but Broker Option 1
    /// requires the flag to be *readable* afterwards.
    /// </summary>
    public required string Origin { get; set; }

    /// <summary>One of <see cref="ClarityResults"/> (§7.2).</summary>
    public required string ClarityResult { get; set; }

    public required string BlobKey { get; set; }

    public required string ContentType { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>One of <see cref="DocumentPushStatuses"/>.</summary>
    public required string PushStatus { get; set; }

    /// <summary>
    /// The outbox row carrying this document to NEXT3, or null when there is none (broker media, and
    /// anything whose push status is `n/a`). The join key §7.3's cleanup query needs.
    /// </summary>
    public Guid? OutboxMessageId { get; set; }

    /// <summary>Null = the bytes are still in the transit container. Set by §7.3's cleanup sweep.</summary>
    public DateTime? BlobDeletedAt { get; set; }

    /// <summary>Null for the unauthenticated Option 2 customer (§5.3) — the audit row carries the token.</summary>
    public Guid? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>The §4 nvarchar enum values for `document.owner_kind`.</summary>
public static class DocumentOwnerKinds
{
    public const string Assignment = "assignment";

    public const string Declaration = "declaration";

    public const string BrokerRequest = "broker_request";
}

/// <summary>The BRD's provenance flag (§4 `origin`).</summary>
public static class DocumentOrigins
{
    public const string Captured = "captured";

    public const string Uploaded = "uploaded";
}

/// <summary>
/// The §4 values for `document.push_status`. §4 names only `n/a` ("n/a for broker docs — broker
/// module never pushes to NEXT3"), and deliberately nothing more: live push state belongs to the
/// outbox row, and denormalising it onto a second table gives two answers that can disagree — which
/// matters, because §7.3 deletes blobs on one of those answers. So this column says only whether the
/// document takes part in the NEXT3 pipeline at all.
/// </summary>
public static class DocumentPushStatuses
{
    /// <summary>An outbox row exists for this document; <see cref="Document.OutboxMessageId"/> names it.</summary>
    public const string Queued = "queued";

    /// <summary>Never pushed — the broker module's media (§5.3).</summary>
    public const string NotApplicable = "n/a";
}

/// <summary>
/// The §4 values for `document.clarity_result`. There is no `failed`: §7.2's server-side re-validation
/// (item 5 — dimensions, size, content type) *rejects* the upload rather than storing a failure, and
/// the blur check is client-side only, so nothing on the server can downgrade a row after the fact.
/// </summary>
public static class ClarityResults
{
    /// <summary>An image that passed the client gate and the server's re-validation.</summary>
    public const string Passed = "passed";

    /// <summary>Not an image — a PDF today, audio once #10 answers (§7.2 item 4 is playback-confirm only).</summary>
    public const string NotApplicable = "not_applicable";
}
