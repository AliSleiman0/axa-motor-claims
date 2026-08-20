using Microsoft.Extensions.Options;

namespace Api.Outbox;

/// <summary>
/// design.md §6.3's worker: drains the outbox every ~30 s. Hosted in the API process for now; §3
/// deploys it as a Container Apps Job, which is the same loop with a different host.
///
/// This is the only namespace allowed to call INext3Client push operations (architecture rule 3).
///
/// All the logic lives in <see cref="OutboxProcessor"/>, deliberately: a class that both schedules
/// and works cannot be tested without waiting, and §6.3's retry schedule spans hours.
/// </summary>
public sealed partial class OutboxWorker(
    OutboxProcessor processor,
    IOptionsMonitor<OutboxOptions> options,
    TimeProvider time,
    ILogger<OutboxWorker> logger) : BackgroundService
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox worker disabled by configuration.")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox pass failed; the loop continues.")]
    private static partial void LogPassFailed(ILogger logger, Exception exception);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.CurrentValue.WorkerEnabled)
        {
            LogDisabled(logger);
            return;
        }

        // TimeProvider overload, not the bare one: a FakeTimeProvider never advances on its own, so a
        // raw PeriodicTimer would hang any test that boots the host.
        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(options.CurrentValue.PollSeconds), time);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await processor.RunOnce(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A failed pass must never kill the loop: the queue draining on recovery is the whole
                // reason the outbox exists (§6.3). Individual push failures are handled inside
                // RunOnce; reaching here means the claim itself failed, e.g. the database was down.
                LogPassFailed(logger, ex);
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
