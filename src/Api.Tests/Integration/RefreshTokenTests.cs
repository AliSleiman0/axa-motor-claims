using Api.Modules.Audit;
using System.Net;
using System.Net.Http.Json;
using Api.Modules.Users;

namespace Api.Tests.Integration;

[Collection("api")]
public sealed class RefreshTokenTests(ApiFixture fixture)
{
    [Fact]
    public async Task Refresh_RotatesThePair_AndOldAccessTokenStaysValidUntilExpiry()
    {
        var user = await fixture.CreateUser(UserRole.Expert, UserStatus.Active);
        var client = fixture.CreateClient();
        var pair = await fixture.Login(client, user.Phone);

        var refresh = await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = pair.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var rotated = await refresh.Content.ReadFromJsonAsync<TokenPairDto>();
        Assert.NotEqual(pair.RefreshToken, rotated!.RefreshToken);

        client.WithBearer(pair.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/expert/ping")).StatusCode);
    }

    [Fact]
    public async Task ReuseOfARotatedToken_RevokesTheWholeFamily()
    {
        var user = await fixture.CreateUser(UserRole.Garage, UserStatus.Active);
        var client = fixture.CreateClient();
        var pair = await fixture.Login(client, user.Phone);

        var refresh = await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = pair.RefreshToken });
        var rotated = await refresh.Content.ReadFromJsonAsync<TokenPairDto>();

        var reuse = await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = pair.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);

        // the replacement issued just before the reuse is dead too
        var replacement = await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = rotated!.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, replacement.StatusCode);
    }

    [Fact]
    public async Task Refresh_AfterExpiry_IsRejected()
    {
        var user = await fixture.CreateUser(UserRole.Broker, UserStatus.Active);
        var client = fixture.CreateClient();
        var pair = await fixture.Login(client, user.Phone);

        fixture.Time.Advance(TimeSpan.FromDays(fixture.AuthOptions().Jwt.RefreshTokenDays + 1));

        var refresh = await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = pair.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    /// <summary>
    /// §9's trail for the one automatic "somebody may be replaying a stolen credential" signal the
    /// app has (slice 7.2). Until this slice it was silent: the user was signed out of every device
    /// and nothing anywhere said why.
    /// </summary>
    [Fact]
    public async Task ReuseOfARotatedToken_IsAudited()
    {
        var user = await fixture.CreateUser(UserRole.Broker, UserStatus.Active);
        var client = fixture.CreateClient();
        var pair = await fixture.Login(client, user.Phone);

        (await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = pair.RefreshToken }))
            .EnsureSuccessStatusCode();

        // The same token again — rotated already, so this is either a replay or a client bug, and
        // §4 says kill the family either way.
        var replay = await client.PostAsJsonAsync(
            "/auth/refresh", new { refreshToken = pair.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        var audit = await fixture.AuditRow(AuditActions.RefreshTokenFamilyRevoked, user.Id);

        // The actor is the token's owner, because the caller is by definition unauthenticated —
        // presenting a revoked refresh token is what got them here, and it is the only identity in
        // the request. Nothing about the presented token is recorded: §9 stores only its hash.
        Assert.Equal(user.Id, audit.ActorUserId);
        Assert.Equal(AuditEntityKinds.AppUser, audit.EntityKind);
        Assert.Contains("reuse_detected", audit.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(pair.RefreshToken, audit.Detail, StringComparison.Ordinal);
    }

}
