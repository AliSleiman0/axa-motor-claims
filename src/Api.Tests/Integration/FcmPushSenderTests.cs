using System.Net;
using System.Text.Json;
using Api.Integrations.Push;
using Api.Modules.Notifications;
using Api.Tests.Integrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Api.Tests.Integration;

/// <summary>
/// Real native push over FCM (design.md §8, slice 6.3) — the Android half of what
/// <c>WebPushSenderTests</c> pins for browsers, and deliberately the same list of tests, because two
/// senders behind one interface must not disagree about when they throw.
///
/// **The test this file exists for is <see cref="ABare404DoesNotRevokeAnything"/>.** FCM answers 404
/// both for "this install is gone" and for "no such project", so a sender that revoked on the status
/// alone would, on the first assignment after a mistyped `Push:Fcm:ProjectId`, permanently cut off
/// every handset in the fleet — with no way back, because a handset cannot re-register against an
/// authenticated endpoint on its own.
/// </summary>
[Collection("api")]
public sealed class FcmPushSenderTests(ApiFixture fixture)
{
    private static readonly PushMessage Message =
        new("New claim assigned", "Claim PLACEHOLDER-VISA-0001 has been assigned to you.", "/expert/1");

    [Fact]
    public async Task ADeliveredPushIsLoggedSentAgainstItsDeviceToken()
    {
        using var expert = await fixture.CreateMappedExpert();
        var device = await fixture.AddDeviceToken(expert.User.Id);

        using var handler = new StubHttpMessageHandler();
        handler.Respond(HttpStatusCode.OK, GrantedToken).Respond(HttpStatusCode.OK, "{\"name\":\"projects/x/messages/1\"}");

        await Sender(handler).Send(expert.User.Id, Message, NotificationTemplates.AssignmentReceived, default);

        var row = Assert.Single(await fixture.PushRows(expert.User.Id));
        Assert.Equal(NotificationStatuses.Sent, row.Status);

        // The row id, not the token: `recipient_address` is nvarchar(320) and a token is allowed 512
        // here, so the token would not reliably fit. WebPushSender made the same choice.
        Assert.Equal(device.Id.ToString(), row.RecipientAddress);

        var refreshed = Assert.Single(await fixture.DeviceTokensOf(expert.User.Id));
        Assert.NotNull(refreshed.LastUsedAt);
    }

    [Fact]
    public async Task TheWirePayloadIsWhatTheServiceWorkerContractExpects()
    {
        using var expert = await fixture.CreateMappedExpert();
        var device = await fixture.AddDeviceToken(expert.User.Id);

        using var handler = new StubHttpMessageHandler();
        handler.Respond(HttpStatusCode.OK, GrantedToken).Respond(HttpStatusCode.OK, "{}");

        await Sender(handler).Send(expert.User.Id, Message, NotificationTemplates.AssignmentReceived, default);

        var send = handler.Requests[^1];
        Assert.Equal($"https://fcm.googleapis.com/v1/projects/{ProjectId}/messages:send", send.Uri!.ToString());
        Assert.Equal($"Bearer {AccessToken}", send.Authorization);

        using var body = JsonDocument.Parse(send.Body);
        var message = body.RootElement.GetProperty("message");
        Assert.Equal(device.Token, message.GetProperty("token").GetString());
        Assert.Equal(Message.Title, message.GetProperty("notification").GetProperty("title").GetString());
        Assert.Equal(Message.Body, message.GetProperty("notification").GetProperty("body").GetString());

        // `data.url` is the very field src/Web/public/sw.js reads on notificationclick, so a tap
        // deep-links identically on both channels and §8's four remaining push events need no second
        // contract. Renaming it silently breaks the web half too.
        Assert.Equal(Message.Url, message.GetProperty("data").GetProperty("url").GetString());
        Assert.Equal("high", message.GetProperty("android").GetProperty("priority").GetString());
    }

