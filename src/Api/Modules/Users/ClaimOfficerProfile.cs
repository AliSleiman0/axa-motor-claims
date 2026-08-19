namespace Api.Modules.Users;

public sealed class ClaimOfficerProfile
{
    public Guid UserId { get; set; }
    public required string Next3User { get; set; }
    public required string Email { get; set; }
}
