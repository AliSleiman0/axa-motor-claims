using System.Security.Claims;
using Api.Infrastructure;
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
                audit.Append(principal.GetUserId(), AuditActions.UserDeactivated, AuditEntityKinds.AppUser, id);
                // One SaveChanges = one transaction: status flip, profile flag, token
                // revocations, and audit row commit together. A repeat deactivate is a
                // no-op 200 and writes no audit row.
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
