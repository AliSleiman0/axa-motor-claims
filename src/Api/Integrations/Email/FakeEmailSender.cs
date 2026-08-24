using System.Globalization;
using Api.Modules.Notifications;

namespace Api.Integrations.Email;

/// <summary>Dev/demo sender: the email lands in the console and the `notification` log, nowhere else.</summary>
public sealed partial class FakeEmailSender(
    ILogger<FakeEmailSender> logger,
    NotificationLog notifications,
    FakeBehavior behavior) : IEmailSender
{
    [LoggerMessage(Level = LogLevel.Information, Message = "FAKE EMAIL to {Recipient} [{Subject}]: {Body}")]
    private static partial void LogEmail(ILogger logger, string recipient, string subject, string body);

    [LoggerMessage(Level = LogLevel.Information, Message = "FAKE EMAIL attachment: {FileName} ({ContentType}, {SizeBytes} bytes)")]
    private static partial void LogAttachment(
        ILogger logger, string fileName, string contentType, long sizeBytes);

    public async Task Send(
        string recipient,
        string subject,
        string body,
        string templateName,
        Guid? recipientUserId,
        IReadOnlyList<EmailAttachment> attachments,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(attachments);

        await behavior.Delay(ct);

        // Names and sizes only, on the log line and in the notification payload alike. **Never the
        // bytes** — the payload column is §4's record of what was sent, not a second copy of a
        // customer's identity documents sitting in a table nothing sweeps. Same discipline §4 applies
        // to SMS, where the payload is null by rule because the body carries a live credential.
        var manifest = attachments.Count == 0
            ? string.Empty
            : "\n\nAttachments:\n" + string.Join(
                "\n",
                attachments.Select(a => string.Create(
                    CultureInfo.InvariantCulture, $"- {a.FileName} ({a.ContentType}, {a.SizeBytes} bytes)")));

        var payload = $"{subject}\n\n{body}{manifest}";

        try
        {
            behavior.MaybeFail(nameof(IEmailSender));
        }
        catch (FakeTransientException ex)
        {
            await notifications.Failed(
                NotificationChannels.Email, recipientUserId, recipient, templateName, payload, ex.Message, ct);
            throw;
        }

        // Opened even though nothing reads them, because a real sender would and this is where the
        // difference shows: an attachment whose blob has been swept must fail the send rather than
        // slip out as an email that is quietly missing a file (§7.3).
        foreach (var attachment in attachments)
        {
            await using var content = await attachment.Open(ct)
                ?? throw new InvalidOperationException(
                    $"Attachment '{attachment.FileName}' has no bytes in blob storage.");

            LogAttachment(logger, attachment.FileName, attachment.ContentType, attachment.SizeBytes);
        }

        LogEmail(logger, recipient, subject, body);
        await notifications.Sent(NotificationChannels.Email, recipientUserId, recipient, templateName, payload, ct);
    }
}
