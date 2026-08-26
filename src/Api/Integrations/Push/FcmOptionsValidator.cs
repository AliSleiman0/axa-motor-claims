using Microsoft.Extensions.Options;

namespace Api.Integrations.Push;

/// <summary>
/// Fail-fast for `Push:Fcm` (design.md §8, slice 6.3) — the third use of the options-validation
/// pipeline, and gated **twice** rather than once: it runs only when `Push:Mode` is `webpush`
/// <em>and</em> `Push:Fcm:Enabled` is true.
///
/// The second gate is what keeps Appendix A's shipped values honest. `Enabled` is false everywhere,
/// so the placeholder project id and the non-existent key path are the configuration every
/// environment currently runs on; a validator that fired on mode alone would stop `dotnet test`,
/// the demo and a fresh clone from booting — the exact regression
/// <c>FakeMode_StartsOnTheSamePlaceholders</c> exists to catch one layer up.
///
/// It is <c>ValidateOnStart</c> for <c>PushOptionsValidator</c>'s reason, which applies here with
/// more force: the FCM sender runs inside <c>CompositePushSender</c>, which is *designed* to swallow
/// one channel's failure so the other still delivers. A mistyped project id would therefore surface
/// as Android handsets quietly receiving nothing while desktop browsers kept working — a
/// single-platform silent outage, which is precisely the shape of bug slice 6.3a found on iOS and
/// this slice exists to make impossible.
/// </summary>
public sealed class FcmOptionsValidator : IValidateOptions<PushOptions>
{
    /// <summary>Appendix A's marker for "not a real value yet".</summary>
    private const string PlaceholderMarker = "PLACEHOLDER";

    public ValidateOptionsResult Validate(string? name, PushOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var fcm = options.Fcm;
        var failures = new List<string>();

        if (!IsSet(fcm.ProjectId))
        {
            failures.Add(
                "Push:Fcm:ProjectId is required when Push:Fcm:Enabled is true (currently "
                + $"{Describe(fcm.ProjectId)}). It is the project segment of the send URL, so a wrong "
                + "one answers 404 for every handset — and the sender deliberately does not revoke on "
                + "a bare 404, precisely so that a typo here cannot silence a whole fleet. Take it "
                + "from the Firebase console (Project settings -> General).");
        }

        if (!IsSet(fcm.ServiceAccountJsonPath))
        {
            failures.Add(
                "Push:Fcm:ServiceAccountJsonPath is required when Push:Fcm:Enabled is true "
                + $"(currently {Describe(fcm.ServiceAccountJsonPath)}). It points at the "
                + "service-account key that signs every send. It is a path rather than the key "
                + "itself so the credential stays a mounted secret (§10) — set it with "
                + "`dotnet user-secrets` locally, never in appsettings, never in a commit.");
        }
        else if (!File.Exists(fcm.ServiceAccountJsonPath))
        {
            // Checked here rather than at the first send for the reason in this class's summary: at
            // send time a missing file is one caught exception inside one channel of a composite
            // built to keep going, so it reads as "Android is quiet today" and nothing else.
            failures.Add(
                $"Push:Fcm:ServiceAccountJsonPath points at no file ('{fcm.ServiceAccountJsonPath}'). "
                + "In a container this is usually a secret that was declared but not mounted — the "
                + "path exists in configuration and nothing exists on disk.");
        }

        if (fcm.TimeoutSeconds <= 0)
        {
            failures.Add(
                $"Push:Fcm:TimeoutSeconds must be greater than zero (currently {fcm.TimeoutSeconds}).");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsSet(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !value.Contains(PlaceholderMarker, StringComparison.OrdinalIgnoreCase);

    /// <summary>Says what is wrong without echoing a value that might be most of a real secret.</summary>
    private static string Describe(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "empty"
        : value.Contains(PlaceholderMarker, StringComparison.OrdinalIgnoreCase) ? "the placeholder"
        : $"{value.Length} characters";
}
