using Microsoft.Extensions.Options;

namespace Api.Integrations.Push;

/// <summary>
/// Fail-fast for `Push:Mode = webpush` (design.md §8), the second use of the options-validation
/// pipeline after <c>Next3OptionsValidator</c> and registered the same way: **only in the live mode**,
/// via <c>ValidateOnStart</c>, never in a constructor.
///
/// The reason it is gated is the same one, and it is not hypothetical: `Push:Mode` is `fake`
/// everywhere and Appendix A's `Push:Vapid:*` are placeholders, so a validator that always ran would
/// stop the application booting on the configuration every environment currently uses.
///
/// The reason it is <c>ValidateOnStart</c> rather than a check inside the sender is also the same:
/// the sender runs inside <c>AssignmentHandler</c>, where a throw is caught and turned into
/// "notified_at stays null". A misconfigured key would therefore surface as experts quietly not being
/// told about claims — the failure this whole application exists to prevent — instead of as a
/// deployment that refused to start.
/// </summary>
public sealed class PushOptionsValidator : IValidateOptions<PushOptions>
{
    /// <summary>Appendix A's marker for "not a real value yet".</summary>
    private const string PlaceholderMarker = "PLACEHOLDER";

    /// <summary>
    /// A P-256 public key is the uncompressed point: 65 bytes, which is 87 base64url characters.
    /// Checked because the failure it prevents is silent — a truncated key is accepted by the
    /// browser's subscribe call and then every push fails to decrypt, on the device, with nothing
    /// server-side to see.
    /// </summary>
    private const int PublicKeyLength = 87;

    public ValidateOptionsResult Validate(string? name, PushOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (!IsSet(options.Vapid.Subject)
            || !(options.Vapid.Subject.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                || options.Vapid.Subject.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            failures.Add(
                "Push:Vapid:Subject must be a 'mailto:' or 'https://' contact when Push:Mode is "
                + $"'{PushModes.WebPush}' (currently '{options.Vapid.Subject}'). Push services use it "
                + "to reach whoever is sending, and some reject a request without it (RFC 8292).");
        }

        if (!IsSet(options.Vapid.PublicKey) || options.Vapid.PublicKey.Length != PublicKeyLength)
        {
            failures.Add(
                $"Push:Vapid:PublicKey must be a {PublicKeyLength}-character base64url P-256 key when "
                + $"Push:Mode is '{PushModes.WebPush}' (currently {Describe(options.Vapid.PublicKey)}). "
                + "Generate a pair with `npx --yes web-push generate-vapid-keys` — see CLAUDE.md.");
        }

        if (!IsSet(options.Vapid.PrivateKey))
        {
            // The value is never quoted back, unlike every other message in this file: it is a
            // credential, and validation failures land in deployment logs.
            failures.Add(
                $"Push:Vapid:PrivateKey is required when Push:Mode is '{PushModes.WebPush}'. It is a "
                + "credential: set it with `dotnet user-secrets` locally or a Container Apps secret in "
                + "deployment, never in appsettings (see CLAUDE.md).");
        }

        if (options.TimeoutSeconds <= 0)
        {
            failures.Add(
                $"Push:TimeoutSeconds must be greater than zero (currently {options.TimeoutSeconds}).");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsSet(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !value.Contains(PlaceholderMarker, StringComparison.OrdinalIgnoreCase);

    /// <summary>Says what is wrong without echoing a value that might be most of a real key.</summary>
    private static string Describe(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "empty"
        : value.Contains(PlaceholderMarker, StringComparison.OrdinalIgnoreCase) ? "the placeholder"
        : $"{value.Length} characters";
}
