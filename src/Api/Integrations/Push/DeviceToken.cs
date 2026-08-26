namespace Api.Integrations.Push;

/// <summary>
/// One Android handset's FCM registration (design.md §4, realized slice 6.3).
///
/// **It is a second table rather than a widened <see cref="PushSubscription"/>, and that is forced
/// rather than chosen.** A web-push subscription is an https endpoint plus the two encryption keys
/// the browser generated (`p256dh`, `auth`); an FCM registration token is one opaque string and has
/// none of the three. Every one of those columns is `IsRequired()`, so a native token fits nowhere
/// in that table without making them nullable — which would leave the web-push sender reading
/// columns that are only sometimes there, on the path §8's primary trigger runs down.
///
/// Same user-scoped shape otherwise: a user has as many rows as they have handsets, and slice 3.4's
/// argument holds unchanged — an expert with a phone and a desk browser should get the popup on
/// both, which is why <c>CompositePushSender</c> sends to this table **and** to
/// <c>push_subscription</c> rather than choosing between them.
///
/// **The token is not a secret of ours.** Possessing it grants nothing without the service-account
/// key that signs the send, exactly as a push endpoint grants nothing without the VAPID private
/// key. So it is stored as FCM gave it, unlike §9's tokens, which are hashed because possession of
/// them *does* grant something.
/// </summary>
public sealed class DeviceToken
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>The registration token FCM issued to this install. Opaque, and vendor-shaped.</summary>
    public required string Token { get; set; }

    /// <summary>
    /// SHA-256 of <see cref="Token"/>, and the reason this column exists is the reason
    /// <c>push_subscription.endpoint_hash</c> exists: the unique index that makes re-registering an
    /// upsert cannot sit on the token itself, because SQL Server caps an index key at 900 bytes and
    /// an nvarchar(512) is 1024. Hashing is for *length*, not secrecy — hence the token is stored
    /// beside it in the clear.
    /// </summary>
    public required string TokenHash { get; set; }

    /// <summary>
    /// Which native platform issued it. Check-constrained to `android` alone, because that is the
    /// only platform this app ships natively (research-capacitor.md §11's platform split — iOS is
    /// the installed PWA and reaches <c>push_subscription</c> through web push).
    ///
    /// The column exists anyway, rather than being implied, because #29 can reverse the split: if
    /// AXA mandates MDM on iOS the native iOS build becomes mandatory, and adding `'ios'` should
    /// then be one migration widening one constraint rather than a table nobody planned for.
    /// </summary>
    public required string Platform { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>When a push was last accepted for this handset. Support's "is this device still live".</summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>
    /// Set when FCM answered 404 carrying <c>UNREGISTERED</c> — the app was uninstalled, the data
    /// cleared, or the token rotated, and this one will never work again.
    ///
    /// Revoked rather than deleted for the same reason <see cref="PushSubscription.RevokedAt"/> is:
    /// it is something that happened *to* the user, so "this expert stopped getting popups on the
    /// 14th, and here is why" stays answerable and the `notification` rows naming this row resolve.
    /// An explicit unregister from the handset deletes instead — the user asked to stop.
    /// </summary>
    public DateTime? RevokedAt { get; set; }
}

/// <summary>
/// Column widths, in one place because two things must agree about them: the EF configuration that
/// creates the columns and the endpoint that refuses oversized input before it reaches them. Split
/// across two files, a widened column and a stale check turn "your handset sent a long token" into a
/// 500 at the database. Same reasoning, same shape, as <see cref="PushSubscriptionLimits"/>.
/// </summary>
public static class DeviceTokenLimits
{
    /// <summary>
    /// Comfortably past the ~163 characters FCM issues today, and short enough to stay a plain
    /// nvarchar. Deliberately generous: Google has lengthened the token format before without
    /// notice, and the failure of a too-narrow column is a handset that silently cannot register.
    /// </summary>
    public const int TokenLength = 512;

    /// <summary>Longest platform value the check constraint allows, with room for `'ios'` later.</summary>
    public const int PlatformLength = 10;
}

/// <summary>
/// Values <c>device_token.platform</c> may take. One today; see <see cref="DeviceToken.Platform"/>
/// for why it is a column rather than an assumption.
/// </summary>
public static class DevicePlatforms
{
    public const string Android = "android";

    /// <summary>What the endpoint validates against, and what the check constraint mirrors.</summary>
    public static readonly string[] All = [Android];
}
