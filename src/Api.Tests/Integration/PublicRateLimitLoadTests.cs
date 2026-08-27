using System.Net;
using Api.Modules.PublicSurface;

namespace Api.Tests.Integration;

/// <summary>
/// design.md §9.1's limiter under **concurrent** load (slice 7.1). <c>PublicRateLimitTests</c> proves
/// the limits exist, are partitioned, and are not an existence oracle; what it never proves is the
/// property an abuse control is actually bought for — that when twenty-five requests arrive at once,
/// exactly the permitted number get through and every one of the rest is refused with a
/// <c>Retry-After</c>.
/// </summary>
/// <remarks>
/// <para>
/// Its own file because it moves the limits and because its assertions are **exact counts**, which
/// makes it the one place in the suite where partition hygiene is load-bearing rather than tidy.
/// Three things make it deterministic and each is deliberate:
/// </para>
/// <para>
/// **A dedicated source-address block.** <c>PublicLinkFlows.NextTestIp</c> cycles
/// <c>203.0.113.{n % 254 + 1}</c> from a counter shared by every test in the run, so an exact-count
/// assertion could be poisoned by a wrap-around collision inside the same wall-clock minute — a
/// partition arriving with permits already spent. This file draws from <c>198.51.100.x</c> instead
/// and never touches the shared pool.
/// </para>
/// <para>
/// **A fresh token *and* a fresh address for every limit change.** The limiter caches one limiter
/// instance per partition key, so a partition created before the change keeps the old numbers for the
/// rest of its window.
/// </para>
/// <para>
/// **The untested leg is put far out of reach.** <c>PartitionedRateLimiter.CreateChained</c> acquires
/// the address limiter first, and a fixed-window lease that is then disposed does **not** hand its
/// permit back — so a token rejection still spends an address permit. Leaving the other leg at
/// 10,000 keeps chained-lease accounting out of the arithmetic entirely.
/// </para>
/// <para>
/// And no <c>Time.Advance</c> anywhere: the rate limiter runs on the real system clock rather than the
/// injected <c>TimeProvider</c>, so the fake clock cannot open a window and moving it would only give
/// the appearance of controlling one.
/// </para>
/// </remarks>
[Collection("api")]
public sealed class PublicRateLimitLoadTests(ApiFixture fixture) : IDisposable
{
    /// <summary>Concurrent callers, chosen well above every permit count used here.</summary>
    private const int Callers = 25;

    /// <summary>The permit count under test. <c>QueueLimit = 0</c> is what makes this exact.</summary>
    private const int Permits = 5;

    /// <summary>Far enough out of reach that the other half of the chain never decides anything.</summary>
    private const int Unlimited = 10_000;

    private static int _ipCounter;

    private readonly PublicLinkOptions _original = Clone(fixture.PublicLink.CurrentValue);

    public void Dispose() => fixture.PublicLink.CurrentValue = _original;

    /// <summary>
    /// §9.1's per-token limit, all at once: one link, one address, twenty-five simultaneous opens.
    /// </summary>
    [Fact]
    public async Task UnderConcurrentLoad_ExactlyThePermitCountSucceeds()
    {
        SetLimits(perIp: Unlimited, perToken: Permits);

        // Issued *after* the limits move, so its partition is created under the new numbers.
        var link = await fixture.IssueLink();
        using var customer = LoadClient();

        var responses = await Task.WhenAll(
            Enumerable.Range(0, Callers).Select(_ => customer.GetAsync($"/public/{link.Token}")));

        try
        {
            AssertExactlyThePermitCountSucceeded(responses);
        }
        finally
        {
            Dispose(responses);
        }
    }

    /// <summary>
    /// The mirror, and the half that matters more: a caller who holds twenty-five different links
    /// still gets five requests from one address. Varying the token must buy no fresh budget, which is
    /// the whole point of chaining the two limiters rather than partitioning on the pair.
    /// </summary>
    [Fact]
    public async Task UnderConcurrentLoad_TheIpLimitHoldsAcrossTokens()
    {
        SetLimits(perIp: Permits, perToken: Unlimited);

        using var broker = await fixture.CreateBrokerClient();
        var tokens = new List<string>();
        for (var i = 0; i < Callers; i++)
        {
            tokens.Add((await fixture.IssueLink(broker)).Token);
        }

        using var customer = LoadClient();

        var responses = await Task.WhenAll(
            tokens.Select(token => customer.GetAsync($"/public/{token}")));

        try
        {
            AssertExactlyThePermitCountSucceeded(responses);
        }
        finally
        {
            Dispose(responses);
        }
    }

    /// <summary>
    /// The shape both tests assert. Exactness is the claim: "most were refused" would pass with a
    /// limiter that leaked a permit under contention, and leaking one permit per burst is precisely
    /// how a rate limit stops being one.
    /// </summary>
    private static void AssertExactlyThePermitCountSucceeded(IReadOnlyList<HttpResponseMessage> responses)
    {
        Assert.Equal(Permits, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(Callers - Permits, responses.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests));

        // Nothing else may appear: a 500 or a 404 in this set would mean the burst had found some
        // other failure and the counts above had balanced by accident.
        Assert.All(
            responses,
            r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.TooManyRequests }));

        // §9.1's refusal has to be actionable — a customer holding a live link is told to wait rather
        // than left to guess. Every one of them, not the first one anybody looked at.
        Assert.All(
            responses.Where(r => r.StatusCode == HttpStatusCode.TooManyRequests),
            r => Assert.True(r.Headers.Contains("Retry-After")));
    }

    private static void Dispose(IEnumerable<HttpResponseMessage> responses)
    {
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    /// <summary>A source address from this file's own block, so no other test can have spent it.</summary>
    private HttpClient LoadClient() =>
        fixture.CreateClient().WithTestIp($"198.51.100.{Interlocked.Increment(ref _ipCounter) % 254 + 1}");

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
