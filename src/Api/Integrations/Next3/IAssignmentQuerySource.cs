namespace Api.Integrations.Next3;

/// <summary>
/// The query half of the Oracle-poll adapter (design.md §6.2, #34; slice 7.4), kept separate from
/// <see cref="OraclePollAssignmentSource"/> so the poll orchestration can be unit-tested against a
/// fake without ever opening a real Oracle connection — no driver, no live target, anywhere in the
/// test suite (slice 7.4's DoD).
/// </summary>
public interface IAssignmentQuerySource
{
    /// <summary>
    /// Returns every assignment row the query currently finds. Deliberately not required to
    /// exclude rows already seen: <see cref="OraclePollRunner"/> forwards every row it gets to
    /// the subscribed handler unchanged, and <c>Api.Modules.Expert.AssignmentHandler</c>'s
    /// unique-index dedupe (slice 2.1) already makes a replayed row a safe no-op — design.md §6.2.
    /// </summary>
    Task<IReadOnlyList<AssignmentReceived>> PollAsync(CancellationToken ct);
}
