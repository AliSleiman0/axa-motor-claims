using Api.Integrations.Push;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Api.Tests.Integrations;

/// <summary>
/// The FCM OAuth exchange against Google's real token endpoint (slice 6.3).
///
/// Skipped unless a service-account key is configured — see <see cref="FcmLiveFactAttribute"/> for
/// why this exists at all and why it is an environment variable rather than user-secrets.
///
/// **This is the only test in the suite that can fail because of something outside this repository**,
/// which is the point: everything else about the FCM adapter is asserted against a scripted
/// transport, so nothing else notices if the key is revoked, the clock has drifted far enough for
/// Google to reject the assertion, or the Cloud Messaging API is disabled on the project.
/// </summary>
public sealed class FcmAccessTokensLiveTests
{
    [FcmLiveFact]
    public async Task ARealServiceAccountKeyMintsAnAccessToken()
    {
        var tokens = Live();

        var first = await tokens.Get(CancellationToken.None);

        // Google returns an opaque bearer. Asserting only that it is substantial and reusable —
        // its contents are not ours to depend on, and it is a live credential, so it is never
        // written to the test output.
        Assert.False(string.IsNullOrWhiteSpace(first));
        Assert.True(first.Length > 32);

        // **The regression this file was written for.** The first version disposed the `RSA` after
        // signing, and `Microsoft.IdentityModel` caches signature providers process-wide — so the
        // *second* mint signed with a disposed handle and threw. Cached tokens make that invisible
        // here, so the cache is bypassed by asking a second instance to mint its own.
        var second = await Live().Get(CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(second));
    }

    [FcmLiveFact]
    public async Task TheSameInstanceMintsOnceAndReusesIt()
    {
        var tokens = Live();

        var first = await tokens.Get(CancellationToken.None);
        var again = await tokens.Get(CancellationToken.None);

        // Against the real endpoint rather than a stub: Google rate limits token minting, and the
        // sender walks every handset a user owns on the assignment-ingestion path. The frozen clock
        // means the cached deadline cannot lapse mid-test.
        Assert.Equal(first, again);
    }

    private static FcmAccessTokens Live()
    {
        var options = new PushOptions { Mode = PushModes.WebPush };
        options.Fcm.Enabled = true;
        options.Fcm.ProjectId = FcmLiveFactAttribute.ProjectId!;
        options.Fcm.ServiceAccountJsonPath = FcmLiveFactAttribute.KeyPath!;

        // A real clock would be fine too, but a frozen one makes the caching assertion exact rather
        // than merely fast. Google validates the assertion's `iat`/`exp` against its own clock, so
        // this must be roughly now — `FakeTimeProvider`'s default epoch would be rejected.
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);

        return new FcmAccessTokens(
            new LiveHttpClientFactory(),
            Options.Create(options),
            time,
            NullLogger<FcmAccessTokens>.Instance);
    }

    /// <summary>A factory that hands out a plain client — this test is deliberately not stubbed.</summary>
    private sealed class LiveHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new() { Timeout = TimeSpan.FromSeconds(30) };
    }
}
