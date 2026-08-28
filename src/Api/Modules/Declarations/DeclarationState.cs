namespace Api.Modules.Declarations;

/// <summary>
/// design.md §5.2's six states. One entity, two views — the garage's (G1–G4) and the claim officer's
/// (O1–O2) — so both read this enum rather than each keeping its own idea of the lifecycle.
///
/// <see cref="Rejected"/> and <see cref="RepairDocsSubmitted"/> are both terminal. §1 is explicit that
/// rejection has no resubmit edge (the garage files a new declaration) and that the lifecycle ends at
/// *repair documents submitted* — the BRD defines no closure or settlement state and we do not invent
/// one.
/// </summary>
public enum DeclarationState
{
    Draft,
    Submitted,
    Approved,
    Rejected,
    RepairsInProgress,
    RepairDocsSubmitted,
}

/// <summary>The §4 nvarchar enum values — the single source for state strings in DB and API.</summary>
public static class DeclarationStates
{
    public const string Draft = "draft";
    public const string Submitted = "submitted";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string RepairsInProgress = "repairs_in_progress";
    public const string RepairDocsSubmitted = "repair_docs_submitted";

    /// <summary>The check-constraint list; kept next to the constants so the two cannot drift.</summary>
    public static readonly string[] All =
    [
        Draft, Submitted, Approved, Rejected, RepairsInProgress, RepairDocsSubmitted,
    ];

    /// <summary>
    /// The states a decision has been recorded for. Both imply an officer and a `decided_at`, which is
    /// half of `CK_declaration_decision`.
    /// </summary>
    public static readonly string[] Decided =
    [
        Approved, Rejected, RepairsInProgress, RepairDocsSubmitted,
    ];

    /// <summary>
    /// The states that carry a visa. §5.2 sets `visa_no` at approval and never clears it, so every
    /// state at or past `approved` has one and no earlier state does — the other half of
    /// `CK_declaration_decision`.
    /// </summary>
    public static readonly string[] Linked =
    [
        Approved, RepairsInProgress, RepairDocsSubmitted,
    ];

    /// <summary>
    /// <see cref="Linked"/> as the enum, for LINQ (slice 7.2's re-queue sweep). Kept immediately
    /// beside its string twin so the two cannot drift: one is what the check constraint tests, the
    /// other is what the query tests, and they are the same claim about the same states.
    /// </summary>
    public static readonly DeclarationState[] LinkedStates =
    [
        DeclarationState.Approved,
        DeclarationState.RepairsInProgress,
        DeclarationState.RepairDocsSubmitted,
    ];

    public static string ToDbValue(this DeclarationState state) => state switch
    {
        DeclarationState.Draft => Draft,
        DeclarationState.Submitted => Submitted,
        DeclarationState.Approved => Approved,
        DeclarationState.Rejected => Rejected,
        DeclarationState.RepairsInProgress => RepairsInProgress,
        DeclarationState.RepairDocsSubmitted => RepairDocsSubmitted,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    public static DeclarationState FromDbValue(string value) => value switch
    {
        Draft => DeclarationState.Draft,
        Submitted => DeclarationState.Submitted,
        Approved => DeclarationState.Approved,
        Rejected => DeclarationState.Rejected,
        RepairsInProgress => DeclarationState.RepairsInProgress,
        RepairDocsSubmitted => DeclarationState.RepairDocsSubmitted,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };
}

/// <summary>The named edges of §5.2's table — one per row, so the table can be indexed by them.</summary>
public enum DeclarationTransition
{
    Submit,
    Approve,
    Reject,
    StartRepairs,
    SubmitRepairDocs,
}

/// <summary>
/// design.md §5.2's transition table, as data — the same treatment §7.1 gets in
/// <see cref="Api.Modules.Media.MediaBuckets"/>, and for the same reason: a rule that lives only in a
/// document is a rule that gets misremembered, and a rule spread across four `if`s in a service is a
/// rule with four places to disagree with itself.
///
/// Being a table rather than a switch inside <see cref="Declaration"/> is what makes the illegal pairs
/// testable **by construction**: the suite walks every (state, transition) combination the enums can
/// express and asserts the table's answer, so a new state cannot be added without the exhaustive test
/// noticing. <see cref="Declaration"/> holds no second copy — its methods all route through
/// <see cref="Target"/>.
/// </summary>
public static class DeclarationTransitions
{
    private static readonly Dictionary<(DeclarationState From, DeclarationTransition Via), DeclarationState> Table =
        new()
        {
            [(DeclarationState.Draft, DeclarationTransition.Submit)] = DeclarationState.Submitted,
            [(DeclarationState.Submitted, DeclarationTransition.Approve)] = DeclarationState.Approved,

            // Terminal, and deliberately with no edge back out: §1 records that rejection is
            // "Disregard Case" and that the garage files a new declaration rather than editing this
            // one. There is no resubmit row in §5.2's table and there is none here.
            [(DeclarationState.Submitted, DeclarationTransition.Reject)] = DeclarationState.Rejected,

            [(DeclarationState.Approved, DeclarationTransition.StartRepairs)] =
                DeclarationState.RepairsInProgress,

            // The last row of §5.2's table. The *entity* edge exists here because the table is the
            // contract and a table with a missing row is a table nobody can trust; G4's endpoint,
            // buckets and repair media are slice 5.1.
            [(DeclarationState.RepairsInProgress, DeclarationTransition.SubmitRepairDocs)] =
                DeclarationState.RepairDocsSubmitted,
        };

    /// <summary>The state this transition leads to, or null when §5.2's table has no such row.</summary>
    public static DeclarationState? Target(DeclarationState from, DeclarationTransition via) =>
        Table.TryGetValue((from, via), out var to) ? to : null;

    /// <summary>Every legal edge, for the suite that asserts the table row by row.</summary>
    public static IReadOnlyCollection<(DeclarationState From, DeclarationTransition Via, DeclarationState To)>
        All => [.. Table.Select(e => (e.Key.From, e.Key.Via, e.Value))];
}
