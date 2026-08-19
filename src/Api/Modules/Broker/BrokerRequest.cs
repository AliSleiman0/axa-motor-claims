namespace Api.Modules.Broker;

/// <summary>
/// design.md §4 <c>broker_request</c> — one row per broker submission, both options. The broker
/// module never touches NEXT3 (§5.3): its terminal act is an email routed by insurance type, which
/// lands in slice 5.2.
/// </summary>
/// <remarks>
/// The six form fields are nullable even though §4 lists them plainly: an Option 2 request exists
/// from the moment the link is issued, which is *before* the customer has entered anything.
/// Completeness is a precondition of the submit transition (§5.3), not a column constraint.
/// </remarks>
public sealed class BrokerRequest
{
    public Guid Id { get; set; }
    public Guid BrokerUserId { get; set; }

    /// <summary>1 = broker fills the form; 2 = the customer does, via a public link (§5.3).</summary>
    public int Option { get; set; }

    public BrokerRequestState State { get; set; }

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

    public DateTime? SubmittedAt { get; set; }
    public DateTime? EmailedAt { get; set; }

    /// <summary>Resolved from the <c>Broker.EmailRouting</c> placeholder (#13) at send time (slice 5.2).</summary>
    public string? EmailRecipient { get; set; }

    public DateTime CreatedAt { get; set; }
}
