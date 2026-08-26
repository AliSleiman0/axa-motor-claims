using Api.Modules.Notifications;

namespace Api.Integrations.Push;

/// <summary>
/// Sends every push down **every** channel this deployment has (design.md §8, slice 6.3).
///
/// **Additive, never modal, and that is the whole design.** A user legitimately has a browser and a
/// phone: an expert reads the worklist on a desk machine and is dispatched to a crash site with a
/// handset, and §4's `push_subscription` was already one-row-per-browser for exactly that reason.
/// Choosing a channel per user would mean guessing which device they are holding, which is the
/// guess slice 3.4 refused to make between two browsers and there is no better reason to make it
/// between a browser and a phone.
///
/// **The throw contract is the load-bearing part, because §8 hangs `notified_at` on it.**
/// <c>AssignmentHandler</c> stamps that column if and only if <c>Send</c> returns, and swallows the
/// exception otherwise — leaving the honest record that nobody was told. So:
///
/// <list type="bullet">
/// <item>if **any** channel delivered to **any** device, this returns — partial success is success,
/// which is the same rule each channel already applies internally across its own devices;</item>
/// <item>it throws <see cref="PushNotDeliveredException"/> only when **zero devices across all
/// channels** accepted;</item>
/// <item>an inner channel's *unexpected* exception counts as "delivered nothing there" and never
/// silences the other channel — the failure mode that would otherwise be introduced here is a
/// misconfigured FCM stopping web push from being attempted at all, which would be a strict
/// regression on the behaviour that shipped in 3.4.</item>
/// </list>
///
/// With <c>Push:Fcm:Enabled</c> false there is one channel and the behaviour is therefore identical
/// to today's, which is what makes turning FCM on a configuration change rather than a rewrite.
/// `fake` mode never builds one of these at all.
/// </summary>
public sealed partial class CompositePushSender(
    IReadOnlyList<IPushSender> channels,
    NotificationLog notifications,
    ILogger<CompositePushSender> logger) : IPushSender
{
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Push channel {Channel} delivered nothing to user {UserId}; other channels continue.")]
    private static partial void LogChannelFailed(
        ILogger logger, string channel, Guid userId, Exception exception);

    /// <summary>
    /// The channels composed, for the composition-root test only (<c>InternalsVisibleTo</c>).
    ///
    /// It exists because `Push:Fcm:Enabled` is read in exactly one place — the DI switch — and
    /// nothing else can observe the difference: both shapes resolve an `IPushSender` and both behave
    /// identically until a handset is registered. Without this, a typo in that configuration key
    /// would ship a webpush deployment whose Android half silently does not exist, which is the
    /// failure this slice was written to make impossible.
    /// </summary>
    internal IReadOnlyList<IPushSender> Channels => channels;

    public async Task Send(
        Guid recipientUserId,
        PushMessage message,
        string templateName,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);

        var delivered = false;
        var failures = new List<Exception>();

        foreach (var channel in channels)
        {
            try
            {
                await channel.Send(recipientUserId, message, templateName, ct);
                delivered = true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // A real shutdown, not a delivery failure. Propagated rather than collected: the
                // caller is being torn down and turning that into "nobody was told" would write a
                // conclusion nothing actually established.
                throw;
            }
            catch (Exception ex)
            {
                // Deliberately everything else, including a channel's own
                // PushNotDeliveredException and including an HttpClient timeout that escaped as a
                // TaskCanceledException with `ct` unset. Each channel already writes its own
                // `notification` rows before failing, so §8's log is complete without anything
                // being recorded here; what must not happen is one channel's fault ending the loop.
                failures.Add(ex);
                LogChannelFailed(logger, channel.GetType().Name, recipientUserId, ex);
            }
        }

        if (delivered)
        {
            return;
        }

        // **The one place "this user has no device anywhere" can honestly be written** (slice 6.3,
        // raised by the db-review). A channel cannot know it: an officer with a browser and no phone
        // makes the FCM channel come up empty on every single push, and a `failed` row from there
        // would say a notification failed that in fact arrived. Here, every channel having come up
        // empty means exactly what §8's log needs to record — nobody could have been told — so it is
        // one row, against the user rather than against any device, the way each channel used to
        // write it when there was only one.
        //
        // Failures of any other kind already wrote their own per-device rows before throwing, so
        // nothing is logged for them here and nothing is lost.
        if (failures.Count > 0 && failures.TrueForAll(f => f is PushChannelHasNoDevicesException))
        {
            await notifications.Failed(
                NotificationChannels.Push, recipientUserId, recipientUserId.ToString(), templateName,
                message.ToJson(), "The user has no registered device on any push channel.", ct);
        }

        throw new PushNotDeliveredException(
            $"None of the {channels.Count} push channel(s) delivered to user {recipientUserId}.",
            new AggregateException(failures));
    }
}
