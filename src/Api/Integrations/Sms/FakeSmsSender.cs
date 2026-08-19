using Api.Integrations;
using Api.Modules.Notifications;

namespace Api.Integrations.Sms;

/// <summary>Dev/demo sender: the OTP code shows up in the console instead of a phone (#7/#40).</summary>
public sealed partial class FakeSmsSender(
    ILogger<FakeSmsSender> logger,
    NotificationLog notifications,
    FakeBehavior behavior) : ISmsSender
{
    [LoggerMessage(Level = LogLevel.Information, Message = "FAKE SMS to {Phone}: {Message}")]
    private static partial void LogSms(ILogger logger, string phone, string message);

    public async Task Send(
        string phone,
        string message,
        string templateName,
        Guid? recipientUserId,
        CancellationToken ct)
    {
        await behavior.Delay(ct);

        try
        {
            behavior.MaybeFail(nameof(ISmsSender));
        }
        catch (FakeTransientException ex)
        {
            await notifications.Failed(
                NotificationChannels.Sms, recipientUserId, phone, templateName, null, ex.Message, ct);
            throw;
        }

        LogSms(logger, phone, message);

        // Payload stays null on purpose. Every SMS this app sends carries a live credential — an OTP
        // code or an invite token — and §9 hashes those in their own tables precisely so a database
        // leak does not hand out working logins. Copying the plaintext into the notification log
        // would give that back. The log records that a send happened, to whom, under which template.
        await notifications.Sent(NotificationChannels.Sms, recipientUserId, phone, templateName, null, ct);
    }
}
