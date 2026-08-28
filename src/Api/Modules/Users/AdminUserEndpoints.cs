using System.Security.Claims;
using Api.Infrastructure;
using Api.Integrations.Push;
using Api.Modules.Audit;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Users;

/// <summary>
/// User-level admin actions shared by all profile types: deactivate (§5.4 — terminal for login,
/// in-flight data untouched) and invite re-issue. Profile CRUD lives in AdminProfileEndpoints.
/// </summary>
public static class AdminUserEndpoints
{
    public static IEndpointRouteBuilder MapAdminUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/users").RequireAuthorization(AuthPolicies.Admin);

        group.MapPost("/{id:guid}/deactivate", async (
            Guid id, ClaimsPrincipal principal, AppDbContext db, TokenService tokens, AuditWriter audit,
            TimeProvider time, CancellationToken ct) =>
        {
            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, ct);
            if (user is null)
            {
                return Results.NotFound();
            }

            if (user.Status != UserStatus.Inactive)
            {
                var now = time.GetUtcNow().UtcDateTime;
                user.Status = UserStatus.Inactive;
                user.InactivatedAt = now;
                await DeactivateProfile(db, user, now, ct);
                await tokens.RevokeAll(id, ct);
                await RevokeDevices(db, audit, principal.GetUserId(), id, now, ct);
                audit.Append(principal.GetUserId(), AuditActions.UserDeactivated, AuditEntityKinds.AppUser, id);
                // One SaveChanges = one transaction: status flip, profile flag, token
                // revocations, device revocations, and audit rows commit together. A repeat
                // deactivate is a no-op 200 and writes no audit row.
                await db.SaveChangesAsync(ct);
            }

            return Results.Ok();
        });

        group.MapPost("/{id:guid}/invite", async (
            Guid id, ClaimsPrincipal principal, AppDbContext db, InviteService invites, AuditWriter audit,
            CancellationToken ct) =>
        {
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, ct);
            if (user is null)
            {
                return Results.NotFound();
            }

            if (user.Status != UserStatus.Invited)
            {
                return Results.Conflict(new { error = "not_invited" });
            }

            // Issue saves — the invite row and the audit row commit together.
            audit.Append(principal.GetUserId(), AuditActions.InviteIssued, AuditEntityKinds.AppUser, id);
            await invites.Issue(id, ct);
            return Results.Ok();
        });

        return app;
    }

    /// <summary>
    /// Stops a deactivated user's devices from being notified (slice 7.2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// §9 says tokens are invalidated on deactivation, and until this slice that meant refresh tokens
    /// only. §8's fan-out does not read a session: <c>CompositePushSender</c> resolves a user's live
    /// <c>push_subscription</c> and <c>device_token</c> rows and sends to all of them. So a
    /// deactivated expert kept receiving claim assignments — visa number in the body, deep link in
    /// the payload — on a phone they may no longer be entitled to hold, and the app they would tap
    /// into is the one thing that *was* correctly locked out.
    /// </para>
    /// <para>
    /// **Tracked writes, not <c>ExecuteUpdate</c>.** That statement commits on its own outside the
    /// change tracker, so the revocations would land in a different transaction from the status flip
    /// beside them — and a fault in between would leave a user who is still active with no devices,
    /// or inactive with live ones. Both halves belong to the same decision, so both belong to the
    /// same <c>SaveChanges</c>, which is also what lets the audit rows join it.
    /// </para>
    /// <para>
    /// Revoked rather than deleted, like every other revocation in §8's registry: the user may be
    /// reactivated, and a <c>notification</c> row naming one of these devices should still resolve.
    /// <c>Reason = "deactivated"</c> is what tells that apart on A2-style support queries from a
    /// handset the person themselves turned off.
    /// </para>
    /// </remarks>
    private static async Task RevokeDevices(
        AppDbContext db, AuditWriter audit, Guid? actorUserId, Guid userId, DateTime now,
        CancellationToken ct)
    {
        var subscriptions = await db.Set<PushSubscription>()
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var subscription in subscriptions)
        {
            subscription.RevokedAt = now;
            audit.Append(
                actorUserId, AuditActions.PushSubscriptionRemoved, AuditEntityKinds.PushSubscription,
                subscription.Id, new { Reason = "deactivated" });
        }

        var devices = await db.Set<DeviceToken>()
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var device in devices)
        {
            device.RevokedAt = now;
            audit.Append(
                actorUserId, AuditActions.DeviceTokenRevoked, AuditEntityKinds.DeviceToken, device.Id,
                new { Reason = "deactivated" });
        }
    }

    /// <summary>
    /// Expert and garage profiles carry their own active flag per §4; it moves with
    /// app_user.status in the same transaction. Officer/broker profiles have no such
    /// columns. Absent profiles are tolerated (pre-1.3 rows, seeded admin).
    /// </summary>
    private static async Task DeactivateProfile(AppDbContext db, AppUser user, DateTime now, CancellationToken ct)
    {
        switch (user.Role)
        {
            case UserRole.Expert:
                var expert = await db.ExpertProfiles.SingleOrDefaultAsync(p => p.UserId == user.Id, ct);
                if (expert is not null)
                {
                    expert.Active = false;
                    expert.InactivatedAt = now;
                }

                break;
            case UserRole.Garage:
                var garage = await db.GarageProfiles.SingleOrDefaultAsync(p => p.UserId == user.Id, ct);
                if (garage is not null)
                {
                    garage.Active = false;
                    garage.InactivatedAt = now;
                }

                break;
            case UserRole.ClaimOfficer:
            case UserRole.Broker:
            case UserRole.Admin:
            default:
                break;
        }
    }
}
