namespace Api.Modules.Users;

/// <summary>One policy per role (§2 matrix), applied at endpoint-group level, plus the shared active-user policy.</summary>
public static class AuthPolicies
{
    public const string Expert = "Expert";
    public const string Garage = "Garage";
    public const string ClaimOfficer = "ClaimOfficer";
    public const string Broker = "Broker";
    public const string Admin = "Admin";
    public const string ActiveUser = "ActiveUser";
}
