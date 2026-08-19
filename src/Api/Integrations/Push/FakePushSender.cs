using Api.Modules.Notifications;

namespace Api.Integrations.Push;

/// <summary>Dev/demo sender: the popup lands in the console and the `notification` log.</summary>
public sealed partial class FakePushSender(
    ILogger<FakePushSender> logger,
    NotificationLog notifications,
    FakeBehavior behavior) : IPushSender
{
    [LoggerMessage(Level = LogLevel.Information, Message = "FAKE PUSH to user {UserId} [{Title}]: {Body}")]
    private static partial void LogPush(ILogger logger, Guid userId, string title, string body);

    public async Task Send(
        Guid recipientUserId,
        string title,
        string body,
        string templateName,
        CancellationToken ct)
    {
        await behavior.Delay(ct);

        // No device tokens exist yet (registration is slice 3.4), so the user id is the best address
        // we have. When tokens arrive this becomes the token that was actually pushed to.
        var address = recipientUserId.ToString();
        var payload = $"{title}\n\n{body}";

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

        LogPush(logger, recipientUserId, title, body);
        await notifications.Sent(NotificationChannels.Push, recipientUserId, address, templateName, payload, ct);
    }
}
