using System.Net;
using System.Net.Http.Json;
using Api.Integrations.Push;
using Api.Modules.Users;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Integration;

/// <summary>
/// Device registration for §8's assignment popup (slice 3.4).
///
/// The load-bearing test is the concurrency one: "one row per browser" is enforced by the unique
/// index on (user_id, endpoint_hash), not by the read that precedes the insert, and 1.5 / 2.2 / 2.4
/// each cost a review round for believing otherwise.
/// </summary>
[Collection("api")]
public sealed class PushSubscriptionTests(ApiFixture fixture)
{
    [Fact]
    public async Task SubscribingRegistersTheBrowser()
    {
        using var expert = await fixture.CreateMappedExpert();

        var response = await expert.Client.PostAsJsonAsync("/api/push/subscriptions", PushFlows.Subscription());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var row = Assert.Single(await fixture.SubscriptionsOf(expert.User.Id));
        Assert.Null(row.RevokedAt);
        Assert.Equal(PushFlows.P256dh, row.P256dh);
    }

    [Fact]
    public async Task FourParallelPostsCreateExactlyOneRow()
    {
        using var expert = await fixture.CreateMappedExpert();
        var body = PushFlows.Subscription();

        // Two tabs, a double-click, a retry — all real. Every one of these passes the read-then-write
        // check before any of them inserts, so without the unique index this creates four rows and
        // the expert then gets four identical popups per assignment. Removing the index from the
        // configuration turns this test red, which is how it was verified.
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            expert.Client.PostAsJsonAsync("/api/push/subscriptions", body)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Single(await fixture.SubscriptionsOf(expert.User.Id));

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task ResubscribingRefreshesTheKeysAndClearsRevocation()
    {
        using var expert = await fixture.CreateMappedExpert();
        var endpoint = PushFlows.NextEndpoint();

        await expert.Client.PostAsJsonAsync("/api/push/subscriptions", PushFlows.Subscription(endpoint));

        var created = Assert.Single(await fixture.SubscriptionsOf(expert.User.Id));
        await fixture.Revoke(created.Id);

        // A browser rotates its keys on its own schedule, and a stale pair does not fail loudly: the
        // push service accepts the message and the device silently cannot decrypt it. So the upsert
        // has to take the new keys, and it has to un-revoke — the endpoint is demonstrably alive
        // again, since the browser just presented it.
        await expert.Client.PostAsJsonAsync(
            "/api/push/subscriptions", PushFlows.Subscription(endpoint, p256dh: PushFlows.OtherP256dh));

        var refreshed = Assert.Single(await fixture.SubscriptionsOf(expert.User.Id));
        Assert.Equal(created.Id, refreshed.Id);
        Assert.Equal(PushFlows.OtherP256dh, refreshed.P256dh);
        Assert.Null(refreshed.RevokedAt);

        // CreatedAt is not touched: the panel offers the button every session, so rewriting it would
        // turn "when this device first registered" into "the last time somebody pressed the button".
        Assert.Equal(created.CreatedAt, refreshed.CreatedAt);
    }

    [Fact]
    public async Task OneUsersSubscriptionIsInvisibleToAnother()
    {
        using var first = await fixture.CreateMappedExpert();
        using var second = await fixture.CreateMappedExpert();
        var endpoint = PushFlows.NextEndpoint();

        // The same browser, two accounts — a shared device, which is allowed on purpose (the unique
        // index is scoped to the user). What must not happen is one of them silently owning it.
        await first.Client.PostAsJsonAsync("/api/push/subscriptions", PushFlows.Subscription(endpoint));
        await second.Client.PostAsJsonAsync("/api/push/subscriptions", PushFlows.Subscription(endpoint));

        Assert.Single(await fixture.SubscriptionsOf(first.User.Id));
        Assert.Single(await fixture.SubscriptionsOf(second.User.Id));
    }

    [Fact]
    public async Task UnsubscribingRemovesOnlyTheCallersRow()
    {
        using var mine = await fixture.CreateMappedExpert();
        using var theirs = await fixture.CreateMappedExpert();
        var endpoint = PushFlows.NextEndpoint();

        await mine.Client.PostAsJsonAsync("/api/push/subscriptions", PushFlows.Subscription(endpoint));
        await theirs.Client.PostAsJsonAsync("/api/push/subscriptions", PushFlows.Subscription(endpoint));

        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/push/subscriptions")
        {
            Content = JsonContent.Create(new { endpoint }),
        };
        using var response = await mine.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await fixture.SubscriptionsOf(mine.User.Id));

        // Knowing somebody else's endpoint must buy nothing — it is the only identifier the API takes.
        Assert.Single(await fixture.SubscriptionsOf(theirs.User.Id));
    }

