using Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Users;

/// <summary>
/// Deactivation is pulled forward from slice 1.3 because the 1.2 DoD requires
/// deactivate-blocks-login end-to-end via the API. 1.3 grows this into the full A1 CRUD.
/// </summary>
public static class AdminUserEndpoints
{
    public static IEndpointRouteBuilder MapAdminUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/users").RequireAuthorization(AuthPolicies.Admin);

        group.MapPost("/{id:guid}/deactivate", async (
            Guid id, AppDbContext db, TokenService tokens, TimeProvider time, CancellationToken ct) =>
        {
            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, ct);
            if (user is null)
            {
                return Results.NotFound();
            }

            if (user.Status != UserStatus.Inactive)
            {
                user.Status = UserStatus.Inactive;
                user.InactivatedAt = time.GetUtcNow().UtcDateTime;
                await tokens.RevokeAll(id, ct);
                // One SaveChanges = one transaction: status flip + token revocations commit together.
                await db.SaveChangesAsync(ct);
            }

            return Results.Ok();
        });

        return app;
    }
}