    [Fact]
    public async Task EveryHandsetGetsItsOwnRow()
    {
        using var expert = await fixture.CreateMappedExpert();
        await fixture.AddDeviceToken(expert.User.Id);
        await fixture.AddDeviceToken(expert.User.Id);

        using var handler = new StubHttpMessageHandler();
        handler.Respond(HttpStatusCode.OK, GrantedToken)
            .Respond(HttpStatusCode.OK, "{}")
            .Respond(HttpStatusCode.OK, "{}");

        await Sender(handler).Send(expert.User.Id, Message, NotificationTemplates.AssignmentReceived, default);

        // One row per handset on purpose: an expert whose second phone stopped receiving is a support
        // question an aggregate row cannot answer.
        Assert.Equal(2, (await fixture.PushRows(expert.User.Id)).Count);
    }

    [Fact]
    public async Task A404CarryingUnregisteredRevokesThatHandsetOnly()
    {
        using var expert = await fixture.CreateMappedExpert();
        var first = await fixture.AddDeviceToken(expert.User.Id);
        var second = await fixture.AddDeviceToken(expert.User.Id);

        using var handler = new StubHttpMessageHandler();
        handler.Respond(HttpStatusCode.OK, GrantedToken)
            .Respond(HttpStatusCode.NotFound, UnregisteredBody)
            .Respond(HttpStatusCode.OK, "{}");

        await Sender(handler).Send(expert.User.Id, Message, NotificationTemplates.AssignmentReceived, default);

        // **Which handset got the 404 is read off the wire, not assumed.** The sender's query carries
        // no ORDER BY — it has no reason to, since every live device is sent to — so naming one of
        // the two rows here would be a test that depends on the order SQL Server happened to return.
        var refused = TokenIn(handler.Requests[1].Body);
        var dead = refused == first.Token ? first : second;
        var live = refused == first.Token ? second : first;

        var rows = await fixture.DeviceTokensOf(expert.User.Id);
        Assert.NotNull(rows.Single(t => t.Id == dead.Id).RevokedAt);

        // The second handset is still attempted and still live — a dead token must not end the loop,
        // which is the failure mode the per-device catch exists to prevent.
        Assert.Null(rows.Single(t => t.Id == live.Id).RevokedAt);
        Assert.NotNull(rows.Single(t => t.Id == live.Id).LastUsedAt);
        Assert.Equal(live.Token, TokenIn(handler.Requests[2].Body));
    }

    /// <summary>The registration token an FCM send body was addressed to.</summary>
    private static string TokenIn(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("message").GetProperty("token").GetString()!;
    }

    [Fact]
    public async Task ABare404DoesNotRevokeAnything()
    {
        using var expert = await fixture.CreateMappedExpert();
        await fixture.AddDeviceToken(expert.User.Id);

        using var handler = new StubHttpMessageHandler();

        // 404 with no UNREGISTERED in it — which is what a mistyped Push:Fcm:ProjectId produces, for
        // *every* device, on the very first assignment. Revoking here would cut off the whole fleet
        // permanently: nothing re-registers a handset, because the registration endpoint is
        // authenticated and the shell would need somebody to sign in and press the button again.
        handler.Respond(HttpStatusCode.OK, GrantedToken)
            .Respond(HttpStatusCode.NotFound, "{\"error\":{\"status\":\"NOT_FOUND\",\"message\":\"Requested entity was not found.\"}}");

        await Assert.ThrowsAsync<PushNotDeliveredException>(() =>
            Sender(handler).Send(expert.User.Id, Message, NotificationTemplates.AssignmentReceived, default));

        Assert.Null(Assert.Single(await fixture.DeviceTokensOf(expert.User.Id)).RevokedAt);
        Assert.Equal(NotificationStatuses.Failed, Assert.Single(await fixture.PushRows(expert.User.Id)).Status);
    }

