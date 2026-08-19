namespace Api.Modules.Users;

/// <summary>
/// Hybrid ownership per design.md §4: seeded from NEXT3 GET /experts (the seed/sync job is #8,
/// not this slice), app-authoritative for app status. Next3Id is nullable until the mapping is
/// known; uniqueness is enforced by a filtered index (recorded in scope-decisions.md).
/// </summary>
public sealed class ExpertProfile
{
    public Guid UserId { get; set; }
    public string? Next3Id { get; set; }
    public required string Email { get; set; }
    public bool Active { get; set; }
    public DateTime? InactivatedAt { get; set; }
}
