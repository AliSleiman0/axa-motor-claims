using System.Net;
using System.Net.Http.Json;
using Api.Modules.Users;

namespace Api.Tests.Integration;

/// <summary>The slice 1.2 DoD as one test: register→login→refresh→deactivate-blocks-login, all via the API.</summary>
[Collection("api")]
public sealed class EndToEndAuthTests(ApiFixture fixture)
{
    [Fact]
    public async Task Invite_Register_Login_Refresh_Deactivate_FullLifecycle()
    {
        // S1 register: invite → accept → OTP verify → active
        var user = await fixture.CreateUser(UserRole.Expert, UserStatus.Invited);
        var inviteToken = await fixture.IssueInvite(user.Id);
        var client = fixture.CreateClient();
        (await client.PostAsJsonAsync("/auth/invite/accept", new { token = inviteToken })).EnsureSuccessStatusCode();
        var code = fixture.Sms.LastOtpFor(user.Phone);
        (await client.PostAsJsonAsync("/auth/invite/verify", new { token = inviteToken, code })).EnsureSuccessStatusCode();

        // login
        var tokens = await fixture.Login(client, user.Phone);
        client.WithBearer(tokens.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/expert/ping")).StatusCode);

        // refresh
        var refresh = await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = tokens.RefreshToken });
        refresh.EnsureSuccessStatusCode();
        var rotated = await refresh.Content.ReadFromJsonAsync<TokenPairDto>();

        // deactivate via the API as the seeded admin
        var adminPhone = fixture.AuthOptions().SeedAdmin.Phone;
        var adminClient = fixture.CreateClient();
        adminClient.WithBearer((await fixture.Login(adminClient, adminPhone)).AccessToken);
        (await adminClient.PostAsync($"/api/admin/users/{user.Id}/deactivate", null)).EnsureSuccessStatusCode();

        // deactivation blocks everything
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/expert/ping")).StatusCode);
        var deadRefresh = await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = rotated!.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, deadRefresh.StatusCode);

        fixture.Time.Advance(TimeSpan.FromSeconds(fixture.AuthOptions().OtpResendSeconds + 1));
        var smsBefore = fixture.Sms.CountFor(user.Phone);
        (await client.PostAsJsonAsync("/auth/otp/request", new { phone = user.Phone })).EnsureSuccessStatusCode();
        Assert.Equal(smsBefore, fixture.Sms.CountFor(user.Phone));
    }
}
