namespace Api.Modules.Users;

public enum UserRole
{
    Expert,
    Garage,
    ClaimOfficer,
    Broker,
    Admin,
}

/// <summary>The §4 nvarchar enum values — the single source for role strings in DB, JWT claim, and policies.</summary>
public static class UserRoles
{
    public const string Expert = "expert";
    public const string Garage = "garage";
    public const string ClaimOfficer = "claim_officer";
    public const string Broker = "broker";
    public const string Admin = "admin";

    public static string ToDbValue(this UserRole role) => role switch
    {
        UserRole.Expert => Expert,
        UserRole.Garage => Garage,
        UserRole.ClaimOfficer => ClaimOfficer,
        UserRole.Broker => Broker,
        UserRole.Admin => Admin,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
    };

    public static UserRole FromDbValue(string value) => value switch
    {
        Expert => UserRole.Expert,
        Garage => UserRole.Garage,
        ClaimOfficer => UserRole.ClaimOfficer,
        Broker => UserRole.Broker,
        Admin => UserRole.Admin,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };
}
