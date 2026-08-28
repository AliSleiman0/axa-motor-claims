using Api.Modules.Audit;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Integration;

/// <summary>
/// Reading <c>audit_log</c> back, for the §9 completeness tests slice 7.2 adds.
/// </summary>
/// <remarks>
/// Every existing audit assertion opens a context and writes the same three-line query inline, which
/// was fine while there were four of them. This slice adds seven new actions across five files, so
/// the query moves here — in the <c>PushFlows</c> / <c>OutboxFlows</c> shape — rather than being
/// copied seven more times. Nothing existing is rewritten to use it: those tests are green and this
/// is not their slice.
/// </remarks>
internal static class AuditFlows
{
    /// <summary>Every row for one action, oldest first.</summary>
    public static async Task<List<AuditLog>> AuditRows(this ApiFixture fixture, string action)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.Action == action)
            .OrderBy(a => a.At).ThenBy(a => a.Id)
            .ToListAsync();
    }

    /// <summary>Every row for one action against one entity, oldest first.</summary>
    public static async Task<List<AuditLog>> AuditRows(
        this ApiFixture fixture, string action, Guid entityId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.Action == action && a.EntityId == entityId)
            .OrderBy(a => a.At).ThenBy(a => a.Id)
            .ToListAsync();
    }

    /// <summary>
    /// The single row for one action against one entity — the common shape, and it asserts
    /// "exactly one" for free, which is what pins the "registration audits never fire on the refresh
    /// path" rule.
    /// </summary>
    public static async Task<AuditLog> AuditRow(
        this ApiFixture fixture, string action, Guid entityId) =>
        Assert.Single(await fixture.AuditRows(action, entityId));
}
