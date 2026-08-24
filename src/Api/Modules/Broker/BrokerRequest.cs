namespace Api.Modules.Broker;

/// <summary>
/// design.md §4 <c>broker_request</c> — one row per broker submission, both options. The broker
/// module never touches NEXT3 (§5.3): its terminal act is an email routed by insurance type, which
/// landed in slice 5.2.
/// </summary>
/// <remarks>
/// The six form fields are nullable even though §4 lists them plainly: an Option 2 request exists
/// from the moment the link is issued, which is *before* the customer has entered anything.
/// Completeness is a precondition of the submit transition (§5.3), not a column constraint.
/// </remarks>
public sealed class BrokerRequest
{
    /// <summary>Private so the two factories below are the only ways in; EF Core materialises with it.</summary>
    private BrokerRequest()
    {
    }

    public Guid Id { get; set; }
    public Guid BrokerUserId { get; set; }

    /// <summary>1 = broker fills the form; 2 = the customer does, via a public link (§5.3).</summary>
    public int Option { get; set; }

    /// <summary>
    /// §5.3's state, and the EF **concurrency token** (configured in `BrokerRequestConfiguration`) —
    /// 4.1's idiom. It is what every transition changes, so it is the once-only guard for a submit in
    /// the schema rather than in an `if`: without it, four simultaneous submits all read `draft`, all
    /// pass, and AXA's desk receives four copies of one quotation request.
    ///
    /// **Unlike `Declaration`, this row does have writes that leave the state alone**, which is 2.4's
    /// objection to the idiom and is worth being honest about rather than repeating 4.1's sentence:
    /// <see cref="MarkEmailed"/> writes only the two email columns. Both are handled where they
    /// happen rather than by weakening the token — `MarkEmailed` runs immediately after the
    /// transition it belongs to, and §5.3's public first-open swallows a lost race because the row
    /// already says what that write was trying to say. The one thing the token cannot arbitrate is a
    /// **Resend**, which changes no state at all; that is claimed on `emailed_at` instead
    /// (<c>BrokerRequestService.TrySend</c>).
    ///
    /// The setter is private for the same reason `Declaration.State`'s is: the legal edges are the
    /// methods below, not whatever a caller assigns.
    /// </summary>
    public BrokerRequestState State { get; private set; } = BrokerRequestState.Draft;

    /// <summary>
    /// The broker's display name as it stood when the request was created (slice 5.2).
    ///
    /// A **snapshot**, denormalised deliberately: §5.3's public page names the broker who sent the
    /// link, and architecture rule 2 forbids `Api.Modules.PublicSurface` from reading `Users` at all.
    /// Copying the name at issue time is what lets the page be friendly without weakening that
    /// boundary. Null on every row written before 5.2, and the page copes.
    /// </summary>
    public string? BrokerDisplayName { get; set; }

    public string? InsuredName { get; set; }

    /// <summary>From the <c>Broker.InsuranceTypes</c> placeholder list (#14) — never a literal.</summary>
    public string? InsuranceType { get; set; }

    public string? InsuredAddress { get; set; }
    public decimal? CarValue { get; set; }

    /// <summary>Customer-entered in Option 2, per §1's recorded decision (flagged #24c).</summary>
    public decimal? EstimatedPremium { get; set; }

    public DateOnly? EffectiveDate { get; set; }

    /// <summary>Option 2 only: where the broker sends the link (#24a decides the channel).</summary>
    public string? CustomerMobile { get; set; }

    public DateTime? SubmittedAt { get; private set; }

    /// <summary>
    /// When the routed email actually left (slice 5.2). **Null while a submitted request's send has
    /// failed** — the state commits before the send, so this column is the difference between "filed
    /// and delivered" and "filed, delivery still owed", which is what B1 shows and what
    /// `BrokerMediaCleanupTask` keys its retention on.
    /// </summary>
    public DateTime? EmailedAt { get; private set; }

