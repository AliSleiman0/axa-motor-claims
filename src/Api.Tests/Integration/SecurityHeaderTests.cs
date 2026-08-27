using System.Net;
using Api.Modules.PublicSurface;

namespace Api.Tests.Integration;

/// <summary>
/// design.md §9's response headers (slice 7.1). Until this slice the API sent none at all — not on
/// the authenticated surface, not on the document bytes it streams to a browser, and not on §9.1's
/// public page, which is the one screen in the product a member of the public reaches from a link.
/// </summary>
[Collection("api")]
public sealed class SecurityHeaderTests(ApiFixture fixture) : IDisposable
{
    private static readonly (string Name, string Value)[] Expected =
    [
        ("X-Content-Type-Options", "nosniff"),
        ("X-Frame-Options", "DENY"),
        ("Content-Security-Policy", "default-src 'none'; frame-ancestors 'none'"),
        ("Referrer-Policy", "no-referrer"),
        ("Strict-Transport-Security", "max-age=31536000"),
    ];

    private readonly PublicLinkOptions _original = Clone(fixture.PublicLink.CurrentValue);

    public void Dispose() => fixture.PublicLink.CurrentValue = _original;

    /// <summary>
    /// Three responses that never meet each other in the pipeline: a bare endpoint mapped before
    /// everything, an authorization failure written by the auth middleware, and §9.1's uniform 404
    /// from a handler. A header set in a handler, or by an endpoint filter, would cover at most one.
    /// </summary>
    [Theory]
    [InlineData("/health", HttpStatusCode.OK)]
    [InlineData("/api/expert/assignments", HttpStatusCode.Unauthorized)]
    [InlineData("/public/PLACEHOLDER-no-such-token", HttpStatusCode.NotFound)]
    public async Task EveryResponseCarriesTheSecurityHeaders(string path, HttpStatusCode expected)
    {
        using var client = fixture.CreatePublicClient();

        var response = await client.GetAsync(path);

        Assert.Equal(expected, response.StatusCode);
        AssertCarriesThem(response);
    }

    /// <summary>
    /// The reason the middleware is registered above <c>UseRateLimiter</c>. A 429 is written by the
    /// limiter itself and short-circuits every later middleware, so it is precisely the response that
    /// a plausible-looking registration order would leave bare — and it is the response a caller
    /// hammering the public surface sees most of.
    /// </summary>
    [Fact]
    public async Task A429CarriesThemToo()
    {
        SetLimits(perIp: 1000, perToken: 1);
        var link = await fixture.IssueLink();
        using var customer = fixture.CreatePublicClient();

        Assert.Equal(HttpStatusCode.OK, (await customer.GetAsync($"/public/{link.Token}")).StatusCode);

        var throttled = await customer.GetAsync($"/public/{link.Token}");

        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        AssertCarriesThem(throttled);
    }

    /// <summary>
    /// **CORS is deliberately absent, and this is where that is written down as a decision rather than
    /// an omission.** The app is same-origin — §10/7.3 serve the SPA from this host — so no browser
    /// ever needs a cross-origin grant, and adding one would mean deciding that some other origin may
    /// call an API that includes §9.1's public surface. A future `UseCors` with a permissive default
    /// turns this red, which is the point.
    /// </summary>
    [Fact]
    public async Task ACrossOriginRequestGetsNoAccessControlHeader()
    {
        using var client = fixture.CreatePublicClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", "https://PLACEHOLDER-elsewhere.example");
        var response = await client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));

        // And the preflight, which is the half a browser actually asks first.
        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/health");
        preflight.Headers.Add("Origin", "https://PLACEHOLDER-elsewhere.example");
        preflight.Headers.Add("Access-Control-Request-Method", "GET");
        var answered = await client.SendAsync(preflight);

        Assert.False(answered.Headers.Contains("Access-Control-Allow-Origin"));
    }

    /// <summary>
    /// Present, correct, and **exactly once**. The single-value check is the one that matters beyond
    /// the obvious: <c>DocumentContent</c> writes its own <c>nosniff</c> line, so the middleware has
    /// to assign rather than append or every document response would carry the header twice — which
    /// is not merely untidy, since some parsers treat a duplicated security header as malformed and
    /// drop it.
    /// </summary>
    private static void AssertCarriesThem(HttpResponseMessage response)
    {
        foreach (var (name, value) in Expected)
        {
            Assert.True(response.Headers.Contains(name), $"{name} missing from {response.StatusCode}");
            Assert.Equal(value, Assert.Single(response.Headers.GetValues(name)));
        }
    }

    private void SetLimits(int perIp, int perToken)
    {
        var options = Clone(_original);
        options.RateLimit.PerIpPermitsPerMinute = perIp;
        options.RateLimit.PerTokenPermitsPerMinute = perToken;
        fixture.PublicLink.CurrentValue = options;
    }

    private static PublicLinkOptions Clone(PublicLinkOptions source) => new()
    {
        ValidityDays = source.ValidityDays,
        DeliveryChannel = source.DeliveryChannel,
        MaxFiles = source.MaxFiles,
        MaxFileMb = source.MaxFileMb,
        RateLimit = new PublicRateLimitOptions
        {
            PerIpPermitsPerMinute = source.RateLimit.PerIpPermitsPerMinute,
            PerTokenPermitsPerMinute = source.RateLimit.PerTokenPermitsPerMinute,
        },
    };
}
