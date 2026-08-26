using System.Net;
using Api.Infrastructure;
using Api.Modules.Notifications;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using LibPushMessage = Lib.Net.Http.WebPush.PushMessage;
using LibPushSubscription = Lib.Net.Http.WebPush.PushSubscription;

namespace Api.Integrations.Push;

/// <summary>
/// Real web push (design.md §8), the BRD's primary trigger made actual: "a popup message will show on
/// the expert mobile". Selected by `Push:Mode = webpush`; the fake stays the default everywhere.
///
/// Sends to **every** live subscription the user has, because an expert may have the app open on a
/// phone and a desk machine and the BRD does not say which one to guess.
///
/// **The contract with <c>AssignmentHandler</c> is the delicate part.** That handler stamps
/// `notified_at` only if this method returns without throwing, and swallows the exception otherwise
/// (leaving the honest record: nobody was told). So this sender must throw when nothing was
/// delivered — including the case where the user has no subscriptions at all, which is not an error
/// in any technical sense but is exactly "nobody was told".
///
/// Singleton, like every sender, so it holds an <see cref="IServiceScopeFactory"/> rather than an
/// <c>AppDbContext</c>: the context is scoped and is not thread-safe, and this is called from the
/// assignment path and (later) from four other §8 events. Same shape as <c>NotificationLog</c>.
/// </summary>
public sealed partial class WebPushSender(
    IServiceScopeFactory scopes,
    IHttpClientFactory httpClients,
    IOptions<PushOptions> options,
    NotificationLog notifications,
    TimeProvider time,
    ILogger<WebPushSender> logger) : IPushSender
{
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Web push to user {UserId}: {Accepted} of {Total} subscriptions accepted.")]
    private static partial void LogDelivered(ILogger logger, Guid userId, int accepted, int total);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Push subscription {SubscriptionId} revoked: the push service answered {Status}.")]
    private static partial void LogRevoked(ILogger logger, Guid subscriptionId, int status);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A push was delivered but its subscription housekeeping could not be saved.")]
    private static partial void LogHousekeepingFailed(ILogger logger, Exception exception);

    public async Task Send(
        Guid recipientUserId,
        PushMessage message,
        string templateName,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);

        var payload = message.ToJson();

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // `AsNoTracking` since slice 6.3: housekeeping is written with `ExecuteUpdateAsync` rather
        // than through the change tracker. See `Stamp` for why that matters.
        var subscriptions = await db.Set<PushSubscription>()
            .AsNoTracking()
            .Where(s => s.UserId == recipientUserId && s.RevokedAt == null)
            .ToListAsync(ct);

        if (subscriptions.Count == 0)
        {
            // Not a technical failure, and still the thing the handler needs to hear about: this user
            // has no browser that could have received it.
            //
            // **The `notification` row for this case moved up to CompositePushSender in slice 6.3,
            // and the reason is that this channel can no longer see whether it is the only one.** An
            // officer or a desk-based expert has a browser and no handset; once FCM is a second
            // channel, a sender that logged `failed` here would write one `sent` row and one `failed`
            // row for the same notification, and §8's table — the record of whether a person was
            // told, and the basis of every "why did this expert never get the popup" answer — would
            // report a failure that did not happen, for the whole non-Android fleet. Only the
            // composite knows that *every* channel came up empty. Raised by the db-review.
            throw new PushChannelHasNoDevicesException(
                $"User {recipientUserId} has no active push subscription.");
        }

        var client = new PushServiceClient(httpClients.CreateClient(PushHttpClient.Name))
        {
            DefaultAuthentication = Vapid(),
        };

        var accepted = 0;
        var revoked = 0;

        foreach (var subscription in subscriptions)
        {
            // One notification row per subscription, on purpose: an expert with two browsers whose
            // phone stopped receiving is a support question, and an aggregate row cannot answer it.
            // The address is the subscription id rather than the endpoint — `recipient_address` is
            // nvarchar(320) and push endpoints run longer, and the id joins to the row that holds the
            // endpoint anyway.
            var address = subscription.Id.ToString();

            try
            {
                await client.RequestPushMessageDeliveryAsync(Describe(subscription), new LibPushMessage(payload), ct);

                accepted++;
                await Stamp(db, subscription.Id, used: time.GetUtcNow().UtcDateTime, revoked: null, ct);
                await notifications.Sent(
                    NotificationChannels.Push, recipientUserId, address, templateName, payload, ct);
            }
            catch (PushServiceClientException ex)
            {
                if (IsGone(ex.StatusCode))
                {
                    // 404/410 is the push service saying this browser is gone for good — the user
                    // cleared site data, uninstalled, or the subscription expired. Retrying it for
                    // ever would be a slow leak of failed rows against a device that no longer exists.
                    await Stamp(db, subscription.Id, used: null, revoked: time.GetUtcNow().UtcDateTime, ct);
                    revoked++;
                    LogRevoked(logger, subscription.Id, (int)ex.StatusCode);
                }

                // Everything else — 429, a 5xx, a bad gateway — is the push service having a bad
                // moment, and the subscription stays live. A rate limit is not a dead endpoint, and
                // revoking on one would silently stop notifying an expert who did nothing wrong.
                await Record(address, $"{(int)ex.StatusCode} {ex.StatusCode}: {ex.Message}");
            }
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
            {
                // **The same trap slice 3.3 closed on the NEXT3 client, found here by the db-reviewer
                // because the lesson was not carried across.** HttpClient reports its own timeout as a
                // TaskCanceledException, which derives from OperationCanceledException — so a filter
                // naming TimeoutException never matches, and a timing-out push service would throw
                // clean out of this loop: no `failed` row for §8 to show, the expert's other devices
                // never attempted, and the revocations staged so far discarded. `ct` is the
                // discriminator, so a genuine shutdown still propagates from the catch below.
                await Record(address, $"TimeoutException: {ex.Message}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Deliberately everything else, not a named list. A malformed key stored before this
                // slice tightened its validation throws a FormatException from SetKey, and a filter
                // that did not catch it would let one bad row silence every other device this user
                // owns — the failure mode that made the timeout above so damaging.
                await Record(address, $"{ex.GetType().Name}: {ex.Message}");
            }

        }

        LogDelivered(logger, recipientUserId, accepted, subscriptions.Count);

        if (accepted == 0)
        {
            // Partial success is success — somebody was told, and `notified_at` should say so. Only a
            // total failure leaves it null.
            throw new PushNotDeliveredException(
                $"None of the {subscriptions.Count} push subscription(s) for user {recipientUserId} "
                + $"accepted the message; {revoked} were revoked.");
        }

        Task Record(string address, string error) => notifications.Failed(
            NotificationChannels.Push, recipientUserId, address, templateName, payload, error, ct);
    }

    /// <summary>
    /// Writes one subscription's housekeeping, scoped to that row and immediately.
    ///
    /// **Changed in slice 6.3, and it was a real bug rather than a tidy-up.** This used to mutate the
    /// entity and `SaveChanges` per subscription, with `ChangeTracker.Clear()` in the catch — and
    /// clearing the tracker detaches every subscription *still to be processed* in the loop, so their
    /// later writes silently did nothing and threw nothing. The comment claimed the cost was one
    /// row's housekeeping; it was actually all the subsequent rows', so one transient fault on the
    /// first browser meant the rest were never revoked, each costing a wasted request and a `failed`
    /// row on every future assignment, with no error anywhere. Found by the db-review of slice 6.3's
    /// `device_token` migration, which carried the same shape — a lesson learned in one adapter is
    /// not learned until it is checked in the others.
    ///
    /// Row-scoped statements have no shared state to lose, so the coupling is gone rather than
    /// handled. Failures are still logged and dropped, never rethrown: the push already happened and
    /// its `notification` row is already committed by NotificationLog's own transaction, so losing
    /// `last_used_at` is cosmetic and losing a revocation costs one `failed` row per assignment until
    /// the next send retries it. Either is far cheaper than turning a delivered notification into an
    /// undelivered one.
    /// </summary>
    private async Task Stamp(
        AppDbContext db, Guid subscriptionId, DateTime? used, DateTime? revoked, CancellationToken ct)
    {
        try
        {
            var rows = db.Set<PushSubscription>().Where(s => s.Id == subscriptionId);

            if (used is not null)
            {
                await rows.ExecuteUpdateAsync(s => s.SetProperty(x => x.LastUsedAt, used), ct);
            }

            if (revoked is not null)
            {
                await rows.ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, revoked), ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Deliberately not a named list: the provider surfaces a connection fault as its own
            // exception type rather than a DbUpdateException, and a filter that missed it would undo
            // the whole point of this method.
            LogHousekeepingFailed(logger, ex);
        }
    }

    /// <summary>404 and 410 both mean "this endpoint is gone", and both are terminal (RFC 8030).</summary>
    private static bool IsGone(HttpStatusCode status) =>
        status is HttpStatusCode.NotFound or HttpStatusCode.Gone;

    private static LibPushSubscription Describe(PushSubscription subscription)
    {
        var described = new LibPushSubscription { Endpoint = subscription.Endpoint };
        described.SetKey(PushEncryptionKeyName.P256DH, subscription.P256dh);
        described.SetKey(PushEncryptionKeyName.Auth, subscription.Auth);
        return described;
    }

    private VapidAuthentication Vapid()
    {
        var vapid = options.Value.Vapid;
        return new VapidAuthentication(vapid.PublicKey, vapid.PrivateKey) { Subject = vapid.Subject };
    }
}

