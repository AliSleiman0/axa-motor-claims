namespace Api.Modules.Users;

public sealed class AppUser
{
    public Guid Id { get; set; }
    public required string Phone { get; set; }
    public UserRole Role { get; set; }
    public required string DisplayName { get; set; }
    public UserStatus Status { get; set; }
    public DateTime? InactivatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
