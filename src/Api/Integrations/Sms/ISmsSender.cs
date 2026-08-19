namespace Api.Integrations.Sms;

/// <summary>
/// SMS: OTP codes, invite links, and the Option 2 customer link if #24a says SMS (design.md §8).
/// The gateway is #7/#40; until it is answered the fake is the only implementation.
/// </summary>
public interface ISmsSender
{
    /// <param name="templateName">Names the message template for the `notification` log (§4).</param>
    /// <param name="recipientUserId">
    /// Null when the send is not tied to a known user — an OTP is requested by phone number, and
    /// the phone may not belong to any account.
    /// </param>
    Task Send(string phone, string message, string templateName, Guid? recipientUserId, CancellationToken ct);
}
