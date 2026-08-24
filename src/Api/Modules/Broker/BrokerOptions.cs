namespace Api.Modules.Broker;

/// <summary>
/// design.md Appendix A's `Broker` section — the routing table (#13), the insurance-type list (#14),
/// and the BRD's upload kill-switch.
///
/// **The section has existed since week 0 and was bound by nothing until slice 5.2.** Every value is a
/// placeholder, but unlike the `Next3` and `Push` sections the placeholders here are *well-formed* —
/// three types, three routes, three `@example.invalid` addresses — which is why
/// <see cref="BrokerOptionsValidator"/> runs in every environment rather than behind a mode switch.
///
/// Read through <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/> and never
/// captured at startup: the placeholder file is loaded with <c>reloadOnChange</c>, and
/// <see cref="AllowUpload"/> is a switch AXA is meant to be able to throw per environment without a
/// release (§7.1's Broker Option 1 row).
/// </summary>
public sealed class BrokerOptions
{
    public const string SectionName = "Broker";

    /// <summary>
    /// The BRD's Option 1 kill-switch. False removes the file picker from B2 and refuses an
    /// <c>uploaded</c> provenance on the broker bucket — the point of the switch is that AXA can
    /// insist on photographs taken in the app, so the control has to disappear rather than grey out.
    /// </summary>
    public bool AllowUpload { get; set; } = true;

    /// <summary>
    /// The insurance types a broker may choose (#14). The first two are the BRD's own examples; the
    /// rest are obvious fakes. This choice is what decides where the email goes.
    /// </summary>
    public IList<string> InsuranceTypes { get; } = [];

    /// <summary>
    /// Insurance type → the AXA mailbox that receives it (#13). Ordinal, matching
    /// <c>Next3Options.DocTypes</c>: a routing key is compared against a stored value, and a
    /// culture-sensitive lookup would be a different table in a different locale.
    /// </summary>
    public IDictionary<string, string> EmailRouting { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
