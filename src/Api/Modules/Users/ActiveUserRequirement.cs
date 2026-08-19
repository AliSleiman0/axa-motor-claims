using Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Users;

/// <summary>
/// §9: tokens are invalidated on deactivation. A live JWT dies on its next request because
/// every policy re-checks that the user is still active (no caching — that is the point).
/// </summary>
public sealed class ActiveUserRequirement : IAuthorizationRequirement
{
}

public sealed class ActiveUserHandler(AppDbContext db) : AuthorizationHandler<ActiveUserRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ActiveUserRequirement requirement)
    {
        var sub = context.User.FindFirst("sub")?.Value;
        if (Guid.TryParse(sub, out var userId)
            && await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.Status == UserStatus.Active))
        {
            context.Succeed(requirement);
        }
    }
}
