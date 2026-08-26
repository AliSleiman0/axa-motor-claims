using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Api.Integrations.Push;

/// <summary>
/// OAuth access tokens for FCM's v1 API, minted from the service-account key (slice 6.3).
///
/// FCM v1 does not take an API key: the caller signs a short-lived RS256 assertion with the service
/// account's private key and exchanges it at Google's token endpoint for a bearer token. That is the
/// whole of this class.
///
/// **No new package, and deliberately so.** `Microsoft.IdentityModel.JsonWebTokens` already rides in
/// transitively via `Microsoft.AspNetCore.Authentication.JwtBearer`, and
/// <c>Modules/Users/TokenService.cs</c> has been signing this application's own access tokens with
/// the same <see cref="JsonWebTokenHandler"/> since slice 1.2 — so this is the house idiom with an
/// RSA key instead of a symmetric one, rather than a dependency taken on for one adapter (and
/// therefore no licence question, which 3.4's rule would otherwise raise).
///
/// Singleton, like the senders that use it: the exchange costs a network round trip and the token
/// lasts an hour, so minting one per push would add a second remote call to the
/// assignment-ingestion path for no benefit.
/// </summary>
public sealed partial class FcmAccessTokens(
    IHttpClientFactory httpClients,
    IOptions<PushOptions> options,
    TimeProvider time,
    ILogger<FcmAccessTokens> logger) : IDisposable
{
    /// <summary>The one scope FCM sends need. Google's own constant, not client data.</summary>
    private const string Scope = "https://www.googleapis.com/auth/firebase.messaging";

    /// <summary>
    /// How long before real expiry a cached token is treated as spent.
    ///
    /// Five minutes rather than seconds because the clock that matters is *Google's*, not ours: a
    /// container whose time has drifted a couple of minutes would otherwise present a token the
    /// server considers expired, and the symptom is a 401 on the send path rather than anything
    /// pointing at a clock.
    /// </summary>
    private static readonly TimeSpan ExpiryMargin = TimeSpan.FromMinutes(5);

    /// <summary>The assertion's own lifetime. Google caps it at one hour and rejects anything longer.</summary>
    private static readonly TimeSpan AssertionLifetime = TimeSpan.FromMinutes(55);

    private static readonly JsonWebTokenHandler TokenHandler = new();

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// The cached token and its deadline as **one** field.
    ///
    /// Two fields would be two separate stores, and the fast path below reads them outside the lock:
    /// a reader could observe the new deadline beside the old token and present an expired bearer,
    /// which FCM answers 401 — a `failed` row per handset until the deadline lapsed, with nothing
    /// pointing at the cause. A single reference assignment is atomic, so a reader sees either the
    /// whole old value or the whole new one. Raised by the db-review.
    /// </summary>
    private volatile MintedToken? _minted;

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Minted an FCM access token; it is good until {ExpiresAt:o}.")]
    private static partial void LogMinted(ILogger logger, DateTimeOffset expiresAt);

    /// <summary>
    /// A bearer token for FCM, from cache when one is still good.
    ///
    /// The lock is held across the exchange rather than only around the cache read, so a burst of
    /// pushes after a restart mints **one** token instead of one per concurrent send. Google rate
    /// limits the token endpoint, and a stampede there would fail the sends it exists to serve.
    /// </summary>
    public async Task<string> Get(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        if (_minted is { } cached && now < cached.GoodUntil)
        {
            return cached.Value;
        }

        await _gate.WaitAsync(ct);
        try
        {
            // Re-checked inside the lock: whoever was waiting behind the mint that just finished
            // wants its result, not a second round trip.
            now = time.GetUtcNow();
            if (_minted is { } fresh && now < fresh.GoodUntil)
            {
                return fresh.Value;
            }

            var credentials = await ReadServiceAccount(ct);
            var granted = await Exchange(credentials, now, ct);

            // Through a local, so the compiler can see what `Exchange` already guaranteed: it throws
            // rather than returning a token with a null value.
            var minted = new MintedToken(
                granted.AccessToken!, now.AddSeconds(granted.ExpiresIn) - ExpiryMargin);
            _minted = minted;
            LogMinted(logger, minted.GoodUntil);
            return minted.Value;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Releases the mint lock. Present because the container owns this singleton for the life of the
    /// process and the analyzer is right that something has to; there is no unmanaged state and no
    /// finalizer, so it is the plain single-dispose shape.
    /// </summary>
    public void Dispose() => _gate.Dispose();

    private async Task<ServiceAccount> ReadServiceAccount(CancellationToken ct)
    {
        var path = options.Value.Fcm.ServiceAccountJsonPath;

        // Read every time rather than cached alongside the token: a rotated mounted secret should be
        // picked up at the next mint, which is at most an hour, instead of requiring a restart.
        // Reading a small file once an hour costs nothing worth optimising.
        await using var stream = File.OpenRead(path);
        var account = await JsonSerializer.DeserializeAsync<ServiceAccount>(stream, JsonOptions, ct);

        if (account is null
            || string.IsNullOrWhiteSpace(account.ClientEmail)
            || string.IsNullOrWhiteSpace(account.PrivateKey)
            || string.IsNullOrWhiteSpace(account.TokenUri))
        {
            // Named rather than a NullReferenceException three frames down. The likeliest cause is
            // the wrong file: a `google-services.json` (the *client* config, which carries none of
            // these fields) mounted where the service-account key belongs.
            throw new InvalidOperationException(
                $"The FCM service-account file at '{path}' is missing client_email, private_key or "
                + "token_uri. A google-services.json is not a service-account key — the key comes "
                + "from the Firebase console under Project settings -> Service accounts.");
        }

        return account;
    }

    private async Task<GrantedToken> Exchange(
        ServiceAccount account, DateTimeOffset now, CancellationToken ct)
    {
        var assertion = Sign(account, now);

        using var request = new HttpRequestMessage(HttpMethod.Post, account.TokenUri)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"] = assertion,
            }),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var client = httpClients.CreateClient(FcmHttpClient.Name);
        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            // The body is Google's own error JSON and carries the reason (`invalid_grant` for a
            // clock skew, a revoked key, or a deleted service account). Quoted because none of it is
            // a credential and the alternative is a status code with no cause.
            throw new HttpRequestException(
                $"Google refused the FCM token exchange with {(int)response.StatusCode} "
                + $"{response.StatusCode}: {body}");
        }

        var granted = JsonSerializer.Deserialize<GrantedToken>(body, JsonOptions);
        if (granted is null || string.IsNullOrWhiteSpace(granted.AccessToken) || granted.ExpiresIn <= 0)
        {
            throw new HttpRequestException(
                "Google's FCM token response carried no usable access_token/expires_in pair.");
        }

        return granted;
    }

    private static string Sign(ServiceAccount account, DateTimeOffset now)
    {
        // Disposed as soon as the token is written, so an RSA handle does not leak once an hour for
        // the life of the process.
        using var rsa = RSA.Create();
        rsa.ImportFromPem(account.PrivateKey);

        // **Caching has to be off, and this was a real bug the tests caught.**
        // `Microsoft.IdentityModel` keeps a process-wide `CryptoProviderFactory` cache of signature
        // providers. With it on, the provider built for the first assertion outlives this method and
        // holds the `RSA` that the `using` above has just disposed — so the *next* mint signs with a
        // dead handle and throws `ObjectDisposedException`. In production the first token lasts about
        // an hour, so the symptom would have been Android push working fine after a deploy and then
        // silently stopping, for every handset at once, roughly an hour later: exactly the
        // single-platform silent outage this slice exists to make impossible. A private factory is
        // scoped to this key and this call.
        var key = new RsaSecurityKey(rsa)
        {
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false },
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = account.ClientEmail,
            Audience = account.TokenUri,
            IssuedAt = now.UtcDateTime,
            Expires = now.Add(AssertionLifetime).UtcDateTime,
            Claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["scope"] = Scope,
            },
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
        };

        return TokenHandler.CreateToken(descriptor);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>A token and the moment it stops being usable, written and read as one value.</summary>
    private sealed record MintedToken(string Value, DateTimeOffset GoodUntil);

    /// <summary>The three fields of a service-account key this adapter uses. The file carries more.</summary>
    private sealed record ServiceAccount(
        [property: JsonPropertyName("client_email")] string? ClientEmail,
        [property: JsonPropertyName("private_key")] string? PrivateKey,
        [property: JsonPropertyName("token_uri")] string? TokenUri);

    private sealed record GrantedToken(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}

/// <summary>The named <c>HttpClient</c> FCM sends through — the third, after `next3` and `webpush`.</summary>
public static class FcmHttpClient
{
    public const string Name = "fcm";
}