/// <summary>
/// Nothing received the push.
///
/// It exists so the failure is nameable rather than an <c>InvalidOperationException</c> among others:
/// <c>AssignmentHandler</c> turns it into "notified_at stays null", and A2-style support questions
/// start from the `notification` rows this sender wrote just before throwing it.
/// </summary>
public class PushNotDeliveredException : Exception
{
    public PushNotDeliveredException()
        : base("The push was not delivered to any subscription.")
    {
    }

    public PushNotDeliveredException(string message)
        : base(message)
    {
    }

    public PushNotDeliveredException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// One channel had no device to try at all — no browser subscription, or no Android registration
/// (slice 6.3).
///
/// **It derives from <see cref="PushNotDeliveredException"/> so every existing catch still works**,
/// and it exists so that <c>CompositePushSender</c> can tell "this user owns no phone" apart from
/// "the push service refused". The distinction is not academic: the first is normal for most of the
/// fleet and must not produce a `failed` row when another channel delivered, while the second is a
/// real failure whose row the channel writes for itself before throwing.
///
/// A channel therefore writes **no** `notification` row for this case. The composite writes exactly
/// one, and only when every channel raised it.
/// </summary>
public sealed class PushChannelHasNoDevicesException : PushNotDeliveredException
{
    public PushChannelHasNoDevicesException()
        : base("The user has no device registered on this channel.")
    {
    }

    public PushChannelHasNoDevicesException(string message)
        : base(message)
    {
    }

    public PushChannelHasNoDevicesException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The named <c>HttpClient</c> web push sends through — the second, after `next3`.</summary>
public static class PushHttpClient
{
    public const string Name = "webpush";
}
