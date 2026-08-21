using Api.Modules.Notifications;

namespace Api.Integrations.Push;

/// <summary>Dev/demo sender: the popup lands in the console and the `notification` log.</summary>
public sealed partial class FakePushSender(
    ILogger<FakePushSender> logger,
    NotificationLog notifications,
    FakeBehavior behavior) : IPushSender
{
    [LoggerMessage(Level = LogLevel.Information, Message = "FAKE PUSH to user {UserId} [{Title}]: {Body} -> {Url}")]
    private static partial void LogPush(ILogger logger, Guid userId, string title, string body, string url);

    public async Task Send(
        Guid recipientUserId,
        PushMessage message,
        string templateName,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);

        await behavior.Delay(ct);

        // The user id, because the fake has no devices. The real sender logs one row per subscription
        // and puts that subscription's id here — see WebPushSender for why it is the id rather than
        // the endpoint (`notification.recipient_address` is nvarchar(320); endpoints are longer).
        var address = recipientUserId.ToString();
        var payload = message.ToJson();

        try
        {
            behavior.MaybeFail(nameof(IPushSender));
        }
        catch (FakeTransientException ex)
        {
            await notifications.Failed(
                NotificationChannels.Push, recipientUserId, address, templateName, payload, ex.Message, ct);
            throw;
        }

        LogPush(logger, recipientUserId, message.Title, message.Body, message.Url);
        await notifications.Sent(NotificationChannels.Push, recipientUserId, address, templateName, payload, ct);
    }
}
