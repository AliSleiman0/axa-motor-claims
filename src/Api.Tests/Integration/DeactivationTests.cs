using Api.Modules.Audit;
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

    /// <summary>
    /// §9 says tokens are invalidated on deactivation; until slice 7.2 that meant refresh tokens
    /// only. §8's fan-out reads no session — <c>CompositePushSender</c> resolves a user's live
    /// subscriptions and device tokens and sends to all of them — so a deactivated expert kept
    /// receiving claim assignments, visa number in the body, on a phone they may no longer be
    /// entitled to hold.
    /// </summary>
    [Fact]
    public async Task Deactivate_AlsoRevokesTheUsersDevices()
    {
        var admin = await fixture.CreateUser(UserRole.Admin, UserStatus.Active);
        var adminClient = fixture.CreateClient();
        adminClient.WithBearer((await fixture.Login(adminClient, admin.Phone)).AccessToken);

        using var victim = await fixture.CreateMappedExpert();
        (await victim.Client.PostAsJsonAsync("/api/push/subscriptions", PushFlows.Subscription()))
            .EnsureSuccessStatusCode();
        (await victim.Client.PostAsJsonAsync("/api/push/device-tokens", PushFlows.DeviceTokenBody()))
            .EnsureSuccessStatusCode();

        var subscription = Assert.Single(await fixture.SubscriptionsOf(victim.User.Id));
        var device = Assert.Single(await fixture.DeviceTokensOf(victim.User.Id));

        (await adminClient.PostAsync($"/api/admin/users/{victim.User.Id}/deactivate", null))
            .EnsureSuccessStatusCode();

        Assert.NotNull(Assert.Single(await fixture.SubscriptionsOf(victim.User.Id)).RevokedAt);
        Assert.NotNull(Assert.Single(await fixture.DeviceTokensOf(victim.User.Id)).RevokedAt);

        // Revoked rather than deleted — the user may be reactivated, and a `notification` row naming
        // one of these devices should still resolve. The reason is what tells this apart from a
        // handset somebody switched off themselves.
        Assert.Contains(
            "deactivated",
            (await fixture.AuditRow(AuditActions.PushSubscriptionRemoved, subscription.Id)).Detail,
            StringComparison.Ordinal);
        Assert.Contains(
            "deactivated",
            (await fixture.AuditRow(AuditActions.DeviceTokenRevoked, device.Id)).Detail,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The registry is otherwise a table that only grows: every 404/410 from a push service, every
    /// FCM <c>UNREGISTERED</c>, every OEM battery kill and every handset that changes hands adds a
    /// revoked row. The prune keeps the recent history a support question wants and drops the rest.
    /// </summary>
    [Fact]
    public async Task RevokedDeviceRowsArePrunedOnceTheWindowHasPassed()
    {
        using var expert = await fixture.CreateMappedExpert();
        (await expert.Client.PostAsJsonAsync("/api/push/device-tokens", PushFlows.DeviceTokenBody()))
            .EnsureSuccessStatusCode();

        var device = Assert.Single(await fixture.DeviceTokensOf(expert.User.Id));
        await RevokeNow(device.Id);

        // A live row is never a candidate whatever its age, and a revoked one is not one yet.
        await fixture.Sweep();
        Assert.Single(await fixture.DeviceTokensOf(expert.User.Id));

        await fixture.WithRetention(r => r.RevokedDeviceDays = 0, () => fixture.Sweep());

        Assert.Empty(await fixture.DeviceTokensOf(expert.User.Id));
    }

    /// <summary>
    /// Revokes at the shared clock rather than at <c>PushFlows.RevokeDevice</c>'s fixed date. The
    /// integration classes run as one serialized collection and the clock only moves forward, so a
    /// row revoked at a *literal* moment drifts out of the retention window as the run proceeds — the
    /// test then passes alone and fails in company, which is the worst way to learn it.
    /// </summary>
    private async Task RevokeNow(Guid deviceTokenId)
    {
        await using var db = fixture.CreateDbContext();
        var now = fixture.Time.GetUtcNow().UtcDateTime;
        await db.Database.ExecuteSqlAsync(
            $"UPDATE device_token SET revoked_at = {now} WHERE id = {deviceTokenId}");
    }

    [Fact]
    public async Task ALiveDeviceRowIsNeverPruned()
    {
        using var expert = await fixture.CreateMappedExpert();
        (await expert.Client.PostAsJsonAsync("/api/push/device-tokens", PushFlows.DeviceTokenBody()))
            .EnsureSuccessStatusCode();

        await fixture.WithRetention(r => r.RevokedDeviceDays = 0, () => fixture.Sweep());

        Assert.Single(await fixture.DeviceTokensOf(expert.User.Id));
    }

}
