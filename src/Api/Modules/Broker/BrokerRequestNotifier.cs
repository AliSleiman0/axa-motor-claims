using Api.Infrastructure;
using Api.Integrations.Email;
using Api.Integrations.Push;
using Api.Modules.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Broker;

/// <summary>
/// design.md §8's "Option 2 file ready to send → push → Broker" row (slice 5.3).
///
/// **It lives here, in <c>Api.Modules.Broker</c>, and that is the whole point of the type.** The
/// caller is <c>Api.Modules.PublicSurface</c>, which architecture rule 2 forbids from referencing
/// <c>Api.Modules.Users</c> at all — and telling a broker their customer has finished means resolving
/// that broker's push subscriptions and their <see cref="BrokerProfile.Email"/>, both of which are
/// user data. The public module therefore hands over a request id and learns nothing; this module,
/// which already reads <c>Users</c>, does the resolving. The same boundary that made
/// <c>broker_display_name</c> a snapshot column, from the other direction.
///
/// The delivery shape is <c>DeclarationService.NotifyOne</c>'s, deliberately identical: push first,
/// §8's email fallback beneath it, and **a broad catch on both legs excluding only
/// <c>OperationCanceledException</c>**. Narrowing it to <c>PushNotDeliveredException</c> would miss
/// the two failures that actually happen — the fake sender throws <c>FakeTransientException</c>, and
/// an HTTP client reports its own timeout as <c>TaskCanceledException</c> (3.3's trap, reintroduced
/// by 3.4 the next day) — and either would fault a submission that has already committed. §5.3's
/// ordering rule again: the customer's work must not be lost because a push service was unreachable.
/// </summary>
public sealed partial class BrokerRequestNotifier(
    AppDbContext db,
    IPushSender push,
    IEmailSender email,
    ILogger<BrokerRequestNotifier> logger)
{
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Broker request {RequestId} is ready to send, but notifying broker {UserId} failed on every channel.")]
    private static partial void LogNotifyFailed(
        ILogger logger, Guid requestId, Guid userId, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Broker request {RequestId} is ready to send, but no such request exists to notify anyone about.")]
    private static partial void LogNoRequest(ILogger logger, Guid requestId);

    /// <summary>
    /// Tells the broker their Option 2 customer has submitted. **Best effort and never throws** — the
    /// caller has already committed the submission and locked the token, and §9.1's public surface
    /// must not turn a notification failure into an error the customer sees.
    /// </summary>
    public async Task NotifyReadyToSend(Guid requestId, CancellationToken ct)
    {
        // One statement, and a left join for `NotifyOfficers`' reason: a broker whose profile row is
        // missing still gets the popup, they simply have no address to fall back to. An inner join
        // would have skipped them in silence.
        var target = await db.BrokerRequests.AsNoTracking()
            .Where(r => r.Id == requestId)
            .GroupJoin(
                db.BrokerProfiles.AsNoTracking(),
                request => request.BrokerUserId,
                profile => profile.UserId,
                (request, profiles) => new
                {
                    request.BrokerUserId,
                    request.InsuredName,
                    Email = profiles.Select(p => p.Email).FirstOrDefault(),
                })
            .FirstOrDefaultAsync(ct);

        if (target is null)
        {
            LogNoRequest(logger, requestId);
            return;
        }

        const string title = "A customer completed your link";
        var body = target.InsuredName is { Length: > 0 } name
            ? $"{name}'s details are ready for you to review and send."
            : "A quotation request is ready for you to review and send.";

        try
        {
            await push.Send(
                target.BrokerUserId,
                // `/broker/{id}` is B4 — the review screen, not the list. A broker woken by this
                // notification is being asked to read one submission and press one button.
                new PushMessage(title, body, $"/broker/{requestId}"),
                NotificationTemplates.BrokerRequestReady,
                ct);
            return;
        }
        catch (Exception pushFailure) when (pushFailure is not OperationCanceledException)
        {
            if (string.IsNullOrWhiteSpace(target.Email))
            {
                LogNotifyFailed(logger, requestId, target.BrokerUserId, pushFailure);
                return;
            }

            try
            {
                await email.Send(
                    target.Email, title, body, NotificationTemplates.BrokerRequestReady,
                    target.BrokerUserId, [], ct);
            }
            catch (Exception emailFailure) when (emailFailure is not OperationCanceledException)
            {
                LogNotifyFailed(logger, requestId, target.BrokerUserId, emailFailure);
            }
        }
    }
}
