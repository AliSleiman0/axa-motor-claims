namespace Api.Integrations.Next3;

/// <summary>
/// The bound half of Appendix A's `Next3` section.
///
/// Slice 1.4 bound only <see cref="DocTypes"/>, because nothing else was consumed: the mode and
/// assignment-source switches are read once at startup in <c>ServiceRegistration</c>, where the
/// implementation is chosen, and binding them twice would invite the two copies to disagree. Slice
/// 3.3 adds everything <c>RealNext3Client</c> needs — until then `BaseUrl`, `AuthMode` and
/// `ArrivalTimeZone` sat in the placeholder file binding to nothing at all.
///
/// Every value here is a PLACEHOLDER until #1 answers. Validation is deliberately **not** in this
/// class or in the client's constructor — see <see cref="Next3OptionsValidator"/>.
/// </summary>
public sealed class Next3Options
{
    public const string SectionName = "Next3";

    /// <summary>Where NEXT3 lives (#1). Unused while <c>Next3:Mode</c> is `fake`.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// `apikey` or `oauth` (#1). NEXT3 picks; §6.1 lists mutual TLS as a third option it may choose,
    /// which is why <see cref="ClientCertificatePath"/> exists.
    /// </summary>
    public string AuthMode { get; set; } = string.Empty;

    /// <summary>The static key, when <see cref="AuthMode"/> is `apikey`.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Client-credentials settings, when <see cref="AuthMode"/> is `oauth`.</summary>
    public Next3OAuthOptions OAuth { get; } = new();

    /// <summary>
    /// **Config-ready, not exercised.** If #1 answers "mutual TLS", the certificate is loaded from
    /// here and attached to the handler — a change confined to <c>AddPorts</c>. Nothing in this slice
    /// reads it, and saying so is better than a half-built path that looks supported.
    /// </summary>
    public string ClientCertificatePath { get; set; } = string.Empty;

    /// <summary>
    /// The IANA zone §6.1's "date, time" are expressed in (#6). The split from the stored instant
    /// happens in <c>RealNext3Client</c> and nowhere earlier — see <see cref="ArrivalInfo"/> for why
    /// an expert arriving at 01:30 GST is otherwise reported as arriving the previous day.
    /// </summary>
    public string ArrivalTimeZone { get; set; } = string.Empty;

    /// <summary>
    /// Per-request timeout. It is a real deadline rather than an infinite wait because a hung NEXT3
    /// would otherwise hold an outbox row's lease open until it expired, and §6.3 wants that failure
    /// on the retry schedule instead.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// NEXT3's document-type codes (#12), keyed by the names Appendix A gives them —
    /// `InsuredCarPhoto`, `ExpertReport` and so on. Every value is a PLACEHOLDER until #12 answers,
    /// and <see cref="Api.Modules.Media.MediaBuckets"/> maps a bucket to its key.
    /// </summary>
    public Dictionary<string, string> DocTypes { get; } = new(StringComparer.Ordinal);
}

/// <summary>OAuth client-credentials settings (#1), used only when `Next3:AuthMode` is `oauth`.</summary>
public sealed class Next3OAuthOptions
{
    public string TokenUrl { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;
}

/// <summary>Values <c>Next3:AuthMode</c> may take.</summary>
public static class Next3AuthModes
{
    public const string ApiKey = "apikey";
    public const string OAuth = "oauth";
}
