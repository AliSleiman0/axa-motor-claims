using System.Text.Json;
using System.Text.Json.Serialization;

namespace Api.Integrations.Push;

/// <summary>
/// Push notifications (design.md §8) — the BRD's primary trigger: "a popup message will show on the
/// expert mobile". Web push in the browser and PWA (slice 3.4); native APNs/FCM via Capacitor is
/// slice 6.3 and stays provisional pending research-capacitor.md.
///
/// Addressed by user id, not by device: which devices a user has is
/// <c>push_subscription</c>'s business, and every push in the design targets a known app user who
/// may be reading on a phone and a desktop at once.
/// </summary>
public interface IPushSender
{
    /// <param name="templateName">Names the message template for the `notification` log (§4).</param>
    Task Send(Guid recipientUserId, PushMessage message, string templateName, CancellationToken ct);
}

/// <summary>
/// What arrives on the device. This **is** the JSON the service worker reads
/// (<c>src/Web/public/sw.js</c>), so the property names are a contract with it rather than an
/// internal detail.
///
/// A record rather than three positional strings on <see cref="IPushSender.Send"/> (changed slice
/// 3.4, which needed to add <see cref="Url"/>): design.md §8 lists four more push events still to
/// come — declaration submitted, approved, rejected, Option 2 ready to send — and every one of them
/// wants to land the user on a specific screen. A notification you have to go and find the subject
/// of is most of the way to no notification.
/// </summary>
/// <param name="Url">
/// Where clicking it takes the user, as an app-relative path (`/expert/{assignmentId}`). Relative
/// rather than absolute because the same payload is correct on localhost, on the test environment
/// and in production (§10's two environments), and the service worker resolves it against its own
/// origin — a push cannot navigate anywhere else anyway.
/// </param>
public sealed record PushMessage(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("url")] string Url)
{
    private static readonly JsonSerializerOptions Format = new(JsonSerializerDefaults.Web);

    /// <summary>The payload as the service worker will receive it.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Format);
}
