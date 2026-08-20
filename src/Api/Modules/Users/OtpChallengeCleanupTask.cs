using Api.Infrastructure;
using Api.Infrastructure.Cleanup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Modules.Users;

/// <summary>
/// design.md §4: `otp_challenge` is "TTL'd, purged by cleanup job". Slice 1.2 deferred this to
/// whichever slice first built cleanup-job infrastructure, which is 2.3 (scope-decisions.md).
///
/// The rows are dead weight the moment they expire — consumed or not — and every one of them holds a
/// hash of a live-at-the-time login credential, so keeping them past their TTL is a small standing
/// liability for no benefit. The window is generous (`Retention:OtpChallengeHours`, a day by default)
/// because §9's resend throttle reads recent rows and support occasionally wants to see the last
/// attempt; it is orders of magnitude longer than `Auth:OtpTtlMinutes`, so nothing live is ever
/// touched.
///
/// A bulk delete, which §3 sanctions ("bulk/reporting queries") — but as `ExecuteDelete` rather than
/// a stored procedure, since there are no locking semantics to express here.
/// </summary>
public sealed class OtpChallengeCleanupTask(
    AppDbContext db,
    IOptionsMonitor<RetentionOptions> options,
    TimeProvider time) : ICleanupTask
{
    public string Name => "otp_challenges";

    public Task<int> Run(CancellationToken ct)
    {
        var cutoff = time.GetUtcNow().UtcDateTime.AddHours(-options.CurrentValue.OtpChallengeHours);

        return db.OtpChallenges.Where(c => c.ExpiresAt < cutoff).ExecuteDeleteAsync(ct);
    }
}