    [Fact]
    public async Task AnUnparseableErrorBodyDoesNotRevokeEither()
    {
        using var expert = await fixture.CreateMappedExpert();
        await fixture.AddDeviceToken(expert.User.Id);

        using var handler = new StubHttpMessageHandler();

        // An HTML error page from a proxy in front of Google, which a substring match on the body
        // could not classify safely either way. Wrong in the safe direction: keep the handset.
        handler.Respond(HttpStatusCode.OK, GrantedToken)
            .Respond(HttpStatusCode.NotFound, "<html><body>404 UNREGISTERED page not found</body></html>");

        await Assert.ThrowsAsync<PushNotDeliveredException>(() =>
            Sender(handler).Send(expert.User.Id, Message, NotificationTemplates.AssignmentReceived, default));

        Assert.Null(Assert.Single(await fixture.DeviceTokensOf(expert.User.Id)).RevokedAt);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ATemporaryFailureDoesNotRevokeAnything(HttpStatusCode status)
    {
        using var expert = await fixture.CreateMappedExpert();
        await fixture.AddDeviceToken(expert.User.Id);

        using var handler = new StubHttpMessageHandler();
        handler.Respond(HttpStatusCode.OK, GrantedToken).Respond(status, "{}");

        await Assert.ThrowsAsync<PushNotDeliveredException>(() =>
            Sender(handler).Send(expert.User.Id, Message, NotificationTemplates.AssignmentReceived, default));

        // A rate limit is not a dead handset, and revoking on one would silently stop notifying an
        // expert who did nothing wrong.
        Assert.Null(Assert.Single(await fixture.DeviceTokensOf(expert.User.Id)).RevokedAt);
    }

    [Fact]
    public async Task ATimeoutIsRecordedRatherThanEscaping()
    {
        using var expert = await fixture.CreateMappedExpert();
        await fixture.AddDeviceToken(expert.User.Id);
        await fixture.AddDeviceToken(expert.User.Id);

        using var handler = new StubHttpMessageHandler();

        // **Slice 3.3's lesson, third adapter.** HttpClient reports its own timeout as a
        // TaskCanceledException, which derives from OperationCanceledException — so a filter naming
        // TimeoutException never matches and this would throw clean out of the loop, leaving the
        // second handset never attempted and no `failed` row for §8 to show.
        handler.Respond(HttpStatusCode.OK, GrantedToken)
            .Throws(new TaskCanceledException("Simulated HttpClient timeout."))
            .Respond(HttpStatusCode.OK, "{}");

        await Sender(handler).Send(expert.User.Id, Message, NotificationTemplates.AssignmentReceived, default);

        var rows = await fixture.PushRows(expert.User.Id);
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.Status == NotificationStatuses.Failed
            && r.Error!.Contains("TimeoutException", StringComparison.Ordinal));

        // The second handset was still attempted, which is the whole point of catching it here.
        Assert.Contains(rows, r => r.Status == NotificationStatuses.Sent);
    }

    [Fact]
    public async Task PartialSuccessIsSuccess()
    {
        using var expert = await fixture.CreateMappedExpert();
        await fixture.AddDeviceToken(expert.User.Id);
        await fixture.AddDeviceToken(expert.User.Id);

        using var handler = new StubHttpMessageHandler();
        handler.Respond(HttpStatusCode.OK, GrantedToken)
            .Respond(HttpStatusCode.ServiceUnavailable, "{}")
            .Respond(HttpStatusCode.OK, "{}");

        // Somebody was told, so `notified_at` should say so. Only a total failure leaves it null.
        await Sender(handler).Send(expert.User.Id, Message, NotificationTemplates.AssignmentReceived, default);
    }

    [Fact]
    public async Task AUserWithNoHandsetsThrowsTheNoDevicesSignalAndLogsNothingHere()
    {
        using var expert = await fixture.CreateMappedExpert();

        using var handler = new StubHttpMessageHandler();

        var ex = await Assert.ThrowsAsync<PushChannelHasNoDevicesException>(() =>
            Sender(handler).Send(expert.User.Id, Message, NotificationTemplates.AssignmentReceived, default));

        // Nothing reached the wire, so the access token was never even minted.
        Assert.Empty(handler.Requests);
        Assert.Contains("no registered Android device", ex.Message, StringComparison.Ordinal);

        // **No row from this channel.** Most of the fleet — every officer, broker and desk-based
        // expert — has no Android registration at all, so logging `failed` here would report a
        // failure per push for people whose browser popup arrived perfectly. The composite writes
        // exactly one row, and only when every channel came up empty.
        Assert.Empty(await fixture.PushRows(expert.User.Id));
    }

