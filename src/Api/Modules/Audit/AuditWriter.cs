using System.Text.Encodings.Web;
using System.Text.Json;
using Api.Infrastructure;

namespace Api.Modules.Audit;

/// <summary>
/// The only sanctioned write path to audit_log. Adds, never saves — the row joins the caller's
/// SaveChanges so it commits in the same transaction as the change it records. There is
/// deliberately no update or delete surface; the DB trigger enforces append-only structurally.
/// </summary>
public sealed class AuditWriter(AppDbContext db, TimeProvider time)
{
    // Detail exists for human support queries (§9); the default encoder escapes '+' and
    // similar to \uXXXX, which defeats LIKE searches for phone numbers. The JSON lives in
    // an nvarchar column, never in HTML, so relaxed escaping is safe.
    private static readonly JsonSerializerOptions DetailJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public void Append(Guid? actorUserId, string action, string entityKind, Guid? entityId, object? detail = null) =>
        db.Set<AuditLog>().Add(new AuditLog
        {
            Id = Guid.CreateVersion7(),
            ActorUserId = actorUserId,
            Action = action,
            EntityKind = entityKind,
            EntityId = entityId,
            Detail = detail is null ? null : JsonSerializer.Serialize(detail, DetailJson),
            At = time.GetUtcNow().UtcDateTime,
        });
}
