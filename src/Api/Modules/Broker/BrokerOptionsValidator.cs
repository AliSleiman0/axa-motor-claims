using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace Api.Modules.Broker;

/// <summary>
/// Fail-fast for design.md Appendix A's `Broker` section (#13, #14), via <c>ValidateOnStart</c>.
///
/// **Registered unconditionally, which is the one way this departs from the 3.3 pattern.**
/// <c>Next3OptionsValidator</c> and <c>PushOptionsValidator</c> run only in their live mode, because
/// every value they check is a `PLACEHOLDER` that must still let the application boot. The broker
/// placeholders are different in kind: three real-looking types, three routes, three syntactically
/// valid `@example.invalid` addresses. They pass this validator today, so it costs nothing to run
/// everywhere — and it turns "the routing table is wrong" from a 500 the first broker sees, for one
/// insurance type only, into a container that refuses to start.
///
/// The message convention is the repo's: name the exact configuration key, say what was expected,
/// cite the open question that is blocking it.
/// </summary>
public sealed class BrokerOptionsValidator : IValidateOptions<BrokerOptions>
{
    public ValidateOptionsResult Validate(string? name, BrokerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.InsuranceTypes.Count == 0)
        {
            failures.Add(
                "Broker:InsuranceTypes must list at least one insurance type (#14). "
                + "An empty list leaves a broker with nothing to choose and no route to send to.");
        }

        if (options.InsuranceTypes.Any(string.IsNullOrWhiteSpace))
        {
            failures.Add("Broker:InsuranceTypes contains a blank entry (#14).");
        }

        var duplicates = options.InsuranceTypes
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .GroupBy(t => t, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            failures.Add(
                $"Broker:InsuranceTypes lists {Quote(duplicates)} more than once (#14). "
                + "A duplicate type is one entry in the routing table pretending to be two.");
        }

        foreach (var type in options.InsuranceTypes.Where(t => !string.IsNullOrWhiteSpace(t)))
        {
            if (!options.EmailRouting.TryGetValue(type, out var recipient))
            {
                failures.Add(
                    $"Broker:EmailRouting has no entry for the insurance type '{type}' (#13). "
                    + "Every type a broker can choose must resolve to a recipient, or submitting that "
                    + "type is the only thing in the application that fails.");
                continue;
            }

            if (!IsAddress(recipient))
            {
                failures.Add(
                    $"Broker:EmailRouting['{type}'] is not a valid email address "
                    + $"(currently '{recipient}'; the real recipients are #13).");
            }
        }

        // An orphan route is a mistyped type: the table looks complete, the type it was meant for is
        // unroutable, and nothing says so until a broker picks it. Both halves are checked because
        // either one alone leaves that mistake representable.
        var orphans = options.EmailRouting.Keys
            .Where(k => !options.InsuranceTypes.Contains(k, StringComparer.Ordinal))
            .ToList();

        if (orphans.Count > 0)
        {
            failures.Add(
                $"Broker:EmailRouting routes {Quote(orphans)}, which Broker:InsuranceTypes does not "
                + "offer (#13/#14). A route with no type is dead configuration, and the likeliest "
                + "reason for one is that the type beside it is misspelt.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>
    /// Syntax only, deliberately. `PLACEHOLDER-recipient-1@example.invalid` must pass — `.invalid` is
    /// reserved precisely so it can never resolve — so anything that reached for deliverability would
    /// refuse to boot on the configuration every environment runs on today.
    /// </summary>
    private static bool IsAddress(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && MailAddress.TryCreate(value, out var address)
        && address.Host.Contains('.', StringComparison.Ordinal);

    private static string Quote(IEnumerable<string> values) =>
        string.Join(", ", values.Select(v => $"'{v}'"));
}
