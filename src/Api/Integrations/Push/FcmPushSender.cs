using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Api.Infrastructure;
using Api.Modules.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Integrations.Push;

/// <summary>
/// Real native push to Android handsets over FCM (design.md §8, slice 6.3).
///
/// **This exists because the Capacitor WebView has no <c>PushManager</c> at all** — observed on the
/// Samsung in slice 6.3a, and the reason 6.3 exists. On Android there is no web-push fallback the
/// way there is on iOS, so without this the BRD's primary trigger ("a popup message will show on the
/// expert mobile") simply does not happen on the platform most of the fleet is expected to run.
///
/// It mirrors <see cref="WebPushSender"/> deliberately and almost line for line: the same
/// scope-per-send, the same one-notification-row-per-device, the same one-SaveChanges-per-device,
/// the same timeout discrimination, and the same throw-iff-nothing-was-delivered contract that
/// <c>AssignmentHandler</c> reads as "notified_at stays null". Two senders that answer the same
/// interface differently would make §8's contract depend on which channel a user happened to have.
/// </summary>
public sealed partial class FcmPushSender(
    IServiceScopeFactory scopes,
    IHttpClientFactory httpClients,
    FcmAccessTokens accessTokens,
    IOptions<PushOptions> options,
    NotificationLog notifications,
    TimeProvider time,
    ILogger<FcmPushSender> logger) : IPushSender
{
    /// <summary>
    /// FCM's own host. A real value rather than a placeholder — it is Google's endpoint, not client
    /// data, the same treatment as <c>Push:AllowedEndpointHosts</c> and `Media:ImageContentTypes`.
    /// </summary>
    private const string SendHost = "https://fcm.googleapis.com";

    /// <summary>
    /// The FCM v1 error code that means this install is gone for good: uninstalled, data cleared, or
    /// the token rotated. **The only condition under which a row is revoked.**
    /// </summary>
    private const string Unregistered = "UNREGISTERED";

    private static readonly JsonSerializerOptions Payload = new(JsonSerializerDefaults.Web);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "FCM push to user {UserId}: {Accepted} of {Total} device tokens accepted.")]
    private static partial void LogDelivered(ILogger logger, Guid userId, int accepted, int total);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Device token {DeviceTokenId} revoked: FCM answered 404 UNREGISTERED.")]
    private static partial void LogRevoked(ILogger logger, Guid deviceTokenId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "An FCM push was delivered but its device-token housekeeping could not be saved.")]
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

        // `AsNoTracking`, because housekeeping below is written with `ExecuteUpdateAsync` rather than
        // through the change tracker. See `Stamp` for why that matters.
        var devices = await db.Set<DeviceToken>()
            .AsNoTracking()
            .Where(t => t.UserId == recipientUserId && t.RevokedAt == null)
            .ToListAsync(ct);

        if (devices.Count == 0)
        {
            // No `notification` row here — see WebPushSender's matching branch. Most of the fleet
            // has no Android registration at all (every officer, broker and desk-based expert), so a
            // `failed` row per push from this channel would drown §8's log in failures that did not
            // happen. Only CompositePushSender can see that every channel came up empty, and it
            // writes exactly one row for that.
            throw new PushChannelHasNoDevicesException(
                $"User {recipientUserId} has no registered Android device.");
        }

        string accessToken;
        try
        {
            accessToken = await accessTokens.Get(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // A credential or configuration fault, not a device fault — so every handset gets its own
            // `failed` row rather than one aggregate that names no device. Recorded before throwing
            // for the same reason the no-devices branch is: §8's log has to be able to answer why.
            foreach (var device in devices)
            {
                await Record(device.Id.ToString(), $"{ex.GetType().Name}: {ex.Message}");
            }

            throw new PushNotDeliveredException(
                $"No FCM access token could be minted, so none of the {devices.Count} device "
                + $"token(s) for user {recipientUserId} were attempted.", ex);
        }

        var client = httpClients.CreateClient(FcmHttpClient.Name);
        var url = $"{SendHost}/v1/projects/{options.Value.Fcm.ProjectId}/messages:send";

        var accepted = 0;
        var revoked = 0;

        foreach (var device in devices)
        {
            // One notification row per device, on purpose — an expert whose phone stopped receiving
            // while their browser kept working is a support question an aggregate row cannot answer.
            // The address is the **row id**, not the token: `recipient_address` is nvarchar(320) and
            // a registration token is allowed 512 here, so the token would not reliably fit; the id
            // joins to the row that holds it anyway. Same choice as WebPushSender's, same reason.
            var address = device.Id.ToString();

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = JsonContent.Create(Envelope(device.Token, message), options: Payload),
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                using var response = await client.SendAsync(request, ct);

                if (response.IsSuccessStatusCode)
                {
                    accepted++;
                    await Stamp(db, device.Id, used: time.GetUtcNow().UtcDateTime, revoked: null, ct);
                    await notifications.Sent(
                        NotificationChannels.Push, recipientUserId, address, templateName, payload, ct);
                }
                else
                {
                    var body = await response.Content.ReadAsStringAsync(ct);

                    // **404 alone is not enough to revoke, and that distinction is the whole point.**
                    // FCM answers 404 both for "this install is gone" (`UNREGISTERED`) and for "no
                    // such project" — so a mistyped `Push:Fcm:ProjectId` would, on a naive reading,
                    // revoke every device in the fleet on the first assignment and leave nothing to
                    // recover them: a handset cannot re-register on its own against an
                    // authenticated endpoint. Wrong in the safe direction on purpose.
                    if (response.StatusCode == HttpStatusCode.NotFound && IsUnregistered(body))
                    {
                        await Stamp(db, device.Id, used: null, revoked: time.GetUtcNow().UtcDateTime, ct);
                        revoked++;
                        LogRevoked(logger, device.Id);
                    }

                    // Everything else — a 429, a 5xx, a bare 404 — is FCM having a bad moment or the
                    // configuration being wrong, and the token stays live. A rate limit is not a dead
                    // handset, and revoking on one would silently stop notifying an expert who did
                    // nothing wrong.
                    await Record(address, $"{(int)response.StatusCode} {response.StatusCode}: {body}");
                }
            }
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
            {
                // **Slice 3.3's lesson, third adapter.** HttpClient reports its own timeout as a
                // TaskCanceledException, which derives from OperationCanceledException — so a filter
                // naming TimeoutException never matches, and a timing-out FCM would throw clean out
                // of this loop: no `failed` row for §8 to show, the expert's other handsets never
                // attempted, and the revocations staged so far discarded. `ct` is the discriminator,
                // so a genuine shutdown still propagates from the catch below.
                await Record(address, $"TimeoutException: {ex.Message}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Deliberately everything else rather than a named list, for WebPushSender's reason:
                // one malformed row must not be able to silence every other device this user owns.
                await Record(address, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        LogDelivered(logger, recipientUserId, accepted, devices.Count);

        if (accepted == 0)
        {
            // Partial success is success — somebody was told, and `notified_at` should say so. Only
            // a total failure leaves it null.
            throw new PushNotDeliveredException(
                $"None of the {devices.Count} device token(s) for user {recipientUserId} accepted "
                + $"the message; {revoked} were revoked.");
        }

        Task Record(string address, string error) => notifications.Failed(
            NotificationChannels.Push, recipientUserId, address, templateName, payload, error, ct);
    }

    /// <summary>
    /// Writes one device's housekeeping, scoped to that row and immediately.
    ///
    /// **`ExecuteUpdateAsync` rather than the change tracker, and the db-review is why.** The
    /// obvious shape — mutate the entity, `SaveChanges` per device, `ChangeTracker.Clear()` in the
    /// catch — has a failure mode its own comment understated: clearing the tracker detaches every
    /// device *still to be processed*, so the later writes silently do nothing and throw nothing.
    /// One transient fault on the first handset would then mean the rest are never revoked, each
    /// costing a wasted request and a `failed` row on every future assignment, with no error
    /// anywhere. Row-scoped statements have no shared state to lose, so the coupling is gone rather
    /// than handled. (<c>WebPushSender</c> carried the same bug and is fixed the same way — a lesson
    /// learned in one adapter is not learned until it is checked in the others.)
    ///
    /// Failures are logged and dropped, never rethrown: the push already happened and its
    /// `notification` row is already committed by NotificationLog's own transaction, so losing
    /// `last_used_at` is cosmetic and losing a revocation costs one `failed` row per assignment
    /// until the next send retries it. Either is far cheaper than turning a delivered notification
    /// into an undelivered one.
    /// </summary>
    private async Task Stamp(
        AppDbContext db, Guid deviceId, DateTime? used, DateTime? revoked, CancellationToken ct)
    {
        try
        {
            var rows = db.Set<DeviceToken>().Where(t => t.Id == deviceId);

            if (used is not null)
            {
                await rows.ExecuteUpdateAsync(s => s.SetProperty(t => t.LastUsedAt, used), ct);
            }

            if (revoked is not null)
            {
                await rows.ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, revoked), ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Deliberately not a named list: the provider surfaces a connection fault as its own
            // exception type rather than a DbUpdateException, and a filter that missed it would
            // undo the whole point of this method.
            LogHousekeepingFailed(logger, ex);
        }
    }

    /// <summary>
    /// Whether an FCM error body names <see cref="Unregistered"/> in its structured detail.
    ///
    /// Parsed rather than substring-matched, and a body that will not parse is treated as **not**
    /// unregistered: this method's answer is the difference between keeping a handset and cutting it
    /// off permanently, so the failure mode has to be "kept a dead token" (one wasted request per
    /// assignment) rather than "revoked a live one" (an expert who stops being told about claims and
    /// no way back).
    /// </summary>
    private static bool IsUnregistered(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("error", out var error)
                || !error.TryGetProperty("details", out var details)
                || details.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var detail in details.EnumerateArray())
            {
                if (detail.ValueKind == JsonValueKind.Object
                    && detail.TryGetProperty("errorCode", out var code)
                    && code.ValueKind == JsonValueKind.String
                    && string.Equals(code.GetString(), Unregistered, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }
        catch (JsonException)
        {
            // An HTML error page from a proxy, or a truncated body. Not a reason to revoke.
            return false;
        }

        return false;
    }

    /// <summary>
    /// The FCM v1 request body.
    ///
    /// <c>data.url</c> carries the very field <c>src/Web/public/sw.js</c> reads on
    /// `notificationclick`, so a tap deep-links the same way on both channels and §8's four
    /// still-to-come push events need no second contract. `android.priority = high` because these
    /// are user-visible popups an expert is waiting on at a crash site, not background sync.
    /// </summary>
    private static FcmEnvelope Envelope(string token, PushMessage message) =>
        new(new FcmSendMessage(
            token,
            new FcmNotification(message.Title, message.Body),
            new Dictionary<string, string>(StringComparer.Ordinal) { ["url"] = message.Url },
            new FcmAndroidConfig("high")));

    private sealed record FcmEnvelope(
        [property: JsonPropertyName("message")] FcmSendMessage Message);

    private sealed record FcmSendMessage(
        [property: JsonPropertyName("token")] string Token,
        [property: JsonPropertyName("notification")] FcmNotification Notification,
        [property: JsonPropertyName("data")] IReadOnlyDictionary<string, string> Data,
        [property: JsonPropertyName("android")] FcmAndroidConfig Android);

    private sealed record FcmNotification(
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("body")] string Body);

    private sealed record FcmAndroidConfig(
        [property: JsonPropertyName("priority")] string Priority);
}
