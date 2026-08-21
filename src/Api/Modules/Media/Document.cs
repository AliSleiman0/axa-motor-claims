namespace Api.Modules.Media;

/// <summary>
/// Every media item in the system (design.md §4). Authoritative for the metadata; the binary is
/// transit-only, so a row outlives its blob by design — §7.3 deletes the bytes once NEXT3 has them
/// and NEXT3 becomes the system of record from that moment.
///
/// Three columns are not in §4's list and were added here, all recorded in scope-decisions.md:
/// <list type="bullet">
/// <item><see cref="OutboxMessageId"/> — §7.3 requires the cleanup query to join on outbox `sent`
/// "structurally, not by convention", and the only other join key available is the clientRef buried
/// in the outbox row's JSON payload, which is exactly a convention. No FK: that would put
/// <c>Next3OutboxMessage</c> into this module's EF configuration and break architecture rule 4.</item>
/// <item><see cref="BlobDeletedAt"/> — the sweep must be idempotent, and "has this photo's blob been
/// cleaned up yet?" is the support question A2 and the runbook both ask.</item>
/// <item><see cref="FileName"/> — slice 4.1. See its own remarks: a deferred push is queued in a
/// different request from the upload that produced it, and the name would otherwise be gone.</item>
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

    /// <summary>
    /// The uploader's own file name, already stripped to a safe leaf. This is what NEXT3 files the
    /// document under, so <c>invoice.pdf</c> arrives as <c>invoice.pdf</c>.
    ///
    /// It has to be **stored** rather than read from the request, because a
    /// <see cref="PushTiming.OnApproval"/> document is queued by §5.2's approve transition — a
    /// different request, hours later, with the multipart <c>Content-Disposition</c> long gone. Before
    /// this column a garage's <c>invoice.pdf</c> would have reached the *Survey* folder as
    /// <c>019ab….pdf</c>, on exactly the linking step this project exists to get right.
    ///
    /// Nullable because rows written before slice 4.1 genuinely have no name — the enqueue falls back
    /// to <c>{id:N}{ext}</c> — and a `NOT NULL DEFAULT ''` would have made the migration claim
    /// otherwise.
    /// </summary>
    public string? FileName { get; set; }

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
/// The §4 values for `document.push_status`. Live push state still belongs to the outbox row, and
/// denormalising it onto a second table would give two answers that can disagree — which matters,
/// because §7.3 deletes blobs on one of those answers. So this column says only **whether, and when,**
/// the document takes part in the NEXT3 pipeline; never how a push is going.
///
/// <see cref="Deferred"/> joined the pair in slice 4.1 (design.md §4 updated to match). §5.2's
/// "nothing goes to NEXT3 before approval" had no mechanism behind it: every upload wrote its outbox
/// row in the same transaction as the document row, so a garage's Draft would have started pushing
/// under a visa nobody had chosen yet.
/// </summary>
public static class DocumentPushStatuses
{
    /// <summary>An outbox row exists for this document; <see cref="Document.OutboxMessageId"/> names it.</summary>
    public const string Queued = "queued";

    /// <summary>
    /// Waiting for §5.2's approval, which is where the visa comes from. No outbox row yet, so §7.3's
    /// first sweep cannot see it and its blob is retained — which is the intended behaviour, and also
    /// why a *rejected* declaration keeps its bytes indefinitely (recorded as a 7.2 ticket).
    /// </summary>
    public const string Deferred = "deferred";

    /// <summary>Never pushed — the broker module's media (§5.3).</summary>
    public const string NotApplicable = "n/a";

    /// <summary>The check-constraint list; kept beside the constants so the two cannot drift.</summary>
    public static readonly string[] All = [Queued, Deferred, NotApplicable];

    /// <summary>
    /// The statuses that must have **no** outbox row. The other half of
    /// `CK_document_push_status_outbox`, and the reason it is an array rather than two literals in the
    /// constraint string: adding a fourth status without deciding which side it falls on should be
    /// impossible to do by accident.
    /// </summary>
    public static readonly string[] WithoutOutboxRow = [Deferred, NotApplicable];
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
