using Api.Integrations.Push;
using Api.Modules.Notifications;
using Api.Modules.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Api.Tests.Integration;

/// <summary>
/// The composite that makes web push and FCM additive rather than alternative (design.md §8, slice
/// 6.3).
///
/// **Every test here is really a test of §8's `notified_at` contract.**
/// <c>AssignmentHandler.Notify</c> stamps that column if and only if <c>Send</c> returns, so what
/// this class throws on decides whether the database says an expert was told. Two rules have to hold
/// at once and they pull in opposite directions: a single delivery anywhere is success, and a total
/// failure everywhere must still be reported.
///
/// Hand-built channels rather than real senders — the channels' own behaviour is pinned by
/// <c>WebPushSenderTests</c> and <c>FcmPushSenderTests</c>, and what is under test here is only how
/// their outcomes combine. The fixture is needed for one thing: the real <c>NotificationLog</c>, so
/// the one §8 row this class writes is asserted against the real table rather than a double.
/// </summary>
[Collection("api")]
public sealed class CompositePushSenderTests(ApiFixture fixture)
{
    private static readonly PushMessage Message =
        new("New claim assigned", "Claim PLACEHOLDER-VISA-0001 has been assigned to you.", "/expert/1");

    [Fact]
    public async Task TheBrowserAcceptingIsEnough()
    {
        var browser = new RecordingChannel();
        var handsets = new RecordingChannel(new PushNotDeliveredException("no devices"));

        await Composite(browser, handsets).Send(Guid.NewGuid(), Message, Template, default);

        // The expert has no phone registered yet and read the popup on their desk machine. Somebody
        // was told, so `notified_at` should say so.
        Assert.Equal(1, browser.Calls);
        Assert.Equal(1, handsets.Calls);
    }

    [Fact]
    public async Task TheHandsetAcceptingIsEnough()
    {
        var browser = new RecordingChannel(new PushNotDeliveredException("no subscriptions"));
        var handsets = new RecordingChannel();

        // The reverse, and the more common one in the field: an expert at a crash site with the
        // Android app and no browser session anywhere. Before this slice that case had no channel
        // at all, because the Capacitor WebView exposes no PushManager.
        await Composite(browser, handsets).Send(Guid.NewGuid(), Message, Template, default);

        Assert.Equal(1, handsets.Calls);
    }

    [Fact]
    public async Task ItThrowsOnlyWhenEveryChannelDeliveredNothing()
    {
        var browser = new RecordingChannel(new PushNotDeliveredException("no subscriptions"));
        var handsets = new RecordingChannel(new PushNotDeliveredException("no devices"));

        var ex = await Assert.ThrowsAsync<PushNotDeliveredException>(() =>
            Composite(browser, handsets).Send(Guid.NewGuid(), Message, Template, default));

        // The honest record: nobody was told. Both channels' own `notification` rows are already
        // written by the time this throws, so §8's log explains *why* without anything being
        // recorded here.
        Assert.Contains("2 push channel(s)", ex.Message, StringComparison.Ordinal);
        Assert.Equal(1, browser.Calls);
        Assert.Equal(1, handsets.Calls);
    }

    [Fact]
    public async Task AChannelsUnexpectedFailureDoesNotSilenceTheOther()
    {
        // Not a PushNotDeliveredException — a configuration fault, a missing secret file, a bug.
        // If the loop let this out, a misconfigured FCM would stop web push being *attempted at
        // all*, which is a strict regression on what shipped in slice 3.4: browsers worked then and
        // must not stop working because a second channel was added beside them.
        var handsets = new RecordingChannel(new InvalidOperationException("the service account is missing"));
        var browser = new RecordingChannel();

        await Composite(handsets, browser).Send(Guid.NewGuid(), Message, Template, default);

        Assert.Equal(1, browser.Calls);
    }

    [Fact]
    public async Task AChannelTimeoutIsAFailureRatherThanAnEscape()
    {
        // HttpClient reports its own timeout as a TaskCanceledException with the token unset. Both
        // senders already convert it per device, but if one ever let it out, a filter naming
        // OperationCanceledException would swallow it here as a shutdown — so the second channel
        // must still run and a total failure must still be reported.
        var handsets = new RecordingChannel(new TaskCanceledException("Simulated HttpClient timeout."));
        var browser = new RecordingChannel();

        await Composite(handsets, browser).Send(Guid.NewGuid(), Message, Template, default);

        Assert.Equal(1, browser.Calls);
    }

    [Fact]
    public async Task ARealShutdownPropagatesRatherThanBeingRecordedAsNobodyWasTold()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var handsets = new RecordingChannel(new OperationCanceledException());
        var browser = new RecordingChannel();

