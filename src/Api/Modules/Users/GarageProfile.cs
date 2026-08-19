namespace Api.Modules.Users;

public sealed class GarageProfile
{
    public Guid UserId { get; set; }
    public required string ContactName { get; set; }
    public string? Phone { get; set; }
    public string? Mobile { get; set; }
    public required string Email { get; set; }
    public string? Next3Id { get; set; }
    public string? Address { get; set; }

    /// <summary>Display text per design.md §4, not structured.</summary>
    public string? OpeningHours { get; set; }

    public bool Active { get; set; }
    public DateTime? InactivatedAt { get; set; }
}
