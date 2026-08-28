using Api.Infrastructure;
using Api.Infrastructure.Cleanup;
using Api.Integrations.Push;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Modules.Push;

/// <summary>
/// Prunes revoked rows from §8's two device tables (slice 7.2).
/// </summary>
/// <remarks>
/// <para>
/// Neither table has ever shed a row. Both revoke rather than delete, for a good reason — a
/// <c>notification</c> row naming a device should still resolve to something, and a token that was
/// taken from one user and handed to another is exactly the history somebody asks about. But the
/// inflow is continuous and entirely automatic: every 404/410 from a push service, every FCM
/// <c>UNREGISTERED</c>, every OEM battery kill that costs a handset its registration, and every
/// pooled field phone that changes hands adds one. Left alone the registry only grows, and it grows
/// fastest exactly where §8's fan-out reads it.
/// </para>
/// <para>
/// **Only revoked rows, and only past the window.** A live row is never a candidate whatever its age;
/// a non-null <c>RevokedAt</c> is what makes a row dead, and the window is what keeps the recent
/// history a support question would want. <c>ExecuteDelete</c> rather than a tracked loop, like
/// <c>OtpChallengeCleanupTask</c>: there are no locking semantics to express, and nothing to audit —
/// §9 records the *revocation*, which already happened; pruning its tombstone is housekeeping.
/// </para>
/// <para>
/// The window is a placeholder (<c>Retention:RevokedDeviceDays</c>, 30 days), #4/#22.
/// </para>
/// </remarks>
public sealed class DeviceRegistryCleanupTask(
    AppDbContext db,
    IOptionsMonitor<RetentionOptions> options,
    TimeProvider time) : ICleanupTask
{
    public string Name => "device_registry";

    public async Task<int> Run(CancellationToken ct)
    {
        var cutoff = time.GetUtcNow().UtcDateTime.AddDays(-options.CurrentValue.RevokedDeviceDays);

        var tokens = await db.Set<DeviceToken>()
            .Where(t => t.RevokedAt != null && t.RevokedAt <= cutoff)
            .ExecuteDeleteAsync(ct);

        var subscriptions = await db.Set<PushSubscription>()
            .Where(s => s.RevokedAt != null && s.RevokedAt <= cutoff)
            .ExecuteDeleteAsync(ct);

        return tokens + subscriptions;
    }
}
