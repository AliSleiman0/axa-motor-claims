using Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Notifications;

/// <summary>
/// The only sanctioned write path to `notification` (design.md §4, §8) — the AuditWriter precedent,
/// with one deliberate difference: AuditWriter joins the caller's transaction, this one commits its
/// own. A send is an external side effect that already happened (or already failed); its log row
/// must not disappear because the caller's transaction later rolls back.
///
/// Singleton, because the senders it serves are singletons. It creates its own scope per call
/// rather than holding a DbContext — a DbContext is scoped and is not thread-safe, so capturing one
/// in a singleton is the classic way to get "a second operation was started on this context".
/// </summary>
public sealed class NotificationLog(IServiceScopeFactory scopes, TimeProvider time)
{
    public Task Sent(
        string channel,
        Guid? recipientUserId,
        string recipientAddress,
        string template,
        string? payload,
        CancellationToken ct) =>
        Append(channel, recipientUserId, recipientAddress, template, payload, NotificationStatuses.Sent, null, ct);

    public Task Failed(
        string channel,
        Guid? recipientUserId,
        string recipientAddress,
        string template,
        string? payload,
        string error,
        CancellationToken ct) =>
        Append(channel, recipientUserId, recipientAddress, template, payload, NotificationStatuses.Failed, error, ct);

    private async Task Append(
        string channel,
        Guid? recipientUserId,
        string recipientAddress,
        string template,
        string? payload,
        string status,
        string? error,
        CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.Set<Notification>().Add(new Notification
        {
            Id = Guid.CreateVersion7(),
            Channel = channel,
            RecipientUserId = recipientUserId,
            RecipientAddress = recipientAddress,
            Template = template,
            Payload = payload,
            Status = status,
            SentAt = status == NotificationStatuses.Sent ? now : null,
            Error = error,
            CreatedAt = now,
        });

        await db.SaveChangesAsync(ct);
    }
}
