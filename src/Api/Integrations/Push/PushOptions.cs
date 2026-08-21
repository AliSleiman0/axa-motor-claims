namespace Api.Integrations.Push;

/// <summary>
/// Which push implementation is live and what it signs with (design.md Appendix A `Push:*`, realized
/// slice 3.4). Mirrors <c>Blob:Mode</c> and <c>Next3:Mode</c>: the fake is the default everywhere, so
/// nothing — tests, demos, a fresh clone — needs a VAPID key pair to run.
/// </summary>
public sealed class PushOptions
{
    public const string SectionName = "Push";

    /// <summary>`fake` (console + notification log) or `webpush` (real browsers).</summary>
    public string Mode { get; set; } = PushModes.Fake;

    public PushVapidOptions Vapid { get; } = new();

    /// <summary>Per-request timeout for a call to a push service.</summary>
    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// Hosts the API is willing to POST a push to, matched on the host or any subdomain of it.
    ///
    /// **This is an SSRF control, not tidiness.** The endpoint stored by
    /// <c>POST /api/push/subscriptions</c> is a URL the server later calls, and the caller supplies
    /// it — so without this, any authenticated user could aim the API at a host reachable only from
    /// inside the deployment and read the result out of the `notification` log.
    ///
    /// Not client data, so these are real values rather than placeholders — the same treatment as
    /// `Media:ImageContentTypes`. An empty list disables the check.
    /// </summary>
    public IReadOnlyList<string> AllowedEndpointHosts { get; set; } = [];

    /// <summary>
    /// How many live subscriptions one user may hold. The sender walks them serially on the
    /// assignment-ingestion path, so an unbounded set is an unbounded stall for everybody.
    /// </summary>
    public int MaxSubscriptionsPerUser { get; set; } = 10;
}

/// <summary>
/// The VAPID key pair that identifies this application server to push services (RFC 8292).
///
/// **The values in `appsettings.Placeholders.json` are placeholders and must stay that way.** A VAPID
/// private key is a real credential: anyone holding it can send notifications that browsers will
/// accept as coming from AXA. Real keys live in user-secrets locally and in Container Apps secrets in
/// deployment (§10) — never in the repository. CLAUDE.md carries the commands.
/// </summary>
public sealed class PushVapidOptions
{
    /// <summary>Contact for the push service if it needs to reach the sender — `mailto:` or `https:`.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Base64url P-256 public key. Also handed to the browser, so it is not a secret.</summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>Base64url P-256 private key. **A credential.** Never logged, never served, never committed.</summary>
    public string PrivateKey { get; set; } = string.Empty;
}

/// <summary>Values <c>Push:Mode</c> may take.</summary>
public static class PushModes
{
    public const string Fake = "fake";
    public const string WebPush = "webpush";
}
