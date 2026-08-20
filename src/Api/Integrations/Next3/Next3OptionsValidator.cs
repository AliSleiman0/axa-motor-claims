using Microsoft.Extensions.Options;

namespace Api.Integrations.Next3;

/// <summary>
/// Fail-fast for `Next3:Mode = real` (design.md §6.2, #1).
///
/// **Registered only in real mode, and deliberately not written as constructor validation.** Two
/// reasons. `Next3:Mode` is `fake` everywhere until #1 answers, and every value in Appendix A's
/// `Next3` section is a PLACEHOLDER — so a validator that always ran would refuse to let the
/// application boot at all. And <c>PortSelectionTests.Next3Mode_Real_ResolvesRealClient</c> resolves
/// the real client from a bare service collection carrying no `Next3:*` settings, proving the DI
/// switch rather than the configuration; validating in the client's constructor would break that
/// test for a reason that has nothing to do with what it asserts.
///
/// So this runs at host startup, via <c>ValidateOnStart</c>. The first repo use of the options
/// validation pipeline — every earlier fail-fast (<c>AzureBlobStore</c>'s connection string,
/// <c>MediaUploadService</c>'s doc-type lookup) throws eagerly from the code that needs the value,
/// which does not work here because the value is needed inside a background worker, hours later,
/// where the only witness is a `failed` outbox row.
///
/// The message convention is the repo's: name the exact configuration key, say what was expected,
/// and cite the open question that is blocking it.
/// </summary>
public sealed class Next3OptionsValidator : IValidateOptions<Next3Options>
{
    /// <summary>
    /// Appendix A's marker for "no client answer yet". Any value still carrying it is unset, however
    /// well-formed it looks — `https://PLACEHOLDER-next3.example` is a valid absolute URI and would
    /// otherwise sail through, then fail at 3 a.m. against a host that does not exist.
    /// </summary>
    private const string PlaceholderMarker = "PLACEHOLDER";

    public ValidateOptionsResult Validate(string? name, Next3Options options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (!IsSet(options.BaseUrl) || !Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            failures.Add(
                "Next3:BaseUrl must be an absolute http(s) URL when Next3:Mode is 'real' "
                + $"(currently '{options.BaseUrl}'; the sandbox URL is open question #1).");
        }

        switch (options.AuthMode)
        {
            case Next3AuthModes.ApiKey:
                if (!IsSet(options.ApiKey))
                {
                    failures.Add("Next3:ApiKey is required when Next3:AuthMode is 'apikey' (#1).");
                }

                break;

            case Next3AuthModes.OAuth:
                if (!IsSet(options.OAuth.TokenUrl)
                    || !Uri.TryCreate(options.OAuth.TokenUrl, UriKind.Absolute, out _))
                {
                    failures.Add(
                        "Next3:OAuth:TokenUrl must be an absolute URL when Next3:AuthMode is 'oauth' (#1).");
                }

                if (!IsSet(options.OAuth.ClientId) || !IsSet(options.OAuth.ClientSecret))
                {
                    failures.Add(
                        "Next3:OAuth:ClientId and Next3:OAuth:ClientSecret are required when "
                        + "Next3:AuthMode is 'oauth' (#1).");
                }

                break;

            default:
                failures.Add(
                    $"Next3:AuthMode must be '{Next3AuthModes.ApiKey}' or '{Next3AuthModes.OAuth}' "
                    + $"(currently '{options.AuthMode}'). Mutual TLS is config-ready via "
                    + "Next3:ClientCertificatePath but not implemented — see design.md §6.1 and #1.");
                break;
        }

        // Checked here rather than at the first arrival push, because the alternative to a resolvable
        // zone is not a sensible default. Falling back to UTC would report an expert who arrived at
        // 01:30 GST as arriving on the previous calendar day — the exact defect slice 2.4 removed, on
        // the one field a claims dispute turns on (§9) — and it would do it silently.
        if (!IsSet(options.ArrivalTimeZone) || !TryResolveZone(options.ArrivalTimeZone))
        {
            failures.Add(
                $"Next3:ArrivalTimeZone must be a resolvable IANA time zone id (currently "
                + $"'{options.ArrivalTimeZone}'; e.g. 'Asia/Dubai'). It decides which calendar day an "
                + "arrival is reported on — see design.md §6.1 and #6.");
        }

        if (options.TimeoutSeconds <= 0)
        {
            failures.Add(
                $"Next3:TimeoutSeconds must be greater than zero (currently {options.TimeoutSeconds}). "
                + "A push with no deadline holds its outbox lease open instead of retrying (§6.3).");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsSet(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !value.Contains(PlaceholderMarker, StringComparison.OrdinalIgnoreCase);

    private static bool TryResolveZone(string id)
    {
        try
        {
            // IANA ids resolve on Windows too from .NET 6 on, so the same configuration value works
            // on a developer machine and in the Linux container §10 deploys.
            _ = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }
}
