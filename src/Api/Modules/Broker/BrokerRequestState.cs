namespace Api.Modules.Broker;

/// <summary>
/// The design.md §5.3 states for both broker options in one enum, because both live in one
/// <c>broker_request</c> row: Option 1 runs Draft → Submitted, Option 2 runs LinkIssued →
/// CustomerInProgress → ReadyToSend → Sent (+ Expired if the token lapses first).
/// </summary>
public enum BrokerRequestState
{
    Draft,
    Submitted,
    LinkIssued,
    CustomerInProgress,
    ReadyToSend,
    Sent,
    Expired,
}

/// <summary>The §4 nvarchar enum values — the single source for state strings in DB and API.</summary>
public static class BrokerRequestStates
{
    public const string Draft = "draft";
    public const string Submitted = "submitted";
    public const string LinkIssued = "link_issued";
    public const string CustomerInProgress = "customer_in_progress";
    public const string ReadyToSend = "ready_to_send";
    public const string Sent = "sent";
    public const string Expired = "expired";

    /// <summary>The check-constraint list; kept next to the constants so the two cannot drift.</summary>
    public static readonly string[] All =
    [
        Draft, Submitted, LinkIssued, CustomerInProgress, ReadyToSend, Sent, Expired,
    ];

    public static string ToDbValue(this BrokerRequestState state) => state switch
    {
        BrokerRequestState.Draft => Draft,
        BrokerRequestState.Submitted => Submitted,
        BrokerRequestState.LinkIssued => LinkIssued,
        BrokerRequestState.CustomerInProgress => CustomerInProgress,
        BrokerRequestState.ReadyToSend => ReadyToSend,
        BrokerRequestState.Sent => Sent,
        BrokerRequestState.Expired => Expired,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    public static BrokerRequestState FromDbValue(string value) => value switch
    {
        Draft => BrokerRequestState.Draft,
        Submitted => BrokerRequestState.Submitted,
        LinkIssued => BrokerRequestState.LinkIssued,
        CustomerInProgress => BrokerRequestState.CustomerInProgress,
        ReadyToSend => BrokerRequestState.ReadyToSend,
        Sent => BrokerRequestState.Sent,
        Expired => BrokerRequestState.Expired,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };
}
