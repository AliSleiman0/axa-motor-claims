namespace Api.Infrastructure.Cleanup;

/// <summary>
/// One housekeeping sweep. Registering a task rather than adding a method to one big job keeps each
/// sweep in the module that owns the data — §7.3's blob retention lives in the media module, §4's
/// `otp_challenge` TTL lives in the users module — while they share a single schedule.
/// </summary>
public interface ICleanupTask
{
    /// <summary>Used in logs and in the pass summary a test asserts on.</summary>
    string Name { get; }

    /// <summary>Runs one sweep and returns how many items it dealt with.</summary>
    Task<int> Run(CancellationToken ct);
}
