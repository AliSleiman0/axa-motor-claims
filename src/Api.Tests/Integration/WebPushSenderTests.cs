using System.Net;
using Api.Integrations.Push;
using Api.Modules.Notifications;
using Api.Modules.Users;
using Api.Tests.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Api.Tests.Integration;

/// <summary>
/// The real push sender against a scripted transport (design.md §8, slice 3.4).
///
/// No network: `PushServiceClient` takes an `HttpClient`, so the seam is slice 3.3's
/// <see cref="StubHttpMessageHandler"/>. What is actually being pinned here is the contract with
/// <c>AssignmentHandler</c> — it stamps `notified_at` only if this returns without throwing, so
/// "somebody was told" and "nobody was told" have to be exactly right.
/// </summary>
[Collection("api")]
public sealed class WebPushSenderTests(ApiFixture fixture)
{
    [Fact]
    public async Task ADeliveredPushIsLoggedSentAgainstItsSubscription()
    {
        var user = await fixture.CreateUser(UserRole.Expert, UserStatus.Active);
        var subscription = await fixture.AddSubscription(user.Id);
        var handler = new StubHttpMessageHandler();
        handler.Respond(HttpStatusCode.Created);

        await Sender(handler).Send(user.Id, Message(), Template, CancellationToken.None);

        var row = Assert.Single(await fixture.PushRows(user.Id));
        Assert.Equal(NotificationStatuses.Sent, row.Status);

        // The subscription id, not the endpoint: `recipient_address` is nvarchar(320) and a push
        // endpoint runs longer, and the id joins to the row that holds the endpoint anyway.
        Assert.Equal(subscription.Id.ToString(), row.RecipientAddress);
        Assert.Contains("/expert/", row.Payload!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EverySubscriptionGetsItsOwnRow()
    {
        var user = await fixture.CreateUser(UserRole.Expert, UserStatus.Active);
        var phone = await fixture.AddSubscription(user.Id);
        var desktop = await fixture.AddSubscription(user.Id);

        var handler = new StubHttpMessageHandler();
        handler.Respond(HttpStatusCode.Created);
        handler.Respond(HttpStatusCode.Gone);

        await Sender(handler).Send(user.Id, Message(), Template, CancellationToken.None);

        // One row per device, because "the expert's phone stopped receiving but the desktop still
        // works" is a support question, and an aggregate row cannot answer it.
        //
        // Which device drew which response is not asserted: the send order is whatever the query
        // returns, and it varies between a filtered run and a full one — which is how the first
        // version of this test passed alone and failed in the suite. What matters is that both
        // devices got their own row, and that the two outcomes were recorded separately.
        var rows = await fixture.PushRows(user.Id);
        Assert.Equal(2, rows.Count);
        Assert.Equal(
            new[] { phone.Id.ToString(), desktop.Id.ToString() }.Order(StringComparer.Ordinal),
            rows.Select(r => r.RecipientAddress).Order(StringComparer.Ordinal));
        Assert.Single(rows, r => r.Status == NotificationStatuses.Sent);
        Assert.Single(rows, r => r.Status == NotificationStatuses.Failed && r.Error!.Contains("410", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A410RevokesTheSubscription()
    {
        var user = await fixture.CreateUser(UserRole.Expert, UserStatus.Active);
        var subscription = await fixture.AddSubscription(user.Id);
        var handler = new StubHttpMessageHandler();
        handler.Respond(HttpStatusCode.Gone);

        await Assert.ThrowsAsync<PushNotDeliveredException>(() =>
            Sender(handler).Send(user.Id, Message(), Template, CancellationToken.None));

        // 410 is the push service saying this browser is gone for good. Left live, it would collect
        // one `failed` row per assignment for ever against a device that does not exist.
        var row = Assert.Single(await fixture.SubscriptionsOf(user.Id));
        Assert.Equal(subscription.Id, row.Id);
        Assert.NotNull(row.RevokedAt);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ATemporaryFailureDoesNotRevokeAnything(HttpStatusCode status)
    {
        var user = await fixture.CreateUser(UserRole.Expert, UserStatus.Active);
        await fixture.AddSubscription(user.Id);
        var handler = new StubHttpMessageHandler();
        handler.Respond(status);

        await Assert.ThrowsAsync<PushNotDeliveredException>(() =>
            Sender(handler).Send(user.Id, Message(), Template, CancellationToken.None));

        // A rate limit is not a dead endpoint. Revoking on one would silently stop notifying an
        // expert who did nothing wrong, and nothing would ever un-revoke it but a manual re-subscribe.
        var row = Assert.Single(await fixture.SubscriptionsOf(user.Id));
        Assert.Null(row.RevokedAt);
    }

    [Fact]
    public async Task ATimeoutIsRecordedRatherThanEscaping()
    {
        var user = await fixture.CreateUser(UserRole.Expert, UserStatus.Active);
        await fixture.AddSubscription(user.Id);
        await fixture.AddSubscription(user.Id);

        var handler = new StubHttpMessageHandler();
        // What HttpClient throws when its own Timeout elapses — a TaskCanceledException, which is an
        // OperationCanceledException. Slice 3.3 closed this on the NEXT3 client and the lesson was not
        // carried across; the db-reviewer found it here. Unhandled, it escapes the loop, so the second
        // device is never attempted, no `failed` row is written for §8 to show, and the revocation
        // staged below is discarded — and then it escapes AssignmentHandler's catch too, because that
        // filter also excludes OperationCanceledException.
        handler.Throws(new TaskCanceledException("Simulated HttpClient timeout."));
        handler.Respond(HttpStatusCode.Gone);

        await Assert.ThrowsAsync<PushNotDeliveredException>(() =>
            Sender(handler).Send(user.Id, Message(), Template, CancellationToken.None));

        var rows = await fixture.PushRows(user.Id);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(NotificationStatuses.Failed, r.Status));
        Assert.Contains(rows, r => r.Error!.Contains("Timeout", StringComparison.Ordinal));

        // The second device was still attempted, and its revocation still persisted. Which of the two
        // timed out is not asserted: the send order is whatever the query returns, and pinning it
        // would be pinning an implementation detail instead of the property that matters.
        Assert.Single(await fixture.SubscriptionsOf(user.Id), s => s.RevokedAt is not null);
    }

    [Fact]
    public async Task PartialSuccessIsSuccess()
    {
        var user = await fixture.CreateUser(UserRole.Expert, UserStatus.Active);
        await fixture.AddSubscription(user.Id);
        await fixture.AddSubscription(user.Id);

        var handler = new StubHttpMessageHandler();
        handler.Respond(HttpStatusCode.Gone);
        handler.Respond(HttpStatusCode.Created);

        // Somebody was told, so `notified_at` should say so. Throwing here would leave the honest
        // record saying nobody was notified while a popup was sitting on the expert's desk.
        await Sender(handler).Send(user.Id, Message(), Template, CancellationToken.None);
    }

    [Fact]
    public async Task AUserWithNoSubscriptionsThrowsTheNoDevicesSignalAndLogsNothingHere()
    {
        var user = await fixture.CreateUser(UserRole.Expert, UserStatus.Active);

        // **Changed deliberately in slice 6.3: this asserted a `failed` row, and the row moved to
        // CompositePushSender.** It has not been relaxed — the row is still written for exactly this
        // case, and `CompositePushSenderTests.NoDeviceOnAnyChannelIsOneHonestFailedRow` pins it.
        // What changed is who can honestly write it. Once FCM is a second channel, an officer or a
        // desk-based expert has a browser and no handset, so a `failed` row from whichever channel
        // came up empty would say a notification failed that in fact arrived — in the table §8 uses
        // to answer "was this person told". Only the composite can see that *every* channel was
        // empty. Raised by the db-review of slice 6.3's migration.
        var nobodyHere = await Assert.ThrowsAsync<PushChannelHasNoDevicesException>(() =>
            Sender(new StubHttpMessageHandler()).Send(user.Id, Message(), Template, CancellationToken.None));

        // Still a PushNotDeliveredException, so every existing catch — including
        // AssignmentHandler's, which leaves notified_at null — behaves exactly as before.
        Assert.IsAssignableFrom<PushNotDeliveredException>(nobodyHere);
        Assert.Contains("no active push subscription", nobodyHere.Message, StringComparison.Ordinal);
        Assert.Empty(await fixture.PushRows(user.Id));
    }

    [Fact]
    public async Task ARevokedSubscriptionIsNeverSentToAgain()
    {
        var user = await fixture.CreateUser(UserRole.Expert, UserStatus.Active);
        var subscription = await fixture.AddSubscription(user.Id);
        await fixture.Revoke(subscription.Id);

        var handler = new StubHttpMessageHandler();

        // No response is scripted: the stub throws if anything reaches it, so this asserts that the
        // filtered query really excluded the revoked row rather than merely tolerating it.
        await Assert.ThrowsAnyAsync<PushNotDeliveredException>(() =>
            Sender(handler).Send(user.Id, Message(), Template, CancellationToken.None));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task TheVapidSubjectAndKeysReachTheWire()
    {
        var user = await fixture.CreateUser(UserRole.Expert, UserStatus.Active);
        await fixture.AddSubscription(user.Id);
        var handler = new StubHttpMessageHandler();
        handler.Respond(HttpStatusCode.Created);

        await Sender(handler).Send(user.Id, Message(), Template, CancellationToken.None);

        // A push service rejects an unsigned request, so the absence of this header would be a 401
        // from a real service and nothing at all from a stub.
        var request = Assert.Single(handler.Requests);
        Assert.NotNull(request.Authorization);
        Assert.Contains("vapid", request.Authorization, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheTotalFailureSignalIsOneAssignmentHandlerActuallyCatches()
    {
        // `AssignmentHandler.Notify` catches `Exception ex when (ex is not OperationCanceledException)`
        // and turns it into "notified_at stays null". If this exception ever came to derive from
        // OperationCanceledException — as TaskCanceledException does, which is the trap this slice hit
        // twice — a total delivery failure would sail straight through that filter, out of Handle(),
        // and fault the assignment-ingestion path instead of being recorded honestly.
        Exception nobodyWasTold = new PushNotDeliveredException();

        Assert.False(nobodyWasTold is OperationCanceledException);
    }

    private const string Template = NotificationTemplates.AssignmentReceived;

    private static PushMessage Message() =>
        new("New claim assigned", "Claim PLACEHOLDER-VISA-T has been assigned to you.", "/expert/PLACEHOLDER");

    /// <summary>
    /// A sender wired to the scripted transport. Real VAPID keys, because the library signs with them
    /// and a placeholder would throw before any request was made — generated per run rather than
    /// committed, which is the same rule the application follows.
    /// </summary>
    private WebPushSender Sender(StubHttpMessageHandler handler)
    {
        var options = new PushOptions { Mode = PushModes.WebPush, TimeoutSeconds = 15 };
        options.Vapid.Subject = "mailto:PLACEHOLDER-push@example.invalid";
        options.Vapid.PublicKey = VapidTestKeys.PublicKey;
        options.Vapid.PrivateKey = VapidTestKeys.PrivateKey;

        return new WebPushSender(
            fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            new StubHttpClientFactory(handler),
            Options.Create(options),
            fixture.Services.GetRequiredService<NotificationLog>(),
            fixture.Time,
            NullLogger<WebPushSender>.Instance);
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
