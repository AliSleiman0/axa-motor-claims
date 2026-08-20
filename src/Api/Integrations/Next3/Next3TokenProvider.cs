using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Api.Integrations.Next3;

/// <summary>
/// The OAuth client-credentials half of §6.1's "Authentication" operation, cached.
///
/// A token request per push would double the call count against a legacy core system for no benefit,
/// so the token is held until it is nearly expired. **Nearly**, not exactly: a token fetched with one
/// second left would be sent on a request that arrives after it has died, so <see cref="Skew"/> is
/// subtracted from the expiry. The clock is <see cref="TimeProvider"/>, so the caching is testable
/// on a frozen clock like everything else in this codebase.
///
/// Singleton, because <c>OutboxProcessor</c> is one (and it is the only caller): the cache must
/// outlive a request and be shared across worker passes, the same reasoning that makes the fake
/// NEXT3 a singleton.
/// </summary>
public sealed class Next3TokenProvider(
    IHttpClientFactory httpClients,
    IOptions<Next3Options> options,
    TimeProvider time) : IDisposable
{
    /// <summary>
    /// How early a token is treated as expired. Generous on purpose — a NEXT3 push can queue behind a
    /// 15 MB upload, and re-fetching a token is cheap next to a `failed` row on A2.
    /// </summary>
    private static readonly TimeSpan Skew = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _token;
    private DateTimeOffset _expiresAt;

    /// <summary>The bearer token, fetched if there is no live one cached.</summary>
    public async Task<string> GetToken(CancellationToken ct)
    {
        if (TryReuse(out var cached))
        {
            return cached;
        }

        await _gate.WaitAsync(ct);
        try
        {
            // Re-checked inside the gate: several pushes in one batch can miss the cache together,
            // and without this they would each fetch a token and the last one would win.
            if (TryReuse(out var afterWait))
            {
                return afterWait;
            }

            return await Fetch(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Drops the cached token so the next call fetches a fresh one.
    ///
    /// Called by <c>RealNext3Client</c> on a 401. NEXT3 may expire a token early, or revoke one, and
    /// the difference between that and a genuinely bad credential cannot be told apart from the
    /// status alone — so the client retries exactly once after this, and a second 401 is a rejection.
    /// </summary>
    public void Invalidate()
    {
        _token = null;
        _expiresAt = default;
    }

    /// <summary>Releases the fetch gate. The container owns the lifetime — this is a singleton.</summary>
    public void Dispose() => _gate.Dispose();

    private bool TryReuse(out string token)
    {
        var cached = _token;
        token = cached ?? string.Empty;
        return cached is not null && time.GetUtcNow() < _expiresAt;
    }

    private async Task<string> Fetch(CancellationToken ct)
    {
        var settings = options.Value;
        var client = httpClients.CreateClient(Next3HttpClient.Name);

        using var request = new HttpRequestMessage(HttpMethod.Post, settings.OAuth.TokenUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = settings.OAuth.ClientId,
                ["client_secret"] = settings.OAuth.ClientSecret,
            }),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(request, ct);

        // Same split as every other call (§6.3): a 5xx on the token endpoint is NEXT3 having a bad
        // moment and is worth the retry schedule; a 400 `invalid_client` is a credential someone has
        // to fix, and belongs on A2 now rather than in 26 hours.
        if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
        {
            var snippet = await Next3Http.ReadSnippet(response, ct);
            throw new Next3RejectedException((int)response.StatusCode, "the token request", snippet);
        }

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(ct)
            ?? throw new Next3RejectedException("NEXT3's token endpoint returned an empty body.");

        if (string.IsNullOrWhiteSpace(payload.AccessToken))
        {
            throw new Next3RejectedException("NEXT3's token endpoint returned no access_token.");
        }

        // A missing or absurd expires_in is treated as one minute rather than as forever: a token
        // cached indefinitely turns into a 401 on every push the moment NEXT3 rotates it.
        var lifetime = payload.ExpiresIn > 0
            ? TimeSpan.FromSeconds(payload.ExpiresIn)
            : TimeSpan.FromMinutes(1);

        _token = payload.AccessToken;
        _expiresAt = time.GetUtcNow() + (lifetime > Skew ? lifetime - Skew : lifetime);

        return payload.AccessToken;
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
