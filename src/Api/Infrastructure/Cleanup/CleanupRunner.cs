namespace Api.Infrastructure.Cleanup;

/// <summary>
/// Runs every registered <see cref="ICleanupTask"/> once.
///
/// Split from <see cref="CleanupWorker"/> for the reason slice 2.2 split <c>OutboxProcessor</c> from
/// <c>OutboxWorker</c>: a class that both schedules and works cannot be tested without waiting, and
/// §7.3's retention windows are measured in days. Tests call <see cref="RunOnce"/> directly — the same
/// code the loop runs, on the test's schedule.
///
/// Singleton holding an <see cref="IServiceScopeFactory"/>: the tasks need a scoped
/// <see cref="AppDbContext"/> and this is driven from a hosted service.
/// </summary>
public sealed partial class CleanupRunner(IServiceScopeFactory scopes, ILogger<CleanupRunner> logger)
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Cleanup task {Task} handled {Count} item(s).")]
    private static partial void LogSwept(ILogger logger, string task, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Cleanup task {Task} failed; the pass continues.")]
    private static partial void LogTaskFailed(ILogger logger, string task, Exception exception);

    /// <summary>Runs one pass. Returns each task's count, keyed by name.</summary>
    public async Task<IReadOnlyDictionary<string, int>> RunOnce(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var tasks = scope.ServiceProvider.GetServices<ICleanupTask>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var results = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var task in tasks)
        {
            try
            {
                var count = await task.Run(ct);
                results[task.Name] = count;

                if (count > 0)
                {
                    LogSwept(logger, task.Name, count);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One failing sweep must not stop the others: a storage outage should not also stop
                // the OTP purge, and the next pass retries anyway.
                LogTaskFailed(logger, task.Name, ex);
                results[task.Name] = 0;
            }
            finally
            {
                // **Every task in a pass shares this scope's DbContext, so a half-built batch belongs
                // to nobody once its own task has stopped (slice 7.2, db-review).** A sweep that
                // throws mid-loop leaves its staged changes in the tracker; the next task's own
                // `SaveChanges` would then flush them inside *its* transaction — attributing one
                // sweep's writes to another, and, if they are invalid, failing that sweep on every
                // pass for ever with a log line naming the wrong task. Clearing here is cheaper and
                // more obviously right than a scope each: after a successful task there is nothing
                // left to clear, and after a failed one there is nothing worth keeping.
                db.ChangeTracker.Clear();
            }
        }

        return results;
    }
}
