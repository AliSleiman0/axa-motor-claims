namespace Api.Outbox;

/// <summary>
/// One pending write into NEXT3 (design.md §4, §6.3). Every NEXT3 write in the system goes through
/// this table: the domain row and its outbox row commit in one transaction, and the worker drains
/// the queue afterwards. Synchronous pushes were rejected because NEXT3 outages would then stop
/// experts working at accident scenes (§6.3).
///
/// Schema note (realized 2026-08-20, slice 2.2): §4 and HANDOFF §3 spell the second column
/// `claim_id uniqueidentifier`, but there is no uniqueidentifier claim id anywhere in the model —
/// the `claim` cache is keyed on `visa_no`, and §4 calls that cache disposable, which is why nothing
/// holds a foreign key to it. Every <see cref="Api.Integrations.Next3.INext3Client"/> push takes a
/// visa number and §5.4's A2 screen displays "claim/visa", so the column is `visa_no` and carries the
/// value the push actually needs. design.md §4 was corrected to match rather than diverged from.
///
/// This table must stay trigger-free: §6.3's dequeue uses `OUTPUT inserted.*`, and EF silently drops
/// off the OUTPUT clause for any table declared with a trigger (the `audit_log` lesson from 1.3).
/// </summary>
public sealed class Next3OutboxMessage
{
    public Guid Id { get; set; }

    /// <summary>The claim this write lands under. No FK — the `claim` cache is deletable at any time.</summary>
    public required string VisaNo { get; set; }

    /// <summary>One of <see cref="Next3OutboxOperations"/>.</summary>
    public required string Operation { get; set; }

    /// <summary>JSON: the clientRef plus the operation's fields (blob keys, values) — see <see cref="OutboxPayloads"/>.</summary>
    public required string Payload { get; set; }

    /// <summary>One of <see cref="Next3OutboxStatuses"/>.</summary>
    public required string Status { get; set; }

    /// <summary>
    /// Incremented by the dequeue statement, not by the worker: a worker that dies after claiming a
    /// row must still have that attempt counted, or a row that kills its worker retries forever.
    /// </summary>
    public int Attempts { get; set; }

    public string? LastError { get; set; }

    /// <summary>
    /// When the dequeue last claimed this row, i.e. when it was last *tried* — A2's "Last tried"
    /// column (§5.4, slice 6.2). Stamped by the dequeue statement rather than by the worker, for
    /// <see cref="Attempts"/>'s reason: the claim *is* the attempt, and a worker that dies before
    /// recording an outcome still tried.
    ///
    /// It needs its own column because no existing one can answer the question. <see cref="SentAt"/>
    /// is only ever set on success; <see cref="CreatedAt"/> is when the row was enqueued; and
    /// <see cref="NextRetryAt"/> is overwritten with the lease deadline the moment a row is claimed,
    /// so while a push is in flight it says a time in the *future*. Null on rows that predate this
    /// column and on rows never yet claimed — A2 renders an em dash rather than inventing a time.
    ///
    /// Deliberately not stamped by the dequeue's abandoned-row retire: retiring a row is giving up on
    /// it, not attempting it, and stamping there would make this column read "last given up on" for
    /// precisely the rows an admin is looking at.
    /// </summary>
    public DateTime? LastAttemptAt { get; set; }

    /// <summary>
    /// When this row next becomes eligible. Set to <see cref="CreatedAt"/> on enqueue so a new row is
    /// immediately due, to now + backoff after a failure, and to now + lease while claimed.
    /// </summary>
    public DateTime NextRetryAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? SentAt { get; set; }
}

/// <summary>The §4 nvarchar enum values for `next3_outbox.operation`.</summary>
public static class Next3OutboxOperations
{
    public const string UploadDocument = "upload_document";

    public const string UpdateArrival = "update_arrival";

    /// <summary>
    /// §5.2's approval PNG. <see cref="Api.Integrations.Next3.INext3Client"/> has no push_approval
    /// method — §6.2's interface exposes only RecordArrival and UploadDocument — so this dispatches to
    /// UploadDocument into the *Survey* folder, which is exactly what §5.2 describes it as. It stays a
    /// distinct operation value because §4 names it and because A2 needs to tell an approval push
    /// apart from a photo when a claim officer asks why an approval never arrived.
    /// </summary>
    public const string PushApproval = "push_approval";
}

/// <summary>The §4 nvarchar enum values for `next3_outbox.status`.</summary>
public static class Next3OutboxStatuses
{
    public const string Pending = "pending";

    public const string Processing = "processing";

    public const string Sent = "sent";

    /// <summary>Terminal until an admin retries it from A2 (§5.4, slice 6.2).</summary>
    public const string Failed = "failed";
}
