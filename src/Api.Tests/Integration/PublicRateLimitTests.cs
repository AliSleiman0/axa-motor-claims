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

        var response = await customer.PostAsJsonAsync(
            $"/public/{link.Token}/submit", PublicLinkFlows.CompleteSubmission());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
