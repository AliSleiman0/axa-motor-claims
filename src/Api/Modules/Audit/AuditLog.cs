namespace Api.Modules.Audit;

/// <summary>
/// Append-only audit trail row (design.md §4/§9). ActorUserId null = public customer or system.
/// EntityId is nullable beyond §4's letter: a failed login for an unknown phone has no entity
/// (recorded in scope-decisions.md); the attempted phone lives in Detail.
/// </summary>
public sealed class AuditLog
{
    public Guid Id { get; set; }
    public Guid? ActorUserId { get; set; }
    public required string Action { get; set; }
    public required string EntityKind { get; set; }
    public Guid? EntityId { get; set; }
    public string? Detail { get; set; }
    public DateTime At { get; set; }
}
