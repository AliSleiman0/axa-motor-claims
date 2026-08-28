namespace Api.Infrastructure;

/// <summary>
/// The ceiling on how many rows any list endpoint returns (slice 7.2).
/// </summary>
/// <remarks>
/// <para>
/// Every worklist in the product was unbounded: E1, G1, O1, B1, the four admin profile lists and A2's
/// queue all ordered and projected the whole table. At #20's working volumes (~100 claims a day) that
/// is invisible; during the NEXT3 outage A2 exists for, or a year into a garage's declarations, it is
/// a screen that serialises thousands of rows — with error text on each — into a browser that will
/// then render all of them. The failure mode is the worst kind: nothing throws, the screen simply
/// stops arriving, on the day somebody is trying to find out where a photograph went.
/// </para>
/// <para>
/// **A <c>const</c>, not a configuration key.** design.md Appendix A's placeholder rule covers values
/// AXA owns; how many rows a screen may return is not one, and the existing house precedent is the
/// same — <c>DocumentBlobSweeper.BatchSize</c>, <c>ExpertEndpoints.MaxSearchTermLength</c> and
/// <c>MediaUploadService.MaxFieldValueBytes</c> are all consts. Making it tunable would mean an
/// operator could raise it past what the client can render without anybody reviewing the screen.
/// </para>
/// <para>
/// **What a cap does not buy: paging.** Beyond 200 rows a list is silently truncated, and no screen
/// says so. That is deliberate for one week (scope-decisions, slice 7.2) and rests on A2's own
/// argument from 6.2 — a page showing 200 of 3,000 rows above a **Retry all** button that retried all
/// 3,000 would be more dangerous than the truncation. Revisit with #20's real volumes.
/// </para>
/// </remarks>
public static class ListLimits
{
    /// <summary>Rows any single list endpoint may return.</summary>
    public const int MaxRows = 200;
}