    [Fact]
    public async Task UnsubscribingAnUnknownEndpointIsStillNoContent()
    {
        using var expert = await fixture.CreateMappedExpert();

        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/push/subscriptions")
        {
            Content = JsonContent.Create(new { endpoint = PushFlows.NextEndpoint() }),
        };
        using var response = await expert.Client.SendAsync(request);

        // Answering "there was nothing there" would turn this into an oracle for whether any given
        // endpoint is registered to somebody.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Theory]
    // A key that is not the right size is not a key. Stored, it throws out of the sender at send
    // time — and before the loop was hardened, one bad row silenced every other device the expert
    // owned. The same reasoning that makes PushOptionsValidator check the VAPID key exactly.
    [InlineData("PLACEHOLDER-p256dh", PushFlows.Auth, "invalid_p256dh")]
    [InlineData(PushFlows.P256dh, "PLACEHOLDER-auth", "invalid_auth")]
    public async Task AMalformedKeyIsRefusedAtTheDoor(string p256dh, string auth, string expected)
    {
        using var expert = await fixture.CreateMappedExpert();

        using var response = await expert.Client.PostAsJsonAsync(
            "/api/push/subscriptions",
            new { endpoint = PushFlows.NextEndpoint(), p256dh, auth });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expected, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Empty(await fixture.SubscriptionsOf(expert.User.Id));
    }

    [Theory]
    [InlineData("https://attacker.example.invalid/push/1", "endpoint_host_not_allowed")]
    [InlineData("https://localhost:5180/api/health", "endpoint_host_not_allowed")]
    [InlineData("http://fcm.googleapis.com/fcm/send/x", "endpoint_not_https")]
    public async Task AnEndpointTheApiWouldNotWantToCallIsRefused(string endpoint, string expected)
    {
        using var expert = await fixture.CreateMappedExpert();

        // This value is a URL the API will later POST to, supplied by the caller. Unchecked, any
        // authenticated user of any role could aim it at a host reachable only from inside the
        // deployment and read the outcome from the notification log — a blind SSRF out of the service
        // §9.1/#21 expects to survive an InfoSec review.
        using var response = await expert.Client.PostAsJsonAsync(
            "/api/push/subscriptions",
            new { endpoint, p256dh = PushFlows.P256dh, auth = PushFlows.Auth });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expected, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Empty(await fixture.SubscriptionsOf(expert.User.Id));
    }

    [Fact]
    public async Task AUserCannotAccumulateUnboundedDevices()
    {
        using var expert = await fixture.CreateMappedExpert();
        var cap = fixture.Push.CurrentValue.MaxSubscriptionsPerUser;

        for (var i = 0; i < cap; i++)
        {
            using var accepted = await expert.Client.PostAsJsonAsync(
                "/api/push/subscriptions", PushFlows.Subscription());
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        }

        // The sender walks these serially on the assignment-ingestion path, so an unbounded set is an
        // unbounded stall — for every expert, not just this one.
        using var refused = await expert.Client.PostAsJsonAsync(
            "/api/push/subscriptions", PushFlows.Subscription());

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains(
            "too_many_subscriptions", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReregisteringAnExistingBrowserIsNeverRefusedByTheCap()
    {
        using var expert = await fixture.CreateMappedExpert();
        var cap = fixture.Push.CurrentValue.MaxSubscriptionsPerUser;
        var endpoint = PushFlows.NextEndpoint();

        await expert.Client.PostAsJsonAsync("/api/push/subscriptions", PushFlows.Subscription(endpoint));
        for (var i = 0; i < cap - 1; i++)
        {
            using var extra = await expert.Client.PostAsJsonAsync(
                "/api/push/subscriptions", PushFlows.Subscription());
            Assert.Equal(HttpStatusCode.OK, extra.StatusCode);
        }

        // At the cap, but this browser already has a row: refusing it would strand an expert whose
        // keys had just rotated, permanently, with no way back except an administrator.
        using var refreshed = await expert.Client.PostAsJsonAsync(
            "/api/push/subscriptions", PushFlows.Subscription(endpoint, p256dh: PushFlows.OtherP256dh));

        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
    }

    [Fact]
    public async Task TheVapidPublicKeyIsServedToAuthenticatedUsers()
    {
        using var expert = await fixture.CreateMappedExpert();

        var key = await expert.Client.GetFromJsonAsync<VapidKeyDto>("/api/push/vapid-public-key");

        // The placeholder, because Push:Mode is `fake` everywhere — which is itself the assertion
        // that no real key has been committed.
        Assert.NotNull(key);
        Assert.Contains("PLACEHOLDER", key.PublicKey, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheEndpointsRefuseAnAnonymousCaller()
    {
        using var anonymous = fixture.CreateClient();

        using var response = await anonymous.PostAsJsonAsync(
            "/api/push/subscriptions", PushFlows.Subscription());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AnyAuthenticatedRoleMayRegisterADevice()
    {
        // Under ActiveUser rather than the Expert policy: §8 sends pushes to garages, officers and
        // brokers too, and none of them should need a second copy of this endpoint.
        var garage = await fixture.CreateUser(UserRole.Garage, UserStatus.Active);
        using var client = fixture.CreateClient();
        client.WithBearer((await fixture.Login(client, garage.Phone)).AccessToken);

        using var response = await client.PostAsJsonAsync("/api/push/subscriptions", PushFlows.Subscription());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed record VapidKeyDto(string PublicKey);
}
