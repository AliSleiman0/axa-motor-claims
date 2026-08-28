using System.Net;
using System.Net.Http.Json;
using Api.Integrations.Push;
using Api.Modules.Audit;
using Api.Modules.Users;

namespace Api.Tests.Integration;

/// <summary>
/// Native device registration for §8's assignment popup on Android (slice 6.3) — the FCM half of
/// what <c>PushSubscriptionTests</c> pins for browsers.
///
/// The load-bearing test is the same one: "one row per handset" is enforced by the unique index on
/// (user_id, token_hash), not by the read that precedes the insert. It matters more here than for
/// browsers, because the shell re-registers on every launch rather than only when somebody presses
/// a button.
/// </summary>
[Collection("api")]
public sealed class DeviceTokenTests(ApiFixture fixture)
{
    [Fact]
    public async Task RegisteringStoresTheHandset()
    {
        using var expert = await fixture.CreateMappedExpert();
        var token = PushFlows.NextDeviceToken();

        using var response = await expert.Client.PostAsJsonAsync(
            "/api/push/device-tokens", PushFlows.DeviceTokenBody(token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var row = Assert.Single(await fixture.DeviceTokensOf(expert.User.Id));
        Assert.Equal(token, row.Token);
        Assert.Equal(DevicePlatforms.Android, row.Platform);
        Assert.Null(row.RevokedAt);
        Assert.Null(row.LastUsedAt);
    }

    [Fact]
    public async Task FourParallelRegistrationsCreateExactlyOneRow()
    {
        using var expert = await fixture.CreateMappedExpert();
        var body = PushFlows.DeviceTokenBody();

        // An app relaunch while a retry is still in flight, or the shell registering on resume —
        // both real, and more frequent than the browser equivalent because nobody presses a button.
        // Every one of these passes the read-then-write check before any of them inserts, so without
        // the unique index this creates four rows and the expert gets four identical popups per
        // assignment. Removing `IsUnique()` from the configuration turns this test red, which is how
        // it was verified — while `RegisteringIsAnUpsertOnTheSameToken` below stays green, which is
        // the asymmetry that matters: the happy path alone proves nothing.
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            expert.Client.PostAsJsonAsync("/api/push/device-tokens", body)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Single(await fixture.DeviceTokensOf(expert.User.Id));

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task RegisteringIsAnUpsertOnTheSameToken()
    {
        using var expert = await fixture.CreateMappedExpert();
        var token = PushFlows.NextDeviceToken();

        using var first = await expert.Client.PostAsJsonAsync(
            "/api/push/device-tokens", PushFlows.DeviceTokenBody(token));
        using var second = await expert.Client.PostAsJsonAsync(
            "/api/push/device-tokens", PushFlows.DeviceTokenBody(token));

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var row = Assert.Single(await fixture.DeviceTokensOf(expert.User.Id));

        // Each body read exactly once: ReadFromJsonAsync consumes the response stream, so a second
        // read of the same response throws ObjectDisposedException rather than returning the value
        // again.
        var firstId = (await first.Content.ReadFromJsonAsync<RegisteredDevice>())!.Id;
        var secondId = (await second.Content.ReadFromJsonAsync<RegisteredDevice>())!.Id;

        // The same row, not a second one — and the id is stable, because `notification` rows address
        // a handset by it and a new id per launch would orphan every historical row.
        Assert.Equal(firstId, secondId);
        Assert.Equal(row.Id, secondId);
    }

    [Fact]
    public async Task ReregisteringUnRevokesTheHandset()
    {
        using var expert = await fixture.CreateMappedExpert();
        var token = PushFlows.NextDeviceToken();

        using var created = await expert.Client.PostAsJsonAsync(
            "/api/push/device-tokens", PushFlows.DeviceTokenBody(token));

        var row = Assert.Single(await fixture.DeviceTokensOf(expert.User.Id));
        await fixture.RevokeDevice(row.Id);

        // FCM revoked it as UNREGISTERED — but the handset has just presented the same token, so it
        // is demonstrably alive again. Without the un-revoke, an expert who cleared app data and
        // re-enabled notifications would register successfully and still never be notified.
        using var again = await expert.Client.PostAsJsonAsync(
            "/api/push/device-tokens", PushFlows.DeviceTokenBody(token));

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var refreshed = Assert.Single(await fixture.DeviceTokensOf(expert.User.Id));
        Assert.Null(refreshed.RevokedAt);
        Assert.Equal(row.CreatedAt, refreshed.CreatedAt);
    }

    [Fact]
    public async Task AnUnknownPlatformIsRefused()
    {
        using var expert = await fixture.CreateMappedExpert();

        // Refused here rather than at SaveChanges, so the check constraint answering the same
        // question is a backstop and not the error path. `ios` specifically, because it is the value
        // somebody will send first: iOS ships as the installed PWA and reaches push_subscription
        // instead (research-capacitor.md §11), and the day that changes this is a migration.
        using var response = await expert.Client.PostAsJsonAsync(
            "/api/push/device-tokens", PushFlows.DeviceTokenBody(platform: "ios"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("platform_not_supported", await Error(response));
        Assert.Empty(await fixture.DeviceTokensOf(expert.User.Id));
    }

    [Fact]
    public async Task AMissingTokenIsRefused()
    {
        using var expert = await fixture.CreateMappedExpert();

        using var response = await expert.Client.PostAsJsonAsync(
            "/api/push/device-tokens", new { token = "", platform = DevicePlatforms.Android });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("token_required", await Error(response));
    }

    [Fact]
    public async Task AnOverLongTokenIsRefusedRatherThanTruncated()
    {
        using var expert = await fixture.CreateMappedExpert();

        // Checked rather than truncated: a shortened token is one FCM does not recognise, and the
        // failure would surface weeks later as "this expert stopped getting popups" instead of here.
        using var response = await expert.Client.PostAsJsonAsync(
            "/api/push/device-tokens",
            PushFlows.DeviceTokenBody(new string('t', DeviceTokenLimits.TokenLength + 1)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("token_too_long", await Error(response));
        Assert.Empty(await fixture.DeviceTokensOf(expert.User.Id));
    }

    /// <summary>
    /// **Behaviour changed in slice 7.2, and this test changed with it.** It read
    /// <c>AUserCannotAccumulateUnboundedHandsets</c> and asserted a <c>400
    /// too_many_device_tokens</c>. The boundedness claim it was written for is kept and asserted
    /// below — what is gone is the refusal, deliberately.
    ///
    /// The argument: a 400 is only useful to something that can act on it. A browser subscription is
    /// created by a person pressing a button on a screen that can show them the refusal; an FCM
    /// registration is fired by the Capacitor shell on every launch with nobody watching. So a
    /// refused handset was an expert whose popups silently never started — §8's primary trigger,
    /// missing — and it never recovered, because the next launch was refused identically and FCM
    /// rotates tokens on its own schedule. <c>PushSubscriptionTests</c> keeps the 400 for browsers,
    /// which is the asymmetry.
    /// </summary>
    [Fact]
    public async Task AtTheCap_ANewHandsetEvictsTheLeastRecentlyUsedOne()
    {
        using var expert = await fixture.CreateMappedExpert();
        var cap = fixture.Push.CurrentValue.MaxDeviceTokensPerUser;

        for (var i = 0; i < cap; i++)
        {
            using var accepted = await expert.Client.PostAsJsonAsync(
                "/api/push/device-tokens", PushFlows.DeviceTokenBody());
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        }

        var before = await fixture.DeviceTokensOf(expert.User.Id);
        var oldest = before[0];

        using var accepted2 = await expert.Client.PostAsJsonAsync(
            "/api/push/device-tokens", PushFlows.DeviceTokenBody());

        Assert.Equal(HttpStatusCode.OK, accepted2.StatusCode);

        var after = await fixture.DeviceTokensOf(expert.User.Id);

        // The property the old test existed for, unchanged: the sender walks live rows serially on
        // the assignment-ingestion path, so an unbounded set is an unbounded stall for every expert.
        Assert.Equal(cap, after.Count(t => t.RevokedAt is null));

        // And the one it did not have: which row went. Least-recently-used on `LastUsedAt ??
        // CreatedAt` — none of these has ever been used, so it is the oldest by creation.
        Assert.NotNull(after.Single(t => t.Id == oldest.Id).RevokedAt);

        var audit = await fixture.AuditRow(AuditActions.DeviceTokenRevoked, oldest.Id);
        Assert.Equal(expert.User.Id, audit.ActorUserId);
        Assert.Equal(AuditEntityKinds.DeviceToken, audit.EntityKind);
        Assert.Contains("evicted", audit.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// The browser side keeps the refusal, and the divergence is the point: a person is looking at
    /// the push panel and can be told. Asserted here beside its twin so the two cannot drift apart
    /// silently — a later "let us make these consistent" has to delete a test that says why.
    /// </summary>
    [Fact]
    public async Task ABrowserSubscriptionAtItsCapIsStillRefused()
    {
        using var expert = await fixture.CreateMappedExpert();
        var cap = fixture.Push.CurrentValue.MaxSubscriptionsPerUser;

        for (var i = 0; i < cap; i++)
        {
            using var accepted = await expert.Client.PostAsJsonAsync(
                "/api/push/subscriptions", PushFlows.Subscription());
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        }

        using var refused = await expert.Client.PostAsJsonAsync(
            "/api/push/subscriptions", PushFlows.Subscription());

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("too_many_subscriptions", await Error(refused));
        Assert.Equal(cap, (await fixture.SubscriptionsOf(expert.User.Id)).Count);
    }

    /// <summary>
    /// §9's trail for the handset-changing-hands event (slice 7.2). The shape is what makes it
    /// answerable from the losing side: the actor is the **new** registrant, the entity is the row
    /// that was taken away, and the detail names who held it.
    /// </summary>
    [Fact]
    public async Task DisplacingAHandsetIsAudited()
    {
        using var first = await fixture.CreateMappedExpert();
        using var second = await fixture.CreateMappedExpert();
        var handset = PushFlows.NextDeviceToken();

        using (var mine = await first.Client.PostAsJsonAsync(
            "/api/push/device-tokens", PushFlows.DeviceTokenBody(handset)))
        using (var theirs = await second.Client.PostAsJsonAsync(
            "/api/push/device-tokens", PushFlows.DeviceTokenBody(handset)))
        {
            Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
            Assert.Equal(HttpStatusCode.OK, theirs.StatusCode);
        }

        var displaced = Assert.Single(await fixture.DeviceTokensOf(first.User.Id));
        var audit = await fixture.AuditRow(AuditActions.DeviceTokenDisplaced, displaced.Id);

        Assert.Equal(second.User.Id, audit.ActorUserId);
        Assert.Equal(AuditEntityKinds.DeviceToken, audit.EntityKind);
        Assert.Contains(first.User.Id.ToString(), audit.Detail, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// **Registration audits fire on creation and revocation only, never on the refresh path.** The
    /// shell re-registers on every launch, so auditing that would bury the events that matter under
    /// one row per app open, per handset, per day. The `Assert.Single` inside
    /// <c>AuditRow</c> is what enforces it.
    /// </summary>
    [Fact]
    public async Task RegisteringIsAuditedOnce_AndReregisteringAddsNothing()
    {
        using var expert = await fixture.CreateMappedExpert();
        var handset = PushFlows.NextDeviceToken();

        for (var i = 0; i < 3; i++)
        {
            using var response = await expert.Client.PostAsJsonAsync(
                "/api/push/device-tokens", PushFlows.DeviceTokenBody(handset));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var row = Assert.Single(await fixture.DeviceTokensOf(expert.User.Id));
        var audit = await fixture.AuditRow(AuditActions.DeviceTokenRegistered, row.Id);

        Assert.Equal(expert.User.Id, audit.ActorUserId);
        Assert.Contains(DevicePlatforms.Android, audit.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// Unregistering: one audit row, and it commits with the delete rather than after it — the route
    /// was an <c>ExecuteDelete</c> until slice 7.2, which runs outside the change tracker and would
    /// have left the record of the deletion in a second transaction naming a row that was already
    /// gone.
    /// </summary>
    [Fact]
    public async Task UnregisteringIsAudited()
    {
        using var expert = await fixture.CreateMappedExpert();
        var handset = PushFlows.NextDeviceToken();

        using (var registered = await expert.Client.PostAsJsonAsync(
            "/api/push/device-tokens", PushFlows.DeviceTokenBody(handset)))
        {
            Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        }

        var row = Assert.Single(await fixture.DeviceTokensOf(expert.User.Id));

        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/push/device-tokens")
        {
            Content = JsonContent.Create(new { token = handset }),
        };
        using var response = await expert.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await fixture.DeviceTokensOf(expert.User.Id));

        var audit = await fixture.AuditRow(AuditActions.DeviceTokenRevoked, row.Id);
        Assert.Contains("user", audit.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReregisteringAnExistingHandsetIsNeverRefusedByTheCap()
    {
        using var expert = await fixture.CreateMappedExpert();
        var cap = fixture.Push.CurrentValue.MaxDeviceTokensPerUser;
        var first = PushFlows.NextDeviceToken();

        using (var initial = await expert.Client.PostAsJsonAsync(
            "/api/push/device-tokens", PushFlows.DeviceTokenBody(first)))
        {
            Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        }

        for (var i = 1; i < cap; i++)
        {
            using var filler = await expert.Client.PostAsJsonAsync(
                "/api/push/device-tokens", PushFlows.DeviceTokenBody());
            Assert.Equal(HttpStatusCode.OK, filler.StatusCode);
        }

        // At the cap, and the shell re-registers on every launch. If the count were taken before the
        // upsert branch, an expert at the limit would find their own phone refused — and FCM rotates
        // tokens, so they could never recover by using it.
        using var again = await expert.Client.PostAsJsonAsync(
            "/api/push/device-tokens", PushFlows.DeviceTokenBody(first));

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(cap, (await fixture.DeviceTokensOf(expert.User.Id)).Count);
    }

    [Fact]
    public async Task RegisteringAHandsetRevokesItForWhoeverHadItBefore()
    {
        using var first = await fixture.CreateMappedExpert();
        using var second = await fixture.CreateMappedExpert();
        var handset = PushFlows.NextDeviceToken();

        using (var mine = await first.Client.PostAsJsonAsync(
            "/api/push/device-tokens", PushFlows.DeviceTokenBody(handset)))
        {
            Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        }

        // The phone is handed to somebody else, who signs in. **This test asserted the opposite
        // first** — that both rows stay live, copied from push_subscription's "two people sharing a
        // browser profile each keep their own row". The db-review showed the precedent does not
        // carry: a browser profile is plausibly one person's, but an FCM token identifies the *app
        // install*, and a field handset is pooled, handed over and re-issued. With both rows live,
        // every claim assigned to the first expert pops up on the second person's screen — visa
        // number in the body, deep link into somebody else's assignment — and it never self-heals,
        // because FCM only reports UNREGISTERED for a token that is dead and this one is alive.
        using (var theirs = await second.Client.PostAsJsonAsync(
            "/api/push/device-tokens", PushFlows.DeviceTokenBody(handset)))
        {
            Assert.Equal(HttpStatusCode.OK, theirs.StatusCode);
        }

        // The handset belongs to whoever signed in last. The displaced row is revoked rather than
        // deleted, so `notification` rows naming it still resolve.
        Assert.NotNull(Assert.Single(await fixture.DeviceTokensOf(first.User.Id)).RevokedAt);
        Assert.Null(Assert.Single(await fixture.DeviceTokensOf(second.User.Id)).RevokedAt);
    }

    [Fact]
    public async Task TheOriginalOwnerGetsTheHandsetBackByRegisteringAgain()
    {
        using var first = await fixture.CreateMappedExpert();
        using var second = await fixture.CreateMappedExpert();
        var handset = PushFlows.NextDeviceToken();

        using (var a = await first.Client.PostAsJsonAsync(
            "/api/push/device-tokens", PushFlows.DeviceTokenBody(handset)))
        using (var b = await second.Client.PostAsJsonAsync(
            "/api/push/device-tokens", PushFlows.DeviceTokenBody(handset)))
        {
            Assert.Equal(HttpStatusCode.OK, a.StatusCode);
            Assert.Equal(HttpStatusCode.OK, b.StatusCode);
        }

        // Taking the handset back is just signing in again — the shell registers on launch, so this
        // happens without anybody being told there is a rule. The row is un-revoked rather than
        // recreated, which is why the id and `created_at` survive a hand-over and back.
        using (var again = await first.Client.PostAsJsonAsync(
            "/api/push/device-tokens", PushFlows.DeviceTokenBody(handset)))
        {
            Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        }

        Assert.Null(Assert.Single(await fixture.DeviceTokensOf(first.User.Id)).RevokedAt);
        Assert.NotNull(Assert.Single(await fixture.DeviceTokensOf(second.User.Id)).RevokedAt);
    }

    [Fact]
    public async Task UnregisteringRemovesOnlyTheCallersRow()
    {
        using var mine = await fixture.CreateMappedExpert();
        using var theirs = await fixture.CreateMappedExpert();
        var token = PushFlows.NextDeviceToken();

        using (var a = await mine.Client.PostAsJsonAsync(
            "/api/push/device-tokens", PushFlows.DeviceTokenBody(token)))
        using (var b = await theirs.Client.PostAsJsonAsync(
            "/api/push/device-tokens", PushFlows.DeviceTokenBody(token)))
        {
            Assert.Equal(HttpStatusCode.OK, a.StatusCode);
            Assert.Equal(HttpStatusCode.OK, b.StatusCode);
        }

        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/push/device-tokens")
        {
            Content = JsonContent.Create(new { token }),
        };
        using var response = await mine.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await fixture.DeviceTokensOf(mine.User.Id));

        // Knowing someone else's token buys nothing — the delete is scoped to the caller.
        Assert.Single(await fixture.DeviceTokensOf(theirs.User.Id));
    }

    [Fact]
    public async Task UnregisteringAnUnknownHandsetIsStillNoContent()
    {
        using var expert = await fixture.CreateMappedExpert();

        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/push/device-tokens")
        {
            Content = JsonContent.Create(new { token = PushFlows.NextDeviceToken() }),
        };
        using var response = await expert.Client.SendAsync(request);

        // Whether a row was there is not the caller's business: answering would make this endpoint
        // report "does this token belong to somebody" for any token presented to it.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task TheEndpointsRefuseAnAnonymousCaller()
    {
        using var client = fixture.CreateClient();

        using var post = await client.PostAsJsonAsync(
            "/api/push/device-tokens", PushFlows.DeviceTokenBody());
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/push/device-tokens")
        {
            Content = JsonContent.Create(new { token = PushFlows.NextDeviceToken() }),
        };
        using var delete = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, delete.StatusCode);
    }

    [Theory]
    [InlineData(UserRole.Garage)]
    [InlineData(UserRole.ClaimOfficer)]
    [InlineData(UserRole.Broker)]
    public async Task AnyAuthenticatedRoleMayRegisterAHandset(UserRole role)
    {
        // ActiveUser rather than the Expert policy, for the reason the subscription routes give:
        // §8 pushes to garages, officers and brokers too, and none of them should need a second
        // copy of this endpoint. Every row is caller-scoped, so the wider policy grants nothing.
        var user = await fixture.CreateUser(role, UserStatus.Active);
        using var client = fixture.CreateClient();
        client.WithBearer((await fixture.Login(client, user.Phone)).AccessToken);

        using var response = await client.PostAsJsonAsync(
            "/api/push/device-tokens", PushFlows.DeviceTokenBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(await fixture.DeviceTokensOf(user.Id));
    }

    private static async Task<string?> Error(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ErrorBody>())?.Error;

    private sealed record ErrorBody(string? Error);

    private sealed record RegisteredDevice(Guid Id);
}