    [Fact]
    public async Task TheAccessTokenIsMintedOnceAndReusedAcrossSends()
    {
        using var expert = await fixture.CreateMappedExpert();
        await fixture.AddDeviceToken(expert.User.Id);

        using var handler = new StubHttpMessageHandler();
        handler.Respond(HttpStatusCode.OK, GrantedToken)
            .Respond(HttpStatusCode.OK, "{}")
            .Respond(HttpStatusCode.OK, "{}");

        var sender = Sender(handler);
        await sender.Send(expert.User.Id, Message, NotificationTemplates.AssignmentReceived, default);
        await sender.Send(expert.User.Id, Message, NotificationTemplates.AssignmentReceived, default);

        // Three requests, not four: the exchange is a network round trip and the token lasts an
        // hour, so minting one per push would put a second remote call on the assignment path.
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(FcmTestServiceAccount.TokenUri, handler.Requests[0].Uri!.ToString());
        Assert.All(handler.Requests.Skip(1), r =>
            Assert.Contains("messages:send", r.Uri!.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task ARefusedTokenExchangeFailsEveryHandsetRatherThanOne()
    {
        using var expert = await fixture.CreateMappedExpert();
        await fixture.AddDeviceToken(expert.User.Id);
        await fixture.AddDeviceToken(expert.User.Id);

        using var handler = new StubHttpMessageHandler();
        handler.Respond(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\"}");

        await Assert.ThrowsAsync<PushNotDeliveredException>(() =>
            Sender(handler).Send(expert.User.Id, Message, NotificationTemplates.AssignmentReceived, default));

        // A credential fault, not a device fault — so every handset gets its own `failed` row rather
        // than one aggregate naming no device, and §8's log can still answer "why was this expert
        // never told". Nothing was attempted on the send endpoint.
        var rows = await fixture.PushRows(expert.User.Id);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(NotificationStatuses.Failed, r.Status));
        Assert.Single(handler.Requests);

        // And no handset is revoked: a revoked key is not a dead phone.
        Assert.All(await fixture.DeviceTokensOf(expert.User.Id), t => Assert.Null(t.RevokedAt));
    }

    [Fact]
    public async Task ARevokedHandsetIsNeverSentToAgain()
    {
        using var expert = await fixture.CreateMappedExpert();
        var device = await fixture.AddDeviceToken(expert.User.Id);
        await fixture.RevokeDevice(device.Id);

        // Nothing is scripted, so any request at all throws out of the stub — which is the assertion.
        using var handler = new StubHttpMessageHandler();

        await Assert.ThrowsAnyAsync<PushNotDeliveredException>(() =>
            Sender(handler).Send(expert.User.Id, Message, NotificationTemplates.AssignmentReceived, default));

        Assert.Empty(handler.Requests);
    }

    private const string ProjectId = "PLACEHOLDER-firebase-project-id";
    private const string AccessToken = "PLACEHOLDER-fcm-access-token";
    private const string GrantedToken =
        "{\"access_token\":\"" + AccessToken + "\",\"expires_in\":3600,\"token_type\":\"Bearer\"}";

    /// <summary>The FCM v1 error shape that means this install is gone (the `details` array).</summary>
    private const string UnregisteredBody =
        "{\"error\":{\"code\":404,\"status\":\"NOT_FOUND\",\"details\":[{"
        + "\"@type\":\"type.googleapis.com/google.firebase.fcm.v1.FcmError\","
        + "\"errorCode\":\"UNREGISTERED\"}]}}";

    private FcmPushSender Sender(StubHttpMessageHandler handler)
    {
        var options = new PushOptions { Mode = PushModes.WebPush };
        options.Fcm.Enabled = true;
        options.Fcm.ProjectId = ProjectId;
        options.Fcm.ServiceAccountJsonPath = FcmTestServiceAccount.Path;

        var factory = new StubHttpClientFactory(handler);

        return new FcmPushSender(
            fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            factory,
            new FcmAccessTokens(
                factory, Options.Create(options), fixture.Time, NullLogger<FcmAccessTokens>.Instance),
            Options.Create(options),
            fixture.Services.GetRequiredService<NotificationLog>(),
            fixture.Time,
            NullLogger<FcmPushSender>.Instance);
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
