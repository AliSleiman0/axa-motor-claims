using System.Net;
using System.Net.Http.Json;
using Api.Modules.Users;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Integration;

[Collection("api")]
public sealed class DeactivationTests(ApiFixture fixture)
{
    [Fact]
    public async Task Deactivate_KillsLiveAccessToken_Refresh_AndSilencesLogin()
    {
        var admin = await fixture.CreateUser(UserRole.Admin, UserStatus.Active);
        var adminClient = fixture.CreateClient();
        adminClient.WithBearer((await fixture.Login(adminClient, admin.Phone)).AccessToken);

        var victim = await fixture.CreateUser(UserRole.Expert, UserStatus.Active);
        var victimClient = fixture.CreateClient();
        var victimTokens = await fixture.Login(victimClient, victim.Phone);
        victimClient.WithBearer(victimTokens.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await victimClient.GetAsync("/api/expert/ping")).StatusCode);

        var deactivate = await adminClient.PostAsync($"/api/admin/users/{victim.Id}/deactivate", null);
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

        // the live access token dies on its next request (§9: invalidated on deactivation)
        Assert.Equal(HttpStatusCode.Forbidden, (await victimClient.GetAsync("/api/expert/ping")).StatusCode);

        var refresh = await victimClient.PostAsJsonAsync("/auth/refresh", new { refreshToken = victimTokens.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);

        fixture.Time.Advance(TimeSpan.FromSeconds(fixture.AuthOptions().OtpResendSeconds + 1));
        var smsBefore = fixture.Sms.CountFor(victim.Phone);
        var request = await victimClient.PostAsJsonAsync("/auth/otp/request", new { phone = victim.Phone });
        Assert.Equal(HttpStatusCode.OK, request.StatusCode);
        Assert.Equal(smsBefore, fixture.Sms.CountFor(victim.Phone));

        await using var db = fixture.CreateDbContext();
        var reloaded = await db.Users.SingleAsync(u => u.Id == victim.Id);
        Assert.Equal(UserStatus.Inactive, reloaded.Status);
        Assert.NotNull(reloaded.InactivatedAt);
        Assert.Equal(victim.Phone, reloaded.Phone); // in-flight data untouched (§5.4)
    }

    [Fact]
    public async Task Deactivate_ByNonAdmin_IsForbidden()
    {
        var garage = await fixture.CreateUser(UserRole.Garage, UserStatus.Active);
        var client = fixture.CreateClient();
        client.WithBearer((await fixture.Login(client, garage.Phone)).AccessToken);

        var target = await fixture.CreateUser(UserRole.Expert, UserStatus.Active);
        var response = await client.PostAsync($"/api/admin/users/{target.Id}/deactivate", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
