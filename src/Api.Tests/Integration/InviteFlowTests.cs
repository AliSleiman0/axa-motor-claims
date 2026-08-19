using System.Net;
using System.Net.Http.Json;
using Api.Modules.Users;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Integration;

[Collection("api")]
public sealed class InviteFlowTests(ApiFixture fixture)
{
    [Fact]
    public async Task S1_InviteAcceptVerify_ActivatesTheUser_AndReturnsTokens()
    {
        var user = await fixture.CreateUser(UserRole.Garage, UserStatus.Invited);
        var inviteToken = await fixture.IssueInvite(user.Id);
        Assert.Equal(1, fixture.Sms.CountFor(user.Phone)); // the invite SMS

        var client = fixture.CreateClient();
        var accept = await client.PostAsJsonAsync("/auth/invite/accept", new { token = inviteToken });
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        Assert.Equal(2, fixture.Sms.CountFor(user.Phone)); // + the OTP
        var code = fixture.Sms.LastOtpFor(user.Phone);

        var verify = await client.PostAsJsonAsync("/auth/invite/verify", new { token = inviteToken, code });
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        var tokens = await verify.Content.ReadFromJsonAsync<TokenPairDto>();

        await using (var db = fixture.CreateDbContext())
        {
            var reloaded = await db.Users.SingleAsync(u => u.Id == user.Id);
            Assert.Equal(UserStatus.Active, reloaded.Status);
            var invite = await db.Invites.SingleAsync(i => i.UserId == user.Id);
            Assert.NotNull(invite.UsedAt);
        }

        client.WithBearer(tokens!.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/garage/ping")).StatusCode);
    }

    [Fact]
    public async Task InvalidExpiredAndUsedInvites_AreByteIdentical404s()
    {
        var client = fixture.CreateClient();

        // invalid
        var invalid = await client.PostAsJsonAsync("/auth/invite/accept", new { token = "not-a-real-token" });
        Assert.Equal(HttpStatusCode.NotFound, invalid.StatusCode);
        var invalidBody = await invalid.Content.ReadAsStringAsync();

        // expired
        var expiredUser = await fixture.CreateUser(UserRole.Expert, UserStatus.Invited);
        var expiredToken = await fixture.IssueInvite(expiredUser.Id);
        fixture.Time.Advance(TimeSpan.FromDays(fixture.AuthOptions().InviteValidityDays + 1));
        var expired = await client.PostAsJsonAsync("/auth/invite/accept", new { token = expiredToken });
        Assert.Equal(HttpStatusCode.NotFound, expired.StatusCode);

        // used: run the full happy path, then accept again
        var usedUser = await fixture.CreateUser(UserRole.Broker, UserStatus.Invited);
        var usedToken = await fixture.IssueInvite(usedUser.Id);
        (await client.PostAsJsonAsync("/auth/invite/accept", new { token = usedToken })).EnsureSuccessStatusCode();
        var code = fixture.Sms.LastOtpFor(usedUser.Phone);
        (await client.PostAsJsonAsync("/auth/invite/verify", new { token = usedToken, code })).EnsureSuccessStatusCode();
        var used = await client.PostAsJsonAsync("/auth/invite/accept", new { token = usedToken });
        Assert.Equal(HttpStatusCode.NotFound, used.StatusCode);

        Assert.Equal(invalidBody, await expired.Content.ReadAsStringAsync());
        Assert.Equal(invalidBody, await used.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task InviteAccept_ForDeactivatedUser_Is404()
    {
        var user = await fixture.CreateUser(UserRole.Expert, UserStatus.Invited);
        var inviteToken = await fixture.IssueInvite(user.Id);

        await using (var db = fixture.CreateDbContext())
        {
            var reloaded = await db.Users.SingleAsync(u => u.Id == user.Id);
            reloaded.Status = UserStatus.Inactive;
            await db.SaveChangesAsync();
        }

        var client = fixture.CreateClient();
        var accept = await client.PostAsJsonAsync("/auth/invite/accept", new { token = inviteToken });
        Assert.Equal(HttpStatusCode.NotFound, accept.StatusCode);
    }
}
