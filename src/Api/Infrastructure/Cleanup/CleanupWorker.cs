using Microsoft.Extensions.Options;

namespace Api.Infrastructure.Cleanup;

/// <summary>
/// Schedules <see cref="CleanupRunner"/>. Holds no behaviour, for the reason recorded there.
/// Deployed alongside the outbox worker as a Container Apps Job (§3, §10).
/// </summary>
public sealed partial class CleanupWorker(
    CleanupRunner runner,
    IOptionsMonitor<RetentionOptions> options,
    TimeProvider time,
    ILogger<CleanupWorker> logger) : BackgroundService
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Cleanup worker disabled by configuration.")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Cleanup pass failed; the loop continues.")]
    private static partial void LogPassFailed(ILogger logger, Exception exception);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.CurrentValue.CleanupEnabled)
        {
            LogDisabled(logger);
            return;
        }

        // TimeProvider overload: a FakeTimeProvider never advances on its own, so a bare PeriodicTimer
        // would hang any test that boots the host (the 2.2 lesson, restated because it is invisible).
        using var timer = new PeriodicTimer(
            TimeSpan.FromMinutes(Math.Max(1, options.CurrentValue.PollMinutes)), time);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await runner.RunOnce(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
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
