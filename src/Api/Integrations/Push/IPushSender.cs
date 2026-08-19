namespace Api.Integrations.Push;

/// <summary>
/// Push notifications (design.md §8) — the BRD's primary trigger: "a popup message will show on the
/// expert mobile". Native APNs/FCM via Capacitor, web push in the browser; both are provisional
/// pending research-capacitor.md.
///
/// Addressed by user id, not device token: token registration is a later slice (§8/3.4), and every
/// push in the design targets a known app user.
/// </summary>
public interface IPushSender
{
    /// <param name="templateName">Names the message template for the `notification` log (§4).</param>
    Task Send(Guid recipientUserId, string title, string body, string templateName, CancellationToken ct);
}
