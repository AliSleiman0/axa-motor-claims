using Microsoft.Extensions.Options;

namespace Api.Integrations.Next3;

/// <summary>
/// design.md §6.2's Oracle-poll schedule: drains <see cref="OraclePollRunner"/> every
/// `Next3:AssignmentPollSeconds` (default 15, per the client's own answer to #34).
///
/// **Registered only when `Next3:AssignmentSource` is `oracle-poll`** (`Api.Composition`'s
/// `ServiceRegistration`) — unlike `Api.Outbox.OutboxWorker`/`Api.Modules.Cleanup.CleanupWorker`,
/// which run in every environment and need a config flag purely for test isolation, this loop is
/// only ever meaningful in the one mode nothing sets today, so it is never registered rather than
/// registered-and-permanently-idle.
///
/// All the logic lives in <see cref="OraclePollRunner"/>, deliberately: a class that both schedules
/// and works cannot be tested without waiting.
/// </summary>
public sealed partial class OraclePollWorker(
    OraclePollRunner runner,
    IOptionsMonitor<Next3Options> options,
    TimeProvider time,
    ILogger<OraclePollWorker> logger) : BackgroundService
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Oracle poll cycle failed; the loop continues.")]
    private static partial void LogPassFailed(ILogger logger, Exception exception);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // TimeProvider overload, not the bare one: a FakeTimeProvider never advances on its own, so
        // a raw PeriodicTimer would hang any test that boots the host in oracle-poll mode.
        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(options.CurrentValue.AssignmentPollSeconds), time);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await runner.RunOnce(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A failed pass must never kill the loop: the next poll may find NEXT3's DB (or
                // ours) recovered. Individual delivery failures are handled inside RunOnce;
                // reaching here means the poll itself failed, e.g. the Oracle connection dropped.
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
