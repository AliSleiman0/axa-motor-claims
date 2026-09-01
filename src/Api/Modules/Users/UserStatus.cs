namespace Api.Modules.Users;

public enum UserStatus
{
    Invited,
    Active,
    Inactive,

    /// <summary>
    /// Login blocked because the NEXT3 master-data sync (#7/#8, slice 7.5) no longer lists this
    /// expert/garage's supplier record as active — distinct from <see cref="Inactive"/> on purpose:
    /// the sync task only ever transitions <c>Active ↔ SyncBlocked</c> and never touches a row
    /// whose status is <see cref="Inactive"/>, so a supplier reappearing in NEXT3's list can never
    /// silently undo an admin's deliberate deactivation of that same person. Every auth gate
    /// already checks `== Active` (an allow-list, not a deny-list on `Inactive`), so this value
    /// blocks login with no change to any of them.
    /// </summary>
    SyncBlocked,
}

public static class UserStatuses
{
    public const string Invited = "invited";
    public const string Active = "active";
    public const string Inactive = "inactive";
    public const string SyncBlocked = "sync_blocked";

    public static string ToDbValue(this UserStatus status) => status switch
    {
        UserStatus.Invited => Invited,
        UserStatus.Active => Active,
        UserStatus.Inactive => Inactive,
        UserStatus.SyncBlocked => SyncBlocked,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static UserStatus FromDbValue(string value) => value switch
    {
        Invited => UserStatus.Invited,
        Active => UserStatus.Active,
        Inactive => UserStatus.Inactive,
        SyncBlocked => UserStatus.SyncBlocked,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };
}
