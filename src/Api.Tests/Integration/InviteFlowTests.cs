using System.Net;
using System.Net.Http.Json;
using Api.Modules.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
    public async Task TheInviteSms_CarriesATappableLink_AndStillCarriesTheTokenItself()
    {
        // pass-2 decision 2. The link is the path anybody will actually use; the token stays because
        // carriers strip URLs and because somebody reading the text on a handset while registering on
        // a laptop needs something they can type across — which is the paste box S1 offers.
        var user = await fixture.CreateUser(UserRole.Expert, UserStatus.Invited);
        var token = await fixture.IssueInvite(user.Id);

        var message = fixture.Sms.LastMessageFor(user.Phone);
        var baseUrl = fixture.Services.GetRequiredService<IOptions<AuthOptions>>().Value.AppBaseUrl;

        Assert.Contains($"{baseUrl.TrimEnd('/')}/invite/{token}", message, StringComparison.Ordinal);

        // The half that keeps the existing suite honest: `CapturingSmsSender` and
        // `scripts/demo-reset.ps1` both read the token back out of this exact phrase, and
        // demo-reset seeds the demo **through the real onboarding path**, so a message this could
        // not be parsed out of would fail the reset rather than the demo. The full stop after the
        // token is load-bearing for the same reason — it stops a greedy pattern swallowing the URL.
        Assert.Equal(token, fixture.Sms.LastInviteTokenFor(user.Phone));
    }

    [Fact]
    public async Task TheInviteLink_ComesFromConfiguration_NotFromTheRequest()
    {
        // Building it from `Request.Host` would be shorter and is wrong: the header is
        // attacker-controlled, and this URL carries a live credential into an SMS sent in AXA's
        // name. Asserting the configured origin is what pins that — the invite here is issued
        // through an HTTP request whose host is the test server's, and the link must not mention it.
        var user = await fixture.CreateUser(UserRole.Garage, UserStatus.Invited);
        await fixture.IssueInvite(user.Id);

        var message = fixture.Sms.LastMessageFor(user.Phone);
        var configured = fixture.Services.GetRequiredService<IOptions<AuthOptions>>().Value.AppBaseUrl;

        Assert.StartsWith("https://PLACEHOLDER-", configured, StringComparison.Ordinal);
        Assert.Contains(configured.TrimEnd('/'), message, StringComparison.Ordinal);
        Assert.DoesNotContain("localhost", message, StringComparison.OrdinalIgnoreCase);
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
