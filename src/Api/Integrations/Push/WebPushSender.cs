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

        var subscriptions = await db.Set<PushSubscription>()
            .Where(s => s.UserId == recipientUserId && s.RevokedAt == null)
            .ToListAsync(ct);

        if (subscriptions.Count == 0)
        {
            // Not a technical failure, and still the thing the handler needs to hear about: this user
            // has no device that could have received it. One row, so the answer to "why did this
            // expert never get the popup" is in the same table as every other send.
            await notifications.Failed(
                NotificationChannels.Push, recipientUserId, recipientUserId.ToString(), templateName,
                payload, "The user has no active push subscription.", ct);

            throw new PushNotDeliveredException(
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
                subscription.LastUsedAt = time.GetUtcNow().UtcDateTime;
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
                    subscription.RevokedAt = time.GetUtcNow().UtcDateTime;
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

            // **One SaveChanges per subscription, not one per batch** — §6.3's lesson, relocated. A
            // transient failure persisting one device's housekeeping must not discard another
            // device's revocation, and must never convert a push that was delivered into one that
            // was not. Housekeeping is exactly that: it is logged and dropped, never rethrown.
            await Persist(db, ct);
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

    private async Task Persist(AppDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // The push itself already happened, and its `notification` row is already committed by
            // NotificationLog's own transaction. Losing `last_used_at` is cosmetic; losing a
            // revocation costs one `failed` row per assignment until the next send retries it. Either
            // is far cheaper than turning a delivered notification into an undelivered one.
            LogHousekeepingFailed(logger, ex);
            db.ChangeTracker.Clear();
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
public sealed class PushNotDeliveredException : Exception
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

/// <summary>The named <c>HttpClient</c> web push sends through — the second, after `next3`.</summary>
public static class PushHttpClient
{
    public const string Name = "webpush";
}
