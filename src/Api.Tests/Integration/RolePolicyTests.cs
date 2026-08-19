using System.Net;
using Api.Modules.Users;

namespace Api.Tests.Integration;

[Collection("api")]
public sealed class RolePolicyTests(ApiFixture fixture)
{
    private static readonly Dictionary<UserRole, string> Prefixes = new()
    {
        [UserRole.Expert] = "/api/expert",
        [UserRole.Garage] = "/api/garage",
        [UserRole.ClaimOfficer] = "/api/officer",
        [UserRole.Broker] = "/api/broker",
        [UserRole.Admin] = "/api/admin",
    };

    [Theory]
    [InlineData(UserRole.Expert)]
    [InlineData(UserRole.Garage)]
    [InlineData(UserRole.ClaimOfficer)]
    [InlineData(UserRole.Broker)]
    [InlineData(UserRole.Admin)]
    public async Task Role_ReachesItsOwnGroup_AndIsForbiddenEverywhereElse(UserRole role)
    {
        var user = await fixture.CreateUser(role, UserStatus.Active);
        var client = fixture.CreateClient();
        client.WithBearer((await fixture.Login(client, user.Phone)).AccessToken);

        foreach (var (targetRole, prefix) in Prefixes)
        {
            var response = await client.GetAsync($"{prefix}/ping");
            var expected = targetRole == role ? HttpStatusCode.OK : HttpStatusCode.Forbidden;
            Assert.Equal(expected, response.StatusCode);
        }
    }

    [Fact]
    public async Task Anonymous_IsUnauthorized_OnEveryGroup()
    {
        var client = fixture.CreateClient();
        foreach (var prefix in Prefixes.Values)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"{prefix}/ping")).StatusCode);
        }
    }

    [Fact]
    public async Task ExpiredAccessToken_IsUnauthorized()
    {
        var user = await fixture.CreateUser(UserRole.Expert, UserStatus.Active);
        var client = fixture.CreateClient();
        client.WithBearer((await fixture.Login(client, user.Phone)).AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/expert/ping")).StatusCode);

        fixture.Time.Advance(TimeSpan.FromMinutes(fixture.AuthOptions().Jwt.AccessTokenMinutes + 1));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/expert/ping")).StatusCode);
    }
}
