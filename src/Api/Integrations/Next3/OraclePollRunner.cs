namespace Api.Integrations.Next3;

/// <summary>
/// design.md §6.2's Oracle-poll loop body: poll once, deliver every row found. All the logic lives
/// here rather than in <see cref="OraclePollWorker"/>, deliberately — the same split as
/// <c>Api.Outbox.OutboxWorker</c>/<c>OutboxProcessor</c>: a class that both schedules and works
/// cannot be tested without waiting.
/// </summary>
public sealed partial class OraclePollRunner(
    OraclePollAssignmentSource source,
    IAssignmentQuerySource querySource,
    ILogger<OraclePollRunner> logger)
{
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Delivering assignment {Ref} failed; the rest of this poll cycle continues.")]
    private static partial void LogDeliveryFailed(ILogger logger, string @ref, Exception exception);

    /// <summary>
    /// Runs one poll cycle: fetches the current rows and delivers each to the subscribed handler.
    /// Returns how many rows were found, so a caller can tell an empty cycle from a busy one.
    ///
    /// One row's delivery failing must not stop the rest of the batch — same isolation as
    /// <c>OutboxProcessor.Process</c>, and for the same reason: an exception from the handler for
    /// one visa is not evidence that the next row is bad too.
    /// </summary>
    public async Task<int> RunOnce(CancellationToken ct)
    {
        var rows = await querySource.PollAsync(ct);

        foreach (var row in rows)
        {
            try
            {
                await source.Deliver(row, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogDeliveryFailed(logger, row.Next3AssignmentRef, ex);
            }
        }

        return rows.Count;
    }
}
