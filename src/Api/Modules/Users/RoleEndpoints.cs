namespace Api.Modules.Users;

/// <summary>
/// The five role-gated endpoint groups (§2 matrix, §9 authorization). Modules add their real
/// endpoints to these prefixes in later slices; the ping probe makes the policies testable now.
/// </summary>
public static class RoleEndpoints
{
    public static IEndpointRouteBuilder MapRoleEndpoints(this IEndpointRouteBuilder app)
    {
        MapRoleGroup(app, "/api/expert", AuthPolicies.Expert);
        MapRoleGroup(app, "/api/garage", AuthPolicies.Garage);
        MapRoleGroup(app, "/api/officer", AuthPolicies.ClaimOfficer);
        MapRoleGroup(app, "/api/broker", AuthPolicies.Broker);
        MapRoleGroup(app, "/api/admin", AuthPolicies.Admin);
        return app;
    }

    private static void MapRoleGroup(IEndpointRouteBuilder app, string prefix, string policy)
    {
        var group = app.MapGroup(prefix).RequireAuthorization(policy);
        group.MapGet("/ping", () => Results.Ok());
    }
}
