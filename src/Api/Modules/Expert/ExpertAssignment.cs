namespace Api.Modules.Expert;

/// <summary>
/// A claim NEXT3 assigned to an expert (design.md §4, §5.1). Event-sourced from NEXT3 and
/// authoritative here.
///
/// **Timestamps, not a state enum** (§4, verbatim): the BRD defines no expert-side lifecycle — no
/// submit, no complete — so this slice does not invent one. An assignment accumulates media
/// indefinitely (§1's recorded interpretation).
/// </summary>
public sealed class ExpertAssignment
{
    public Guid Id { get; set; }

    public required string VisaNo { get; set; }

    public Guid ExpertUserId { get; set; }

    /// <summary>
    /// The dedupe key of §6.2. A replayed webhook or an overlapping poll must be a no-op, and the
    /// guard for that is the unique index on this column — not a read-then-write in the handler.
    /// </summary>
    public required string Next3AssignmentRef { get; set; }

    public DateTime ReceivedAt { get; set; }

    /// <summary>Set only when the push actually succeeded; a failed send leaves it null.</summary>
    public DateTime? NotifiedAt { get; set; }

    /// <summary>First open of E2, never overwritten.</summary>
    public DateTime? OpenedAt { get; set; }

    // Written by the Arrived button in slice 2.4. Created now so next week is one less migration:
    // the columns are §4's, and adding them later would churn a table that already has rows.
    public DateTime? ArrivedAt { get; set; }

    public double? ArrivalLat { get; set; }

    public double? ArrivalLng { get; set; }
}
