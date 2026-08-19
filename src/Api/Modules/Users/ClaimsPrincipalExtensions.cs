using System.Security.Claims;

namespace Api.Modules.Users;

public static class ClaimsPrincipalExtensions
{
    /// <summary>Parses the raw "sub" claim (inbound mapping is off). Null when absent or malformed.</summary>
    public static Guid? GetUserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirst("sub")?.Value, out var userId) ? userId : null;
}
