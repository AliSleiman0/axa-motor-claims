namespace Api.Integrations.Email;

/// <summary>
/// Email (design.md §8). The broker module's terminal act: a request routed to the AXA recipient for
/// its insurance type. Recipients (#13) and the type list (#14) are placeholder config — never
/// invented here. Also the fallback channel for officer notifications (§8).
/// </summary>
public interface IEmailSender
{
    /// <param name="templateName">Names the message template for the `notification` log (§4).</param>
    /// <param name="recipientUserId">Null when the recipient is an AXA mailbox, not an app user.</param>
    Task Send(
        string recipient,
        string subject,
        string body,
        string templateName,
        Guid? recipientUserId,
        CancellationToken ct);
}
