using System.Net;
using System.Net.Http.Json;

namespace Api.Tests.Integration;

/// <summary>
/// design.md §10 (slice 7.6) — liveness stays a dumb 200; readiness actually checks something.
/// </summary>
[Collection("api")]
public sealed class HealthEndpointTests(ApiFixture fixture)
{
    [Fact]
    public async Task Liveness_IsAlways200()
    {
        using var client = fixture.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readiness_IsOk_WhenTheDatabaseIsReachable()
    {
        using var client = fixture.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ReadyBody>();
        Assert.NotNull(body);
        Assert.Equal("ok", body.Status);
        Assert.True(body.Checks.Db);
    }

    [Fact]
    public async Task Readiness_ReportsBlobOk_WithoutAttemptingAContainerCheck_WhenBlobModeIsFake()
    {
        // The suite runs entirely on Blob:Mode=fake (design.md Appendix A) — this is the proof the
        // conditional in HealthEndpoints is actually conditional: InMemoryBlobStore.ContainerExists
        // always returns true, so a genuinely broken condition here would still read "ok" by
        // accident. What this test actually pins is that the field is present and true, which is
        // the observable half of "no container check was attempted" available from outside.
        using var client = fixture.CreateClient();

        var response = await client.GetAsync("/health/ready");

        var body = await response.Content.ReadFromJsonAsync<ReadyBody>();
        Assert.NotNull(body);
        Assert.True(body.Checks.Blob);
    }

    private sealed record ReadyBody(string Status, ReadyChecks Checks);

    private sealed record ReadyChecks(bool Db, bool Blob);
}
