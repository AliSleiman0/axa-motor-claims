using Microsoft.Extensions.Options;

namespace Api.Integrations.Next3;

/// <summary>
/// Fail-fast for `Next3:AssignmentSource = oracle-poll` (design.md §6.2, #34).
///
/// **Registered only when `Next3:AssignmentSource` is `oracle-poll`, deliberately independent of
/// <see cref="Next3OptionsValidator"/>.** `Next3:Mode` (claim operations) and
/// `Next3:AssignmentSource` (assignment delivery) are separate switches — a deployment can run one
/// in `real` and the other in `fake`, or the reverse — so folding this into the mode-gated
/// validator would either under- or over-validate depending on which switch was flipped. Same
/// reasons as <see cref="Next3OptionsValidator"/> for running via `ValidateOnStart` rather than
/// constructor validation: `Next3:AssignmentSource` stays `fake` everywhere until real Oracle
/// connection details land, so an always-on validator would refuse to let the application boot at
/// all, and the value is only needed inside a background worker, not at the point it is read.
/// </summary>
public sealed class Next3OracleOptionsValidator : IValidateOptions<Next3Options>
{
    private const string PlaceholderMarker = "PLACEHOLDER";

    public ValidateOptionsResult Validate(string? name, Next3Options options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        var oracle = options.Oracle;

        if (!IsSet(oracle.Host))
        {
            failures.Add(
                $"Next3:Oracle:Host is required when Next3:AssignmentSource is 'oracle-poll' "
                + $"(currently '{oracle.Host}'; see #34).");
        }

        if (oracle.Port <= 0)
        {
            failures.Add(
                $"Next3:Oracle:Port must be greater than zero (currently {oracle.Port}; #34).");
        }

        if (!IsSet(oracle.ServiceName))
        {
            failures.Add(
                $"Next3:Oracle:ServiceName is required when Next3:AssignmentSource is 'oracle-poll' "
                + $"(currently '{oracle.ServiceName}'; see #34).");
        }

        if (!IsSet(oracle.Username))
        {
            failures.Add(
                $"Next3:Oracle:Username is required when Next3:AssignmentSource is 'oracle-poll' "
                + $"(currently '{oracle.Username}'; see #34).");
        }

        if (!IsSet(oracle.Password))
        {
            failures.Add(
                $"Next3:Oracle:Password is required when Next3:AssignmentSource is 'oracle-poll' "
                + $"(currently '{oracle.Password}'; see #34).");
        }

        if (options.AssignmentPollSeconds <= 0)
        {
            failures.Add(
                $"Next3:AssignmentPollSeconds must be greater than zero (currently "
                + $"{options.AssignmentPollSeconds}). The client's own answer is 'every 15 seconds' (#34).");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsSet(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !value.Contains(PlaceholderMarker, StringComparison.OrdinalIgnoreCase);
}
