namespace Api.Outbox;

// Shell only — the §6.3 dequeue/retry loop lands in slice 2.2. This is the only namespace
// allowed to call INext3Client push operations (arch rule 3).
public sealed class OutboxWorker : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
}
