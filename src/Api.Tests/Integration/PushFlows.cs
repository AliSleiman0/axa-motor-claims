using System.Buffers.Text;
using System.Security.Cryptography;
using Api.Integrations.Push;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Integration;

/// <summary>
/// Shared setup for the web-push tests (slice 3.4), in the <c>ExpertFlows</c> / <c>OutboxFlows</c>
/// shape.
/// </summary>
internal static class PushFlows
{
    /// <summary>
    /// A real-shaped `p256dh`: 65 bytes base64url-encoded, which is 87 characters. Real because the
    /// endpoint now decodes and length-checks it, and a fixture that was merely plausible would test
    /// the 400 path instead of the one it means to.
    /// </summary>
    public const string P256dh =
        "BAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    /// <summary>A second valid key, for asserting that re-subscribing takes the new one.</summary>
    public const string OtherP256dh =
        "BAEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQE";

    /// <summary>A real-shaped `auth`: 16 bytes base64url-encoded, which is 22 characters.</summary>
    public const string Auth = "AAAAAAAAAAAAAAAAAAAAAA";

    private static int _counter;

    /// <summary>
    /// A unique endpoint on a host the allow-list accepts. Unique because the suite shares one
    /// database for the run and the unique index is on (user_id, endpoint_hash).
    /// </summary>
    public static string NextEndpoint() =>
        $"https://fcm.googleapis.com/fcm/send/PLACEHOLDER-T{Interlocked.Increment(ref _counter):D5}";

    public static object Subscription(string? endpoint = null, string? p256dh = null) => new
    {
        endpoint = endpoint ?? NextEndpoint(),
        p256dh = p256dh ?? P256dh,
        auth = Auth,
    };

    public static async Task<List<PushSubscription>> SubscriptionsOf(this ApiFixture fixture, Guid userId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Set<PushSubscription>()
            .Where(s => s.UserId == userId)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync();
    }

    /// <summary>Marks a subscription dead the way the push service would, so the upsert can un-do it.</summary>
    public static async Task Revoke(this ApiFixture fixture, Guid subscriptionId)
    {
        await using var db = fixture.CreateDbContext();
        await db.Set<PushSubscription>()
            .Where(s => s.Id == subscriptionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)));
    }

    /// <summary>
    /// Registers a browser directly, for tests about sending rather than about subscribing.
    ///
    /// Uses a **real** P-256 point by default, unlike <see cref="P256dh"/>: the sender encrypts the
    /// payload to this key, so a value that is merely 65 well-formed bytes fails inside the crypto
    /// with a platform exception rather than exercising the path under test.
    /// </summary>
    public static async Task<PushSubscription> AddSubscription(
        this ApiFixture fixture, Guid userId, string? endpoint = null)
    {
        // Resolved once: calling NextEndpoint() twice would hash a different endpoint than it stores,
        // and the row would then be unreachable by the only key the API looks it up with.
        var resolved = endpoint ?? NextEndpoint();

        var subscription = new PushSubscription
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Endpoint = resolved,
            EndpointHash = Api.Infrastructure.TokenHashing.Hash(resolved),
            P256dh = BrowserTestKeys.P256dh,
            Auth = Auth,
            CreatedAt = fixture.Time.GetUtcNow().UtcDateTime,
        };

        await using var db = fixture.CreateDbContext();
        db.Set<PushSubscription>().Add(subscription);
        await db.SaveChangesAsync();
        return subscription;
    }

    /// <summary>
    /// A unique FCM registration token. Unique for <see cref="NextEndpoint"/>'s reason: the suite
    /// shares one database for the run and the unique index is on (user_id, token_hash).
    /// </summary>
    public static string NextDeviceToken() =>
        $"PLACEHOLDER-fcm-token-{Interlocked.Increment(ref _counter):D5}"
        + ":APA91bPLACEHOLDERPLACEHOLDERPLACEHOLDERPLACEHOLDER";

    public static object DeviceTokenBody(string? token = null, string? platform = null) => new
    {
        token = token ?? NextDeviceToken(),
        platform = platform ?? DevicePlatforms.Android,
    };

    public static async Task<List<DeviceToken>> DeviceTokensOf(this ApiFixture fixture, Guid userId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Set<DeviceToken>()
            .Where(t => t.UserId == userId)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync();
    }

    /// <summary>Marks a handset dead the way FCM's UNREGISTERED would, so the upsert can un-do it.</summary>
    public static async Task RevokeDevice(this ApiFixture fixture, Guid deviceTokenId)
    {
        await using var db = fixture.CreateDbContext();
        await db.Set<DeviceToken>()
            .Where(t => t.Id == deviceTokenId)
            .ExecuteUpdateAsync(s => s.SetProperty(
                x => x.RevokedAt, new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)));
    }

    /// <summary>
    /// Registers a handset directly, for tests about sending rather than about registering.
    ///
    /// No key material here, unlike <see cref="AddSubscription"/>: an FCM token is opaque and the
    /// sender does no crypto with it, so a placeholder-shaped string exercises the real path.
    /// </summary>
    public static async Task<DeviceToken> AddDeviceToken(
        this ApiFixture fixture, Guid userId, string? token = null)
    {
        // Resolved once, for AddSubscription's reason: hashing a second call's value would store a
        // row the lookup key can never find.
        var resolved = token ?? NextDeviceToken();

        var device = new DeviceToken
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Token = resolved,
            TokenHash = Api.Infrastructure.TokenHashing.Hash(resolved),
            Platform = DevicePlatforms.Android,
            CreatedAt = fixture.Time.GetUtcNow().UtcDateTime,
        };

        await using var db = fixture.CreateDbContext();
        db.Set<DeviceToken>().Add(device);
        await db.SaveChangesAsync();
        return device;
    }

    /// <summary>The `notification` rows this user's pushes produced, oldest first.</summary>
    public static async Task<List<Api.Modules.Notifications.Notification>> PushRows(
        this ApiFixture fixture, Guid userId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Set<Api.Modules.Notifications.Notification>()
            .Where(n => n.RecipientUserId == userId
                && n.Channel == Api.Modules.Notifications.NotificationChannels.Push)
            .OrderBy(n => n.CreatedAt)
            .ToListAsync();
    }
}

