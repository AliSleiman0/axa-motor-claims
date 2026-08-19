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
}
