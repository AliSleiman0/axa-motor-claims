using Api.Infrastructure;
using Api.Infrastructure.Cleanup;
using Api.Integrations.Next3;
using Api.Modules.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Modules.Users;

/// <summary>
/// design.md §6.1/#7/#8, resolved 2026-08-31, built slice 7.5: reconciles `expert_profile` and
/// `garage_profile` against NEXT3's live supplier list. A supplier newly in network gets a profile
/// + SMS invite (the exact three-step pipeline `AdminProfileEndpoints`'s create-expert/create-garage
/// already uses); a supplier that drops out of network, or that NEXT3 itself marks inactive, blocks
/// that user's login until it reappears. Claim officers and brokers are explicitly **not** sourced
/// from NEXT3 (Q7's own answer) and this task never touches them.
///
/// **Singleton, unlike every other `ICleanupTask` in this codebase, and that is deliberate.**
/// `CleanupRunner.RunOnce` resolves `IEnumerable&lt;ICleanupTask&gt;` fresh every pass from one
/// shared scope; a scoped registration would give this task a fresh instance every pass, and the
/// self-throttling this class does (once/day inside `CleanupWorker`'s hourly shared loop) needs an
/// instance field that survives between passes. .NET DI resolves each registration according to
/// its own lifetime regardless of what else shares the interface, so a singleton sitting beside
/// several scoped `ICleanupTask` registrations is safe and correctly returns the same instance
/// every pass. Because it is a singleton it cannot take scoped dependencies
/// (`AppDbContext`/`InviteService`/`AuditWriter`/`TokenService`) via constructor — it opens its own
/// scope inside <see cref="Run"/>, the same pattern `CleanupRunner` itself uses one level up.
///
/// A bespoke worker/runner pair (slice 7.4's `OraclePollWorker`/`OraclePollRunner`) was rejected:
/// that shape exists for a genuinely different cadence (15 seconds), while a once/day sync sits
/// comfortably inside `CleanupWorker`'s hour-scale shared timer.
/// </summary>
public sealed partial class MasterDataSyncTask(
    INext3Client next3,
    IServiceScopeFactory scopes,
    IOptionsMonitor<Next3Options> options,
    TimeProvider time,
    ILogger<MasterDataSyncTask> logger) : ICleanupTask
{
    private DateTimeOffset? _lastSyncedAt;

    /// <summary>
    /// Test-only: this task is a process-wide singleton (see the class doc comment), so its
    /// self-throttle state outlives any one test in the shared, serialized "api" collection.
    /// Without this, only the first test in the run to call <see cref="Run"/> would ever see a real
    /// sync pass — every later test's call would silently no-op against a throttle state it never set.
    /// </summary>
    internal void ResetThrottleForTests() => _lastSyncedAt = null;

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Master-data sync skipped a {Role} supplier {Next3Id}: invalid mobile number.")]
    private static partial void LogSkippedInvalidPhone(ILogger logger, UserRole role, string next3Id);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Master-data sync skipped a {Role} supplier {Next3Id}: phone already in use by another user.")]
    private static partial void LogSkippedDuplicatePhone(ILogger logger, UserRole role, string next3Id);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Master-data sync's block/unblock of {Role} supplier {Next3Id} lost a race to a concurrent " +
            "update (an admin action or another replica's pass); left for the next pass to re-decide.")]
    private static partial void LogSkippedConcurrentUpdate(ILogger logger, UserRole role, string next3Id);

    public string Name => "next3_master_data_sync";

    public async Task<int> Run(CancellationToken ct)
    {
        var interval = TimeSpan.FromHours(options.CurrentValue.MasterDataSyncIntervalHours);
        var now = time.GetUtcNow();
        if (_lastSyncedAt is { } last && now - last < interval)
        {
            return 0;
        }

        var experts = await next3.GetExperts(ct);
        var garages = await next3.GetGarages(ct);

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invites = scope.ServiceProvider.GetRequiredService<InviteService>();
        var audit = scope.ServiceProvider.GetRequiredService<AuditWriter>();
        var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();

        var expertRows = experts.Select(e => new SupplierRow(e.Next3Id, e.Name, e.Mobile, e.Email, e.Active)).ToList();
        var garageRows = garages.Select(g => new SupplierRow(g.Next3Id, g.Name, g.Mobile, g.Email, g.Active)).ToList();

        var touched = 0;
        touched += await ReconcileAsync(
            db, invites, audit, tokens, UserRole.Expert, expertRows, db.ExpertProfiles,
            p => p.Next3Id, p => p.UserId,
            (p, active, at) => { p.Active = active; p.InactivatedAt = at; },
            (row, userId) => new ExpertProfile { UserId = userId, Next3Id = row.Next3Id, Email = row.Email, Active = true },
            AuditEntityKinds.ExpertProfile, ct);
        touched += await ReconcileAsync(
            db, invites, audit, tokens, UserRole.Garage, garageRows, db.GarageProfiles,
            p => p.Next3Id, p => p.UserId,
            (p, active, at) => { p.Active = active; p.InactivatedAt = at; },
            // NEXT3 supplies no separate contact name (docs/client-answers-2026-08-31/suppliers-list.sql
            // has no such column) — the supplier's business name seeds it; an admin can edit it later.
            (row, userId) => new GarageProfile
            {
                UserId = userId,
                ContactName = row.Name,
                Email = row.Email,
                Next3Id = row.Next3Id,
                Active = true,
            },
            AuditEntityKinds.GarageProfile, ct);

        _lastSyncedAt = now;
        return touched;
    }

    /// <summary>The columns `Next3Expert` and `Next3Garage` share — see design.md §6.1's two-branch query.</summary>
    private sealed record SupplierRow(string Next3Id, string Name, string Mobile, string Email, bool Active);

    /// <summary>
    /// One reconciliation pass for one profile type. Generic over <typeparamref name="TProfile"/>
    /// rather than duplicated per role: the reference SQL's `EXPERT`/`GARAGE` branches are
    /// structurally identical, and CLAUDE.md's recurring-bug-class list is explicit that a lesson
    /// fixed in one branch is not learned until it is checked in the other — a shared algorithm
    /// means there is only one branch to check.
    ///
    /// Pulls every profile of this type into memory rather than filtering in SQL: `Next3Id` access
    /// is a delegate, not an expression tree EF can translate, and #20's resolved real volumes
    /// (15 claims/day, dozens of suppliers at most) make a full scan here free.
    /// </summary>
    private async Task<int> ReconcileAsync<TProfile>(
        AppDbContext db, InviteService invites, AuditWriter audit, TokenService tokens,
        UserRole role, IReadOnlyList<SupplierRow> rows, DbSet<TProfile> profileSet,
        Func<TProfile, string?> getNext3Id, Func<TProfile, Guid> getUserId,
        Action<TProfile, bool, DateTime?> setActive, Func<SupplierRow, Guid, TProfile> createProfile,
        string entityKind, CancellationToken ct)
        where TProfile : class
    {
        var now = time.GetUtcNow().UtcDateTime;
        // AsNoTracking discovery only — which profiles exist and what their Next3Id is. The actual
        // mutation reads AppUser fresh, per row, immediately before saving (below): AppUser.Status
        // is the concurrency token (added alongside this task, db-review finding), and a bulk
        // upfront load held across this whole pass — which includes the NEXT3 round trip and every
        // other row's work — would check that token against a snapshot that is stale by the time it
        // matters, exactly the lost-update window AdminUserEndpoints.Deactivate can land in.
        var discovery = await profileSet.AsNoTracking().ToListAsync(ct);
        var byRef = rows.ToDictionary(r => r.Next3Id, StringComparer.Ordinal);

        var touched = 0;
        var knownNext3Ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var profile in discovery)
        {
            var id = getNext3Id(profile);
            if (id is null)
            {
                continue;
            }

            knownNext3Ids.Add(id);
            var userId = getUserId(profile);
            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null)
            {
                continue;
            }

            // "Should be active" folds together two NEXT3 answers into one condition: a supplier
            // absent from the result entirely (dropped out of network) and one present but marked
            // ACTIVE='N' (NEXT3-side inactive while still in network) are the same fact to us.
            var shouldBeActive = byRef.TryGetValue(id, out var row) && row.Active;

            if (shouldBeActive && user.Status == UserStatus.SyncBlocked)
            {
                db.Attach(profile);
                user.Status = UserStatus.Active;
                user.InactivatedAt = null;
                setActive(profile, true, null);
                audit.Append(null, AuditActions.SupplierSyncUnblocked, entityKind, userId, new { Next3Id = id });
                if (await TrySave(db, role, id, ct))
                {
                    touched++;
                }
            }
            else if (!shouldBeActive && user.Status == UserStatus.Active)
            {
                // Never reached when user.Status is Inactive (admin-deactivated) — the whole point
                // of a distinct SyncBlocked status (design.md §4, decided with the developer): this
                // sync must never silently undo an admin's deliberate deactivation. Guaranteed by
                // AppUser.Status being the concurrency token, not merely by this in-memory check —
                // the check alone was the db-review's finding: a snapshot read makes the guarantee
                // only as good as how fresh the snapshot still is.
                db.Attach(profile);
                user.Status = UserStatus.SyncBlocked;
                user.InactivatedAt = now;
                setActive(profile, false, now);
                await tokens.RevokeAll(userId, ct);
                await AdminUserEndpoints.RevokeDevices(db, audit, null, userId, now, ct);
                audit.Append(null, AuditActions.SupplierSyncBlocked, entityKind, userId, new { Next3Id = id });
                if (await TrySave(db, role, id, ct))
                {
                    touched++;
                }
            }
        }

        foreach (var row in rows.Where(r => r.Active && !knownNext3Ids.Contains(r.Next3Id)))
        {
            if (!Phone.IsValidE164(row.Mobile))
            {
                LogSkippedInvalidPhone(logger, role, row.Next3Id);
                continue;
            }

            if (await db.Users.AnyAsync(u => u.Phone == row.Mobile, ct))
            {
                LogSkippedDuplicatePhone(logger, role, row.Next3Id);
                continue;
            }

            var user = new AppUser
            {
                Id = Guid.CreateVersion7(),
                Phone = row.Mobile,
                Role = role,
                DisplayName = row.Name,
                Status = UserStatus.Invited,
                CreatedAt = now,
            };
            db.Users.Add(user);
            profileSet.Add(createProfile(row, user.Id));
            audit.Append(null, AuditActions.SupplierSyncCreated, entityKind, user.Id, new { row.Next3Id, row.Email });

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Race backstop, same as AdminProfileEndpoints.SaveThenInvite: the unique indexes
                // (phone, next3_id) are the real guard. The next sync pass picks this row up again.
                db.ChangeTracker.Clear();
                continue;
            }

            await invites.Issue(user.Id, ct);
            touched++;
        }

        return touched;
    }

    /// <summary>
    /// Saves one row's block/unblock, catching a lost race to `AdminUserEndpoints.Deactivate` or
    /// another replica's own sync pass — db-review finding on this task's first draft.
    /// `AppUser.Status` is the concurrency token (`AppUserConfiguration`), so a stale write here
    /// throws rather than silently clobbering whatever the other writer just committed; there is
    /// nothing to reconcile mid-pass, since the next scheduled sync re-decides from scratch against
    /// whatever the database says by then.
    /// </summary>
    private async Task<bool> TrySave(AppDbContext db, UserRole role, string next3Id, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            LogSkippedConcurrentUpdate(logger, role, next3Id);
            db.ChangeTracker.Clear();
            return false;
        }
    }
}
