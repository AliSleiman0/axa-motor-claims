using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Api.Modules.PublicSurface;

namespace Api.Tests.Integration;

/// <summary>
/// design.md §9.1's "per-IP and per-token rate limits on every /public/* endpoint", plus the
/// per-submission size cap.
/// </summary>
/// <remarks>
/// Each test lowers the limits and then uses a fresh token and a fresh source IP: the limiter
/// caches a limiter instance per partition key, so only partitions created after the change see
/// the new numbers.
/// </remarks>
[Collection("api")]
public sealed class PublicRateLimitTests(ApiFixture fixture) : IDisposable
{
    private readonly PublicLinkOptions _original = Clone(fixture.PublicLink.CurrentValue);

    public void Dispose() => fixture.PublicLink.CurrentValue = _original;

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

    private void SetLimits(int perIp, int perToken)
    {
        var options = Clone(_original);
        options.RateLimit.PerIpPermitsPerMinute = perIp;
        options.RateLimit.PerTokenPermitsPerMinute = perToken;
        fixture.PublicLink.CurrentValue = options;
    }

    /// <summary>
    /// Slice 5.3's upload route is covered by §9.1's limiter **for free**, and "for free" is exactly
    /// the kind of claim that is worth proving rather than asserting in a comment.
    ///
    /// The mechanism is that <c>PartitionByToken</c> reads the token out of the raw path rather than
    /// from route values, so <c>/public/{token}/documents</c> lands in the same partition as
    /// <c>/public/{token}</c> — one budget per link across every route it has. This test spends that
    /// budget on the *view* and then finds the *upload* throttled, which no per-endpoint policy would
    /// produce: a customer cannot escape the cap by switching to the route that actually stores bytes.
    /// </summary>
    [Fact]
    public async Task TheUploadRouteSharesOneTokenBudgetWithTheRestOfTheLink()
    {
        const int PerToken = 2;
        SetLimits(perIp: 1000, perToken: PerToken);
        var link = await fixture.IssueLink();
        using var customer = fixture.CreatePublicClient();

        for (var i = 0; i < PerToken; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await customer.GetAsync($"/public/{link.Token}")).StatusCode);
        }

        var throttled = await PublicLinkFlows.UploadPublicDocument(customer, link.Token);
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
    }

    /// <summary>
    /// And the other half of "partitions": one customer exhausting their own link must not throttle
    /// another customer's. The partition key is the token's *hash*, so this also holds for a token
    /// that was never valid — which is what stops the limiter being used to probe which links exist.
    /// </summary>
    [Fact]
    public async Task TheUploadRouteIsPartitionedPerToken()
    {
        const int PerToken = 1;
        SetLimits(perIp: 1000, perToken: PerToken);

        var exhausted = await fixture.IssueLink();
        var fresh = await fixture.IssueLink();
        using var customer = fixture.CreatePublicClient();

        Assert.Equal(
            HttpStatusCode.OK, (await customer.GetAsync($"/public/{exhausted.Token}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            (await PublicLinkFlows.UploadPublicDocument(customer, exhausted.Token)).StatusCode);

        // Same client, same IP, a different link: its own budget, untouched.
        var other = await PublicLinkFlows.UploadPublicDocument(customer, fresh.Token);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, other.StatusCode);
    }

    [Fact]
    public async Task PerToken_ThrottlesAtTheConfiguredPermitCount_WithRetryAfter()
    {
        const int PerToken = 3;
        SetLimits(perIp: 1000, perToken: PerToken);
        var link = await fixture.IssueLink();
        using var customer = fixture.CreatePublicClient();

        for (var i = 0; i < PerToken; i++)
        {
            var allowed = await customer.GetAsync($"/public/{link.Token}");
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        var throttled = await customer.GetAsync($"/public/{link.Token}");
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        Assert.True(throttled.Headers.Contains("Retry-After"));
    }

    /// <summary>
    /// The header above is asserted to be *present*; this asserts it is **usable**. A customer holding
    /// a live link is told to wait a specific number of seconds, and S1/S2 already read the auth
    /// surface's version of this header rather than counting down from config (slice 4.4) — a
    /// `Retry-After` of 0, or of 86400, would send them away from a link that is about to work.
    /// </summary>
    /// <remarks>
    /// Bounded rather than exact, and that is not slack. The value is
    /// <c>(int)lease.RetryAfter.TotalSeconds</c> off the **real system clock** — the limiter does not
    /// use the injected <c>TimeProvider</c> — so a window opened microseconds ago legitimately reports
    /// 59 or 60 depending on where the truncation falls. An equality assertion here would be a flake
    /// with a countdown attached to it. What is worth pinning is that it lies inside the window it
    /// describes: greater than zero (a wait of "none" for a request that was just refused is a client
    /// retry loop) and no greater than the one-minute window (anything larger is not this limiter).
    /// </remarks>
    [Fact]
    public async Task TheRetryAfterValue_IsSecondsWithinTheWindow()
    {
        SetLimits(perIp: 1000, perToken: 1);
        var link = await fixture.IssueLink();
        using var customer = fixture.CreatePublicClient();

        Assert.Equal(HttpStatusCode.OK, (await customer.GetAsync($"/public/{link.Token}")).StatusCode);

        var throttled = await customer.GetAsync($"/public/{link.Token}");
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);

        var raw = Assert.Single(throttled.Headers.GetValues("Retry-After"));
        Assert.True(
            int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds),
            $"Retry-After must be a delay in whole seconds, not '{raw}'.");
        Assert.InRange(seconds, 1, 60);
    }

    /// <summary>
    /// §9.1's uniform surface, at the one status code <c>PublicLinkNotFoundUniformityTests</c> cannot
    /// reach. <c>InvalidTokens_ThrottleToo_SoTheLimiterIsNotAnOracle</c> proves a bogus token is
    /// throttled at all; this proves the two refusals are **the same refusal**. A 429 that carried a
    /// body, a different content type or a `Retry-After` only for real links would let a scanner sort
    /// live links from dead ones by exhausting a budget it is allowed to exhaust — the limiter turned
    /// into the oracle the 404 is so careful not to be.
    /// </summary>
    /// <remarks>
    /// The header's *value* is deliberately outside the comparison: two responses a few milliseconds
    /// apart legitimately differ by one second of remaining window, and that difference is a clock
    /// rather than a fact about the token.
    /// </remarks>
    [Fact]
    public async Task A429ForAValidToken_IsIndistinguishableFromABogusOne()
    {
        SetLimits(perIp: 1000, perToken: 1);

        var link = await fixture.IssueLink();
        var bogus = Convert.ToHexString(Guid.NewGuid().ToByteArray());

        // Separate clients so the two tokens spend their own budgets from their own addresses; the
        // per-IP leg is out of reach either way.
        using var real = fixture.CreatePublicClient();
        using var unknown = fixture.CreatePublicClient();

        Assert.Equal(HttpStatusCode.OK, (await real.GetAsync($"/public/{link.Token}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await unknown.GetAsync($"/public/{bogus}")).StatusCode);

        var throttledReal = await real.GetAsync($"/public/{link.Token}");
        var throttledBogus = await unknown.GetAsync($"/public/{bogus}");

        Assert.Equal(HttpStatusCode.TooManyRequests, throttledReal.StatusCode);
        Assert.Equal(throttledReal.StatusCode, throttledBogus.StatusCode);
        Assert.Equal(
            await throttledReal.Content.ReadAsStringAsync(),
            await throttledBogus.Content.ReadAsStringAsync());
        Assert.Equal(ContentShape(throttledReal), ContentShape(throttledBogus));
        Assert.Equal(
            throttledReal.Headers.Contains("Retry-After"),
            throttledBogus.Headers.Contains("Retry-After"));
    }

    private static string[] ContentShape(HttpResponseMessage response) =>
        [.. response.Content.Headers
            .Select(h => $"{h.Key}: {string.Join(",", h.Value)}")
            .Order(StringComparer.Ordinal)];

    [Fact]
    public async Task PerToken_PartitionsAreIsolated_ASecondTokenIsUnaffected()
    {
        SetLimits(perIp: 1000, perToken: 2);
        using var broker = await fixture.CreateBrokerClient();
        var first = await fixture.IssueLink(broker);
        var second = await fixture.IssueLink(broker);
        using var customer = fixture.CreatePublicClient();

        for (var i = 0; i < 2; i++)
        {
            await customer.GetAsync($"/public/{first.Token}");
        }

        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            (await customer.GetAsync($"/public/{first.Token}")).StatusCode);
        // Same IP, different token: exhausting one link must not lock out another customer.
        Assert.Equal(HttpStatusCode.OK, (await customer.GetAsync($"/public/{second.Token}")).StatusCode);
    }

    [Fact]
    public async Task PerIp_ThrottlesAcrossDifferentTokens()
    {
        const int PerIp = 3;
        SetLimits(perIp: PerIp, perToken: 1000);
        using var broker = await fixture.CreateBrokerClient();
        var links = new List<string>();
        for (var i = 0; i < PerIp + 1; i++)
        {
            links.Add((await fixture.IssueLink(broker)).Token);
        }

        using var customer = fixture.CreatePublicClient();
        for (var i = 0; i < PerIp; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await customer.GetAsync($"/public/{links[i]}")).StatusCode);
        }

        // A fresh token cannot buy more budget from the same source address.
        var throttled = await customer.GetAsync($"/public/{links[PerIp]}");
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
    }

    [Fact]
    public async Task PerIp_PartitionsAreIsolated_ADifferentSourceIsUnaffected()
    {
        SetLimits(perIp: 2, perToken: 1000);
        using var broker = await fixture.CreateBrokerClient();
        var link = await fixture.IssueLink(broker);

        using var noisy = fixture.CreatePublicClient();
        for (var i = 0; i < 2; i++)
        {
            await noisy.GetAsync($"/public/{link.Token}");
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await noisy.GetAsync($"/public/{link.Token}")).StatusCode);

        using var quiet = fixture.CreatePublicClient();
        Assert.Equal(HttpStatusCode.OK, (await quiet.GetAsync($"/public/{link.Token}")).StatusCode);
    }

    [Fact]
    public async Task InvalidTokens_ThrottleToo_SoTheLimiterIsNotAnOracle()
    {
        const int PerToken = 2;
        SetLimits(perIp: 1000, perToken: PerToken);
        using var customer = fixture.CreatePublicClient();
        var bogus = Convert.ToHexString(Guid.NewGuid().ToByteArray());

        for (var i = 0; i < PerToken; i++)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await customer.GetAsync($"/public/{bogus}")).StatusCode);
        }

        // Throttling a nonexistent token exactly like a real one is the point: the 429/404
        // boundary must not tell a scanner which links exist.
        Assert.Equal(HttpStatusCode.TooManyRequests, (await customer.GetAsync($"/public/{bogus}")).StatusCode);
    }

    [Fact]
    public async Task AuthenticatedAndHealthEndpoints_AreNeverThrottled()
    {
        SetLimits(perIp: 1, perToken: 1);
        using var client = fixture.CreateClient().WithTestIp(PublicLinkFlows.NextTestIp());

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        }
    }

    [Fact]
    public async Task OversizeBody_IsRejectedWithPayloadTooLarge()
    {
        var options = Clone(_original);
        options.MaxFileMb = 1;
        fixture.PublicLink.CurrentValue = options;

        var link = await fixture.IssueLink();
        using var customer = fixture.CreatePublicClient();

        using var content = new StringContent(
            new string('x', 2 * 1024 * 1024), Encoding.UTF8, "application/json");
        var response = await customer.PostAsync($"/public/{link.Token}/submit", content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task BodyWithinTheCap_PassesTheFilter()
    {
        var options = Clone(_original);
        options.MaxFileMb = 1;
        fixture.PublicLink.CurrentValue = options;

        var link = await fixture.IssueLink();
        using var customer = fixture.CreatePublicClient();

        // The attach is within the cap too, and it has to happen: since slice 5.3 a submission needs a
        // supporting document, so without it this would answer 400 and pass the filter for the wrong
        // reason — the assertion below would no longer be about the body size at all.
        await PublicLinkFlows.OpenAndAttach(customer, link.Token);

        var response = await customer.PostAsJsonAsync(
            $"/public/{link.Token}/submit", PublicLinkFlows.CompleteSubmission());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
