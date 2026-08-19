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

    public async Task Send(
        string recipient,
        string subject,
        string body,
        string templateName,
        Guid? recipientUserId,
        CancellationToken ct)
    {
        await behavior.Delay(ct);

        var payload = $"{subject}\n\n{body}";

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

        LogEmail(logger, recipient, subject, body);
        await notifications.Sent(NotificationChannels.Email, recipientUserId, recipient, templateName, payload, ct);
    }
}
