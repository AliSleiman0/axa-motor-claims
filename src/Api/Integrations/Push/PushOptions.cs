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

    /// <summary>
    /// Firebase Cloud Messaging, for the Android Capacitor shell (slice 6.3). Nested rather than its
    /// own top-level section because it is not a *third* mode: FCM runs **beside** web push under
    /// `Mode = webpush`, never instead of it (see <c>CompositePushSender</c>).
    /// </summary>
    public PushFcmOptions Fcm { get; } = new();

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

    /// <summary>
    /// How many live FCM registrations one user may hold. Separate from
    /// <see cref="MaxSubscriptionsPerUser"/> rather than shared, because the two count different
    /// things: browsers accumulate (every profile, every machine), handsets do not — an expert
    /// carries one phone, and a user at ten is a reinstall loop rather than ten devices.
    /// The same unbounded-serial-stall argument applies, so it is bounded for the same reason.
    /// </summary>
    public int MaxDeviceTokensPerUser { get; set; } = 10;
}

/// <summary>
/// Firebase Cloud Messaging (design.md §8, slice 6.3) — the only way an Android handset can be
/// notified, because the Capacitor WebView exposes no <c>PushManager</c> at all
/// (research-capacitor.md §3, observed).
///
/// **<see cref="Enabled"/> defaults to false and the shipped values are placeholders**, so every
/// environment keeps booting and `dotnet test` needs no Firebase project. Turning it on requires a
/// real project id and a service-account key file, which is what <c>FcmOptionsValidator</c> checks —
/// and only then, for the reason <c>PushOptionsValidator</c> spells out at length.
/// </summary>
public sealed class PushFcmOptions
{
    /// <summary>Whether <c>CompositePushSender</c> adds the FCM channel beneath web push.</summary>
    public bool Enabled { get; set; }

    /// <summary>The Firebase project the send URL is built from. Not a secret; the key beside it is.</summary>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>
    /// Path to the service-account JSON key.
    ///
    /// **A path, not the key itself.** The file holds an RSA private key that can send notifications
    /// Android accepts as coming from AXA — the same weight as `Push:Vapid:PrivateKey` — so it is
    /// mounted as a secret (§10) and never inlined into configuration, where it would end up in
    /// appsettings, in a deployment log, or in a commit.
    /// </summary>
    public string ServiceAccountJsonPath { get; set; } = string.Empty;

    /// <summary>
    /// Per-request deadline for a call to FCM or to Google's token endpoint. A real deadline: the
    /// sender walks a user's handsets serially on the assignment-ingestion path, so a hung FCM must
    /// fail that one device rather than hold up the notification for everybody.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 15;
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