        // The container is being torn down. Turning that into `PushNotDeliveredException` would
        // write a conclusion nothing established — and would leave `notified_at` null for an
        // assignment nobody had actually failed to deliver. The second channel is never reached.
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            Composite(handsets, browser).Send(Guid.NewGuid(), Message, Template, cancellation.Token));

        Assert.Equal(0, browser.Calls);
    }

    [Fact]
    public async Task WithOneChannelItBehavesExactlyAsThatChannelDoes()
    {
        // `Push:Fcm:Enabled` is false in every environment, so this is the shape that actually ships
        // today: one channel, and therefore behaviour identical to slice 3.4's. That is what makes
        // turning FCM on a configuration change rather than a deployment risk.
        var browser = new RecordingChannel();
        await Composite(browser).Send(Guid.NewGuid(), Message, Template, default);
        Assert.Equal(1, browser.Calls);

        var failing = new RecordingChannel(new PushNotDeliveredException("no subscriptions"));
        await Assert.ThrowsAsync<PushNotDeliveredException>(() =>
            Composite(failing).Send(Guid.NewGuid(), Message, Template, default));
    }

    [Fact]
    public async Task EveryChannelIsAttemptedEvenAfterOneSucceeds()
    {
        var browser = new RecordingChannel();
        var handsets = new RecordingChannel();

        await Composite(browser, handsets).Send(Guid.NewGuid(), Message, Template, default);

        // No short-circuit on first success, which is the whole point of the class: an expert with a
        // phone *and* a desk browser should get the popup on both, exactly as slice 3.4 refused to
        // choose between two browsers. Deleting the second call here is the plant that proves it.
        Assert.Equal(1, browser.Calls);
        Assert.Equal(1, handsets.Calls);
    }

    [Fact]
    public async Task NoDeviceOnAnyChannelIsOneHonestFailedRow()
    {
        var user = await fixture.CreateUser(UserRole.Expert, UserStatus.Active);
        var browser = new RecordingChannel(new PushChannelHasNoDevicesException("no subscriptions"));
        var handsets = new RecordingChannel(new PushChannelHasNoDevicesException("no devices"));

        await Assert.ThrowsAsync<PushNotDeliveredException>(() =>
            Composite(browser, handsets).Send(user.Id, Message, Template, default));

        // **This is where the row each channel used to write now lives** (slice 6.3, raised by the
        // db-review). One row, not two: the user owns no device anywhere, which is a single fact
        // about the person rather than one per channel. Addressed to the user, since there is no
        // device to name.
        var row = Assert.Single(await fixture.PushRows(user.Id));
        Assert.Equal(NotificationStatuses.Failed, row.Status);
        Assert.Equal(user.Id.ToString(), row.RecipientAddress);
        Assert.Contains("no registered device", row.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AChannelWithDevicesThatAllFailedWritesNoExtraRowHere()
    {
        var user = await fixture.CreateUser(UserRole.Expert, UserStatus.Active);

        // One channel has no device; the other has devices and every one of them was refused. That
        // second channel has already written a `failed` row per device before throwing, so a row
        // here would double-count the same failure — and §8's log is what a support question counts.
        var browser = new RecordingChannel(new PushNotDeliveredException("every subscription refused"));
        var handsets = new RecordingChannel(new PushChannelHasNoDevicesException("no devices"));

        await Assert.ThrowsAsync<PushNotDeliveredException>(() =>
            Composite(browser, handsets).Send(user.Id, Message, Template, default));

        Assert.Empty(await fixture.PushRows(user.Id));
    }

    [Fact]
    public async Task ADeliveredPushNeverWritesTheNoDevicesRow()
    {
        var user = await fixture.CreateUser(UserRole.Expert, UserStatus.Active);

        // The case the whole change exists for: an officer with a browser and no handset. Before the
        // fix this wrote one `failed` row on every single push, for most of the fleet, into the
        // table §8 uses to answer whether a person was told.
        var browser = new RecordingChannel();
        var handsets = new RecordingChannel(new PushChannelHasNoDevicesException("no devices"));

        await Composite(browser, handsets).Send(user.Id, Message, Template, default);

        Assert.Empty(await fixture.PushRows(user.Id));
    }

    private const string Template = NotificationTemplates.AssignmentReceived;

    private CompositePushSender Composite(params IPushSender[] channels) =>
        new(
            channels,
            fixture.Services.GetRequiredService<NotificationLog>(),
            NullLogger<CompositePushSender>.Instance);

    /// <summary>A channel that counts its calls and optionally fails the way a real one would.</summary>
    private sealed class RecordingChannel(Exception? failure = null) : IPushSender
    {
        public int Calls { get; private set; }

        public Task Send(Guid recipientUserId, PushMessage message, string templateName, CancellationToken ct)
        {
            Calls++;
            return failure is null ? Task.CompletedTask : Task.FromException(failure);
        }
    }
}
