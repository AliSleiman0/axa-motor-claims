using System.Net;
using System.Net.Http.Json;
using Api.Modules.Users;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Integration;

[Collection("api")]
public sealed class OtpLoginTests(ApiFixture fixture)
{
    [Fact]
    public async Task Request_ForActiveUser_SendsSingleCode_AndStoresOnlyAHash()
    {
        var user = await fixture.CreateUser(UserRole.Expert, UserStatus.Active);
        var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/otp/request", new { phone = user.Phone });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, fixture.Sms.CountFor(user.Phone));
        var code = fixture.Sms.LastOtpFor(user.Phone);
        Assert.Matches(@"^\d{6}$", code);

        await using var db = fixture.CreateDbContext();
        var challenge = await db.OtpChallenges.SingleAsync(c => c.Phone == user.Phone);
        Assert.NotEqual(code, challenge.CodeHash);
        Assert.Equal(64, challenge.CodeHash.Length);
        Assert.Null(challenge.ConsumedAt);
    }

    [Fact]
    public async Task Verify_WithCorrectCode_ReturnsTokens_ThatCarryTheRole()
    {
        var user = await fixture.CreateUser(UserRole.Expert, UserStatus.Active);
        var client = fixture.CreateClient();

        var tokens = await fixture.Login(client, user.Phone);

        Assert.False(string.IsNullOrEmpty(tokens.AccessToken));
        Assert.False(string.IsNullOrEmpty(tokens.RefreshToken));

        client.WithBearer(tokens.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/expert/ping")).StatusCode);

        var me = await client.GetFromJsonAsync<MeDto>("/auth/me");
        Assert.Equal(user.Phone, me!.Phone);
        Assert.Equal(UserRoles.Expert, me.Role);
    }

    [Fact]
    public async Task Verify_AfterTtlExpiry_IsRejected()
    {
        var user = await fixture.CreateUser(UserRole.Garage, UserStatus.Active);
        var client = fixture.CreateClient();
        (await client.PostAsJsonAsync("/auth/otp/request", new { phone = user.Phone })).EnsureSuccessStatusCode();
        var code = fixture.Sms.LastOtpFor(user.Phone);

        fixture.Time.Advance(TimeSpan.FromMinutes(fixture.AuthOptions().OtpTtlMinutes + 1));

        var verify = await client.PostAsJsonAsync("/auth/otp/verify", new { phone = user.Phone, code });
        Assert.Equal(HttpStatusCode.Unauthorized, verify.StatusCode);
    }

    [Fact]
    public async Task Verify_AtMaxAttempts_RejectsEvenTheCorrectCode()
    {
        var user = await fixture.CreateUser(UserRole.Broker, UserStatus.Active);
        var client = fixture.CreateClient();
        (await client.PostAsJsonAsync("/auth/otp/request", new { phone = user.Phone })).EnsureSuccessStatusCode();
        var code = fixture.Sms.LastOtpFor(user.Phone);
        var wrong = code == "000000" ? "000001" : "000000";
        var maxAttempts = fixture.AuthOptions().OtpMaxAttempts;

        for (var i = 0; i < maxAttempts; i++)
        {
            var attempt = await client.PostAsJsonAsync("/auth/otp/verify", new { phone = user.Phone, code = wrong });
            Assert.Equal(HttpStatusCode.Unauthorized, attempt.StatusCode);
        }

        var lockedOut = await client.PostAsJsonAsync("/auth/otp/verify", new { phone = user.Phone, code });
        Assert.Equal(HttpStatusCode.Unauthorized, lockedOut.StatusCode);

        await using var db = fixture.CreateDbContext();
        var challenge = await db.OtpChallenges.SingleAsync(c => c.Phone == user.Phone);
        Assert.Equal(maxAttempts, challenge.Attempts);
    }

    [Fact]
    public async Task Verify_ReplayOfConsumedCode_IsRejected()
    {
        var user = await fixture.CreateUser(UserRole.ClaimOfficer, UserStatus.Active);
        var client = fixture.CreateClient();
        (await client.PostAsJsonAsync("/auth/otp/request", new { phone = user.Phone })).EnsureSuccessStatusCode();
        var code = fixture.Sms.LastOtpFor(user.Phone);

        var first = await client.PostAsJsonAsync("/auth/otp/verify", new { phone = user.Phone, code });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var replay = await client.PostAsJsonAsync("/auth/otp/verify", new { phone = user.Phone, code });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }

    [Fact]
    public async Task Request_NewChallenge_SupersedesTheOldOne()
    {
        var user = await fixture.CreateUser(UserRole.Expert, UserStatus.Active);
        var client = fixture.CreateClient();
        (await client.PostAsJsonAsync("/auth/otp/request", new { phone = user.Phone })).EnsureSuccessStatusCode();
        var oldCode = fixture.Sms.LastOtpFor(user.Phone);

        fixture.Time.Advance(TimeSpan.FromSeconds(fixture.AuthOptions().OtpResendSeconds + 1));
        (await client.PostAsJsonAsync("/auth/otp/request", new { phone = user.Phone })).EnsureSuccessStatusCode();
        var newCode = fixture.Sms.LastOtpFor(user.Phone);

        var oldVerify = await client.PostAsJsonAsync("/auth/otp/verify", new { phone = user.Phone, code = oldCode });
        Assert.Equal(HttpStatusCode.Unauthorized, oldVerify.StatusCode);

        var newVerify = await client.PostAsJsonAsync("/auth/otp/verify", new { phone = user.Phone, code = newCode });
        Assert.Equal(HttpStatusCode.OK, newVerify.StatusCode);
    }

    [Fact]
    public async Task Request_WithinResendWindow_IsThrottled_WithRetryAfter()
    {
        var user = await fixture.CreateUser(UserRole.Garage, UserStatus.Active);
        var client = fixture.CreateClient();
        (await client.PostAsJsonAsync("/auth/otp/request", new { phone = user.Phone })).EnsureSuccessStatusCode();

        var throttled = await client.PostAsJsonAsync("/auth/otp/request", new { phone = user.Phone });
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        Assert.True(throttled.Headers.Contains("Retry-After"));

        fixture.Time.Advance(TimeSpan.FromSeconds(fixture.AuthOptions().OtpResendSeconds + 1));
        var again = await client.PostAsJsonAsync("/auth/otp/request", new { phone = user.Phone });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
    }

    [Fact]
    public async Task UnknownPhone_GetsUniformResponses_AndNoSms()
    {
        var ghostPhone = TestPhones.Next();
        var client = fixture.CreateClient();

        var request = await client.PostAsJsonAsync("/auth/otp/request", new { phone = ghostPhone });
        Assert.Equal(HttpStatusCode.OK, request.StatusCode);
        Assert.Equal(0, fixture.Sms.CountFor(ghostPhone));

        var ghostVerify = await client.PostAsJsonAsync("/auth/otp/verify", new { phone = ghostPhone, code = "123456" });
        Assert.Equal(HttpStatusCode.Unauthorized, ghostVerify.StatusCode);
        var ghostBody = await ghostVerify.Content.ReadAsStringAsync();

        var user = await fixture.CreateUser(UserRole.Expert, UserStatus.Active);
        (await client.PostAsJsonAsync("/auth/otp/request", new { phone = user.Phone })).EnsureSuccessStatusCode();
        var code = fixture.Sms.LastOtpFor(user.Phone);
        var wrong = code == "000000" ? "000001" : "000000";
        var wrongVerify = await client.PostAsJsonAsync("/auth/otp/verify", new { phone = user.Phone, code = wrong });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongVerify.StatusCode);

        Assert.Equal(ghostBody, await wrongVerify.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task InvitedUser_RequestingLoginCode_GetsOkButNoSms()
    {
        var user = await fixture.CreateUser(UserRole.Garage, UserStatus.Invited);
        var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/otp/request", new { phone = user.Phone });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, fixture.Sms.CountFor(user.Phone));
    }
}
