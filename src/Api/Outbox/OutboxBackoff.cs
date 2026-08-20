namespace Api.Outbox;

/// <summary>
/// design.md §6.3's retry schedule: 1 min → 5 min → 30 min → 2 h → 6 h …, with the ceiling as a
/// config knob to be re-tuned when #33 (NEXT3 maintenance windows) is answered.
///
/// Pure: no clock, no database, no config object. The schedule is one of the four things CLAUDE.md
/// requires to be written test-first, and this is the shape that makes that possible.
/// </summary>
public static class OutboxBackoff
{
    // Indexed by attempt number: the delay applied *after* the Nth failure. Anything beyond the
    // table repeats the last entry, which the ceiling then caps.
    private static readonly TimeSpan[] Schedule =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2),
    ];

    /// <summary>How long to wait after <paramref name="attempts"/> failed attempts.</summary>
    /// <param name="attempts">The attempt that just failed, 1-based (the dequeue increments it).</param>
    /// <param name="ceilingHours">§6.3's backoff ceiling; every delay is capped at it.</param>
    public static TimeSpan Delay(int attempts, int ceilingHours)
    {
        // The dequeue increments attempts before the worker sees the row, so anything below 1 means
        // a caller has lost track of which attempt actually failed.
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(ceilingHours, 1);

        var ceiling = TimeSpan.FromHours(ceilingHours);
        var delay = attempts <= Schedule.Length ? Schedule[attempts - 1] : ceiling;

        // The ceiling caps the table too, not only the tail: if #33 answers that NEXT3 is only ever
        // briefly unavailable, lowering the ceiling has to mean "retry faster" from attempt one.
        return delay <= ceiling ? delay : ceiling;
    }
}
