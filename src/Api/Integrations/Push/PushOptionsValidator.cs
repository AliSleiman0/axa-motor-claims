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
        else if (HostOf(options.Vapid.Subject) is not { } host || IsUnroutable(host))
        {
            failures.Add(
                $"Push:Vapid:Subject must be **routable** (currently '{options.Vapid.Subject}'). "
                + "Apple validates the VAPID JWT's `sub` claim and answers 403 BadJwtToken to a "
                + "reserved or unresolvable contact — so every iPhone silently receives nothing "
                + "while Android and desktop keep working, and the only trace is a `failed` row in "
                + "the `notification` log that nobody reads until an expert says they never got a "
                + "claim. Observed on a real handset in slice 6.3a with "
                + "'mailto:...@example.invalid'. Use a real AXA mailbox or the deployed origin — "
                + "this is the one Appendix A value that cannot stay obviously fake (§10's "
                + "deployment checklist).");
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

    /// <summary>
    /// The host a push service would have to reach, or null if the subject does not carry one.
    ///
    /// Two shapes, because RFC 8292 allows two: `mailto:` is not a URI with a host — <c>Uri.Host</c>
    /// on one is empty — so the address is split on its **last** <c>@</c>, which is the one that
    /// separates local part from domain (a quoted local part may legally contain others).
    /// </summary>
    private static string? HostOf(string subject)
    {
        if (subject.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            var address = subject["mailto:".Length..];
            var at = address.LastIndexOf('@');
            return at > 0 && at < address.Length - 1 ? address[(at + 1)..] : null;
        }

        return Uri.TryCreate(subject, UriKind.Absolute, out var uri) && uri.Host.Length > 0
            ? uri.Host
            : null;
    }

    /// <summary>
    /// Whether the host is one the DNS deliberately cannot resolve — the RFC 2606 / RFC 6761
    /// reserved set, which is exactly where a placeholder reaches for.
    ///
    /// Suffix-matched as well as compared, because `a.example.com` and `mail.corp.invalid` are as
    /// unreachable as their parents, and a validator that only rejected the bare names would be one
    /// subdomain away from the outage it exists to prevent.
    /// </summary>
    private static bool IsUnroutable(string host)
    {
        foreach (var reserved in ReservedHosts)
        {
            if (host.Equals(reserved, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith($".{reserved}", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// RFC 2606 §2's reserved TLDs plus RFC 6761's `localhost`, and §3's three second-level names.
    /// Not client data and not a threshold — a fixed list from a standard, so it lives here rather
    /// than in Appendix A, the same treatment as <c>Push:AllowedEndpointHosts</c>' real values.
    /// </summary>
    private static readonly string[] ReservedHosts =
    [
        "localhost", "invalid", "example", "test",
        "example.com", "example.net", "example.org",
    ];

    private static bool IsSet(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !value.Contains(PlaceholderMarker, StringComparison.OrdinalIgnoreCase);

    /// <summary>Says what is wrong without echoing a value that might be most of a real key.</summary>
    private static string Describe(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "empty"
        : value.Contains(PlaceholderMarker, StringComparison.OrdinalIgnoreCase) ? "the placeholder"
        : $"{value.Length} characters";
}