    /// <summary>Resolved from the <c>Broker.EmailRouting</c> placeholder (#13) at send time.</summary>
    public string? EmailRecipient { get; private set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// B2's Option 1 row (§5.3). Starts at `draft`; the six fields arrive together because there is no
    /// edit endpoint.
    /// </summary>
    public static BrokerRequest NewDraft(
        Guid brokerUserId, string? brokerDisplayName, DateTime at) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BrokerUserId = brokerUserId,
            Option = 1,
            BrokerDisplayName = brokerDisplayName,
            CreatedAt = at,
        };

    /// <summary>
    /// B3's Option 2 row (§5.3). **Starts at `link_issued`, not `draft`** — B1's rule is that a row
    /// appears the moment a link is issued, before the customer has typed anything, and the fields are
    /// genuinely empty until they do.
    ///
    /// A factory rather than an object initializer because <see cref="State"/> has a private setter:
    /// the legal entry points are these two, so no caller can invent a state the machine has no edge
    /// into.
    /// </summary>
    public static BrokerRequest NewLink(
        Guid brokerUserId, string? brokerDisplayName, string? insuranceType, string? customerMobile,
        DateTime at) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BrokerUserId = brokerUserId,
            Option = 2,
            State = BrokerRequestState.LinkIssued,
            BrokerDisplayName = brokerDisplayName,
            InsuranceType = insuranceType,
            CustomerMobile = customerMobile,
            CreatedAt = at,
        };

    /// <summary>Option 1's only transition (§5.3): Draft → Submitted, set before the email is sent.</summary>
    public void Submit(DateTime at)
    {
        Require(BrokerRequestState.Draft, nameof(Submit));
        State = BrokerRequestState.Submitted;
        SubmittedAt = at;
    }

    /// <summary>
    /// Option 2: the customer has opened the link (§5.3 P1). LinkIssued → CustomerInProgress.
    ///
    /// A method rather than a bare assignment since slice 5.2 gave <see cref="State"/> a private
    /// setter; the caller (`PublicLinkTokenService`) already guarded the source state, and the guard
    /// is repeated here for the reason <see cref="Require"/> exists.
    /// </summary>
    public void OpenByCustomer()
    {
        Require(BrokerRequestState.LinkIssued, nameof(OpenByCustomer));
        State = BrokerRequestState.CustomerInProgress;
    }

    /// <summary>
    /// Option 2: the customer has submitted and the token is locked (§5.3 P1). → ReadyToSend.
    ///
    /// Deliberately accepts both `link_issued` and `customer_in_progress`: §9.1's token may be opened
    /// and submitted in one pass, and the lock — not the intermediate state — is what makes this
    /// once-only.
    /// </summary>
    public void ReadyToSend(DateTime at)
    {
        if (State is not (BrokerRequestState.LinkIssued or BrokerRequestState.CustomerInProgress))
        {
            throw new InvalidOperationException(
                $"A broker request in state '{State.ToDbValue()}' cannot become ready to send "
                + "(design.md §5.3).");
        }

        State = BrokerRequestState.ReadyToSend;
        SubmittedAt = at;
    }

    /// <summary>
    /// Records a delivered email. **Not a state transition** — it is called on the same request both
    /// at submit time and by a later Resend, and a Resend must not move a request that has already
    /// reached its terminal state.
    /// </summary>
    public void MarkEmailed(string recipient, DateTime at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);

        EmailRecipient = recipient;
        EmailedAt = at;
    }

    /// <summary>
    /// Only ever a programmer error: every endpoint checks the state first and answers
    /// `409 illegal_transition`, and the concurrency token is what makes that check safe under a race.
    /// This is the backstop that keeps an illegal edge unrepresentable rather than merely unreached.
    /// </summary>
    private void Require(BrokerRequestState expected, string transition)
    {
        if (State != expected)
        {
            throw new InvalidOperationException(
                $"A broker request in state '{State.ToDbValue()}' cannot {transition} "
                + $"(design.md §5.3 allows it only from '{expected.ToDbValue()}').");
        }
    }
}
