namespace Api.Tests.Integrations;

/// <summary>
/// A fact that runs only when a NEXT3 sandbox has been configured, and reports itself skipped when it
/// has not.
///
/// The same compromise <c>[AzuriteFact]</c> makes for blob storage, and for the same reason: open
/// question #1 is unanswered, so there is no sandbox URL, no credentials and no test data, and
/// <c>dotnet test</c> must stay green without them. But a real client with no test at all is how a
/// thin HTTP wrapper quietly stops working — so the contract runs against the sandbox the moment one
/// exists, without the suite depending on it until then.
///
/// The probe is a configuration lookup rather than <c>[AzuriteFact]</c>'s TCP connect, because
/// "reachable" is not the question here: a port that answers proves neither credentials nor data.
/// **A visa number is required too.** Half the contract is about a claim that exists — a replayed
/// <c>clientRef</c>, an arrival, a document — and there is no way to guess one that lives in AXA's
/// sandbox. Skipping for want of a visa is honest; passing because every assertion happened to be
/// about absent data would not be.
/// </summary>
public sealed class SandboxFactAttribute : FactAttribute
{
    /// <summary>The sandbox base URL (#1). Absent everywhere until AXA supplies one.</summary>
    public const string UrlVariable = "NEXT3_SANDBOX_URL";

    /// <summary>A visa number that exists in that sandbox, for the half of the contract that needs one.</summary>
    public const string VisaVariable = "NEXT3_SANDBOX_VISA";

    /// <summary>The API key, when the sandbox uses one. Optional — an empty key is still a run.</summary>
    public const string ApiKeyVariable = "NEXT3_SANDBOX_API_KEY";

    private static readonly Lazy<string?> Url = new(
        () => Environment.GetEnvironmentVariable(UrlVariable), isThreadSafe: true);

    private static readonly Lazy<string?> Visa = new(
        () => Environment.GetEnvironmentVariable(VisaVariable), isThreadSafe: true);

    public SandboxFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Url.Value) || string.IsNullOrWhiteSpace(Visa.Value))
        {
            Skip =
                $"No NEXT3 sandbox: set {UrlVariable} and {VisaVariable} to run the contract against "
                + "the real client (#1).";
        }
    }

    /// <summary>The configured sandbox URL. Only read from a test this attribute did not skip.</summary>
    public static string SandboxUrl =>
        Url.Value ?? throw new InvalidOperationException($"{UrlVariable} is not set.");

    /// <summary>A visa the sandbox knows. Only read from a test this attribute did not skip.</summary>
    public static string SandboxVisa =>
        Visa.Value ?? throw new InvalidOperationException($"{VisaVariable} is not set.");

    /// <summary>The sandbox API key, or empty when it needs none.</summary>
    public static string SandboxApiKey =>
        Environment.GetEnvironmentVariable(ApiKeyVariable) ?? string.Empty;
}
