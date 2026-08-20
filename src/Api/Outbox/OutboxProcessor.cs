using Api.Infrastructure;
using Api.Integrations;
using Api.Integrations.Next3;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Outbox;

/// <summary>
/// design.md §6.3's worker loop body: claim a batch, push each row to NEXT3, record the outcome.
/// The only type in the codebase that calls <see cref="INext3Client"/>'s push operations — architecture
/// rule 3 enforces that with an IL-level test, so a feature module cannot quietly bypass the queue.
///
/// Singleton holding an <see cref="IServiceScopeFactory"/> rather than an AppDbContext: the context is
/// scoped and this is driven from a hosted service. Same shape as NotificationLog and
/// AssignmentIngestion.
/// </summary>
public sealed partial class OutboxProcessor(
    IServiceScopeFactory scopes,
    INext3Client next3,
    IOptionsMonitor<OutboxOptions> options,
    TimeProvider time,
    ILogger<OutboxProcessor> logger)
{
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Outbox {Operation} {MessageId} failed on attempt {Attempts}; disposition {Disposition}.")]
    private static partial void LogPushFailed(
        ILogger logger, string operation, Guid messageId, int attempts, string disposition, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Outbox {Operation} {MessageId} was reclaimed by another worker after attempt {Attempts}; dropping this outcome.")]
    private static partial void LogClaimLost(
        ILogger logger, string operation, Guid messageId, int attempts);

    /// <summary>
    /// Runs one pass: claims a batch and processes it. Returns how many rows were handled, so a caller
    /// can tell a quiet queue from a busy one.
    /// </summary>
    public async Task<int> RunOnce(CancellationToken ct)
    {
        var settings = options.CurrentValue;
        var now = time.GetUtcNow().UtcDateTime;

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dequeue = scope.ServiceProvider.GetRequiredService<OutboxDequeue>();

        // Clamped: `TOP (0)` claims nothing and raises nothing, so a mistyped Outbox:BatchSize in the
        // reloadOnChange placeholder file would silently stop every NEXT3 push while the worker went
        // on looking healthy. A negative value throws instead, which is just as invisible from here.
        var batchSize = Math.Max(1, settings.BatchSize);

        var batch = await dequeue.Claim(
            batchSize, now, settings.LeaseSeconds, settings.MaxAttempts, ct);

        foreach (var message in batch)
        {
            await Process(db, message, settings, ct);
        }

        return batch.Count;
    }

    private async Task Process(
        AppDbContext db, Next3OutboxMessage message, OutboxOptions settings, CancellationToken ct)
    {
        try
        {
            await Push(message, ct);
            MarkSent(message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            MarkFailure(message, ex, settings);
        }

        try
        {
            // One SaveChanges per message, not per batch: a poison row must not roll back the
            // outcomes of the rows that pushed fine alongside it.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another worker reclaimed this row after its lease expired and has taken ownership of
            // the outcome. Ours is stale — dropping it is the whole point of the token. The push
            // itself may well have gone through, which is exactly the case clientRef covers.
            LogClaimLost(logger, message.Operation, message.Id, message.Attempts);

            // Detach, or the stale change would be replayed on the *next* message's SaveChanges and
            // fail the rest of the batch behind it.
            db.Entry(message).State = EntityState.Detached;
        }
    }

    private Task Push(Next3OutboxMessage message, CancellationToken ct) => message.Operation switch
    {
        Next3OutboxOperations.UpdateArrival => PushArrival(message, ct),

        // push_approval rides the same NEXT3 call as an ordinary document — §6.2's interface has no
        // separate method, and §5.2 describes the approval as a PNG landing in the *Survey* folder.
        // The folder comes from the payload the producer wrote, so this dispatch stays dumb.
        Next3OutboxOperations.UploadDocument or Next3OutboxOperations.PushApproval =>
            PushDocument(message, ct),

        _ => throw new InvalidOperationException(
            $"Unknown outbox operation '{message.Operation}' on message {message.Id}."),
    };

    private Task PushArrival(Next3OutboxMessage message, CancellationToken ct)
    {
        var payload = OutboxPayloads.Deserialize<ArrivalOutboxPayload>(message.Payload);
        return next3.RecordArrival(message.VisaNo, payload.ToArrivalInfo(), payload.ClientRef, ct);
    }

    private Task PushDocument(Next3OutboxMessage message, CancellationToken ct)
    {
        var payload = OutboxPayloads.Deserialize<DocumentOutboxPayload>(message.Payload);
        return next3.UploadDocument(message.VisaNo, payload.ToDocumentPush(), payload.ClientRef, ct);
    }

    private void MarkSent(Next3OutboxMessage message)
    {
        message.Status = Next3OutboxStatuses.Sent;
        message.SentAt = time.GetUtcNow().UtcDateTime;
        message.LastError = null;

        // §7.3: the cleanup job deletes a blob only after its outbox row reads `sent`. Nothing here
        // may set that status before the push has actually returned.
    }

    private void MarkFailure(Next3OutboxMessage message, Exception ex, OutboxOptions settings)
    {
        var retryable = IsTransient(ex) && message.Attempts < settings.MaxAttempts;

        message.LastError = $"{ex.GetType().Name}: {ex.Message}";

        if (retryable)
        {
            message.Status = Next3OutboxStatuses.Pending;
            message.NextRetryAt = time.GetUtcNow().UtcDateTime
                + OutboxBackoff.Delay(message.Attempts, settings.BackoffCeilingHours);
        }
        else
        {
            // Terminal until an admin retries it from A2 (§5.4). `sent_at` stays null.
            message.Status = Next3OutboxStatuses.Failed;
        }

        LogPushFailed(
            logger,
            message.Operation,
            message.Id,
            message.Attempts,
            retryable ? "retry" : Next3OutboxStatuses.Failed,
            ex);
    }

    /// <summary>
    /// Which failures are worth retrying. §6.3 describes a retry schedule but not this split; the fake
    /// NEXT3 was built for it in slice 1.4, where an unknown visa throws something *other* than
    /// <see cref="FakeTransientException"/> precisely "so the outbox can tell the two apart and drive
    /// this one to `failed` instead of retrying eight times".
    ///
    /// Unrecognised failures are treated as permanent on purpose: a bug or a malformed payload
    /// surfaces on A2 within seconds, where someone can see it, rather than being retried silently for
    /// fourteen hours first. A2's Retry button makes that decision reversible.
    /// </summary>
    private static bool IsTransient(Exception ex) =>
        ex is FakeTransientException or HttpRequestException or TimeoutException;
}
