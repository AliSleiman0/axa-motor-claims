namespace Api.Modules.Declarations;

/// <summary>
/// design.md §5.2's state machine row — one entity with a garage view (G1–G4) and an officer view
/// (O1–O2).
///
/// **The entity owns its transitions.** <see cref="State"/> has a private setter and every change
/// routes through <see cref="Apply"/>, which consults <see cref="DeclarationTransitions"/>. Nothing
/// outside this class assigns a state, so an illegal transition is not something a service has to
/// remember to check — it is something the type refuses to do.
///
/// One timestamp per transition rather than a single "changed at": §9's audit answer and the officer's
/// inbox both need to know *when* a declaration was submitted, distinct from when it was decided, and
/// a claims dispute turns on exactly that kind of ordering.
///
/// There is no closure or settlement state and no resubmit edge — §1 records both as deliberate.
/// </summary>
public sealed class Declaration
{
    public Guid Id { get; set; }

    public Guid GarageUserId { get; set; }

    /// <summary>
    /// §5.2's state, and the EF **concurrency token** (configured in `DeclarationConfiguration`).
    ///
    /// 2.4's note warned against putting a token on a column that not every write changes — that was
    /// `arrived_at`, where a concurrent `opened_at` save would have failed for no reason. `state` is
    /// the opposite case: it is what every transition changes, and comments live in their own table,
    /// so there is no write to this row that leaves it untouched. That makes it the once-only guard
    /// design.md and CLAUDE.md both ask for, in the schema rather than in an `if`.
    /// </summary>
    public DeclarationState State { get; private set; } = DeclarationState.Draft;

    /// <summary>
    /// Required (§1's interpretation of a form the BRD does not define): it is the key the officer
    /// searches NEXT3 with, so a declaration without one cannot be linked to a visa at all.
    /// </summary>
    public required string PlateNo { get; set; }

    public string? InsuredName { get; set; }

    public string? Note { get; set; }

    /// <summary>
    /// Set at approval and never cleared. No FK to `claim`: §4 makes that cache disposable, and a
    /// foreign key would let deleting a throwaway row orphan a linked declaration.
    /// </summary>
    public string? VisaNo { get; private set; }

    public Guid? OfficerUserId { get; private set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? SubmittedAt { get; private set; }

    public DateTime? DecidedAt { get; private set; }

    public DateTime? RepairsStartedAt { get; private set; }

    public DateTime? RepairDocsSubmittedAt { get; private set; }

    /// <summary>Garage submits the declaration for review (§5.2). Draft → Submitted.</summary>
    public void Submit(DateTime at)
    {
        Apply(DeclarationTransition.Submit);
        SubmittedAt = at;
    }

    /// <summary>
    /// Officer approves and links the declaration to a visa (§5.2). Submitted → Approved.
    ///
    /// The visa arrives here already verified against NEXT3 by the caller — an approval carrying a
    /// visa NEXT3 does not know would surface 26 hours later as a `failed` push, which is the failure
    /// mode this whole project exists to remove.
    /// </summary>
    public void Approve(Guid officerUserId, string visaNo, DateTime at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(visaNo);

        Apply(DeclarationTransition.Approve);
        VisaNo = visaNo;
        OfficerUserId = officerUserId;
        DecidedAt = at;
    }

    /// <summary>
    /// Officer rejects — the BRD's "Disregard Case" (§5.2). Submitted → Rejected, and **terminal**:
    /// no visa is set, nothing is pushed to NEXT3, and there is no edge back out.
    /// </summary>
    public void Reject(Guid officerUserId, DateTime at)
    {
        Apply(DeclarationTransition.Reject);
        OfficerUserId = officerUserId;
        DecidedAt = at;
    }

    /// <summary>Garage begins repairs (§5.2). Approved → RepairsInProgress.</summary>
    public void StartRepairs(DateTime at)
    {
        Apply(DeclarationTransition.StartRepairs);
        RepairsStartedAt = at;
    }

    /// <summary>
    /// The last row of §5.2's table (G4). RepairsInProgress → RepairDocsSubmitted, terminal.
    ///
    /// The edge lives here because §5.2's table is the contract and the state is in §4's check
    /// constraint either way; **slice 5.1 owns G4's endpoint, its repair buckets and its media**, so
    /// nothing in the application calls this yet.
    /// </summary>
    public void SubmitRepairDocs(DateTime at)
    {
        Apply(DeclarationTransition.SubmitRepairDocs);
        RepairDocsSubmittedAt = at;
    }

    private void Apply(DeclarationTransition via) =>
        State = DeclarationTransitions.Target(State, via)
            ?? throw new IllegalTransitionException(State, via);
}
