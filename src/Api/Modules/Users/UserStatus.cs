namespace Api.Modules.Users;

public enum UserStatus
{
    Invited,
    Active,
    Inactive,
}

public static class UserStatuses
{
    public const string Invited = "invited";
    public const string Active = "active";
    public const string Inactive = "inactive";

    public static string ToDbValue(this UserStatus status) => status switch
    {
        UserStatus.Invited => Invited,
        UserStatus.Active => Active,
        UserStatus.Inactive => Inactive,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static UserStatus FromDbValue(string value) => value switch
    {
        Invited => UserStatus.Invited,
        Active => UserStatus.Active,
        Inactive => UserStatus.Inactive,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };
}