/// <summary>
/// A real P-256 VAPID pair, generated fresh for each test run.
///
/// Real because the library signs its request with it and a placeholder throws before anything
/// reaches the transport — so a fixture that was merely well-shaped would test nothing. Generated
/// rather than committed for the same reason the application refuses to commit one: a VAPID private
/// key is a credential, and a test fixture is exactly where one quietly becomes permanent.
/// </summary>
internal static class VapidTestKeys
{
    static VapidTestKeys()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = ecdsa.ExportParameters(includePrivateParameters: true);

        // The uncompressed point, which is what a browser and a push service both expect: 0x04
        // followed by X and Y, 65 bytes, base64url.
        byte[] point = [0x04, .. parameters.Q.X!, .. parameters.Q.Y!];

        PublicKey = Base64Url.EncodeToString(point);
        PrivateKey = Base64Url.EncodeToString(parameters.D!);
    }

    public static string PublicKey { get; }

    public static string PrivateKey { get; }
}

/// <summary>
/// A service-account key file for the FCM tests, written fresh into the test output directory each
/// run (slice 6.3).
///
/// **Real RSA, and generated rather than committed, for <see cref="VapidTestKeys"/>'s two reasons.**
/// Real because <c>FcmAccessTokens</c> imports the PEM and signs an RS256 assertion with it, so a
/// placeholder string would throw inside `ImportFromPem` before anything reached the transport — the
/// test would then be pinning the wrong failure. Generated because a service-account key is a
/// credential, and a fixture is exactly where one quietly becomes permanent.
///
/// In the output directory rather than the system temp folder so it is gitignored (`bin/`) and goes
/// away with a clean, instead of accumulating one file per run somewhere nobody looks.
/// </summary>
internal static class FcmTestServiceAccount
{
    /// <summary>Google's real token endpoint. Not client data — the same treatment as the FCM host.</summary>
    public const string TokenUri = "https://oauth2.googleapis.com/token";

    static FcmTestServiceAccount()
    {
        using var rsa = RSA.Create(2048);

        // The shape of the file the Firebase console hands out, with only the three fields the
        // adapter reads. `private_key` carries real newlines once JSON-decoded, which is what
        // `ImportFromPem` requires — writing it any other way would pass here and fail on the real
        // file.
        var json = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["type"] = "service_account",
            ["project_id"] = "PLACEHOLDER-firebase-project-id",
            ["client_email"] = "PLACEHOLDER-fcm@PLACEHOLDER-project.iam.gserviceaccount.invalid",
            ["private_key"] = rsa.ExportPkcs8PrivateKeyPem(),
            ["token_uri"] = TokenUri,
        });

        Path = System.IO.Path.Combine(AppContext.BaseDirectory, "fcm-service-account.test.json");
        File.WriteAllText(Path, json);
    }

    /// <summary>Where the generated key file sits, for `Push:Fcm:ServiceAccountJsonPath`.</summary>
    public static string Path { get; }
}

/// <summary>
/// A real P-256 public key standing in for a browser's, generated per run.
///
/// It has to be a genuine point on the curve, not just 65 well-shaped bytes: `WebPushSender` encrypts
/// the payload *to* this key, and an invalid point throws out of the platform's ECDH import — which
/// is a different failure from the ones these tests exist to pin, and looked exactly like a broken
/// sender until it was tracked down.
///
/// Recorded consequence: the subscribe endpoint checks only that a key is base64url of the right
/// length, not that it is on the curve. A junk-but-well-formed key is therefore storable, and shows
/// up as a `failed` notification row for that one device — contained, because the sender's loop
/// catches everything per subscription, which is why that catch is deliberately not a named list.
/// </summary>
internal static class BrowserTestKeys
{
    static BrowserTestKeys()
    {
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var parameters = ecdh.ExportParameters(includePrivateParameters: false);

        byte[] point = [0x04, .. parameters.Q.X!, .. parameters.Q.Y!];
        P256dh = Base64Url.EncodeToString(point);
    }

    public static string P256dh { get; }
}
