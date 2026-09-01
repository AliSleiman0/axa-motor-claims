namespace Api.Integrations.Next3;

/// <summary>
/// The `IAssignmentSource` NEXT3's client answer actually specified (#34, resolved 2026-08-31):
/// the client will not build a webhook, so this is delivered by <see cref="OraclePollWorker"/>
/// polling <see cref="IAssignmentQuerySource"/> on a timer and handing every row it finds to this
/// class, rather than by NEXT3 calling in.
///
/// Same shape as <see cref="FakeAssignmentSource"/> deliberately: both are "a handler gets
/// subscribed once, deliveries are dispatched to it later" — the only difference is what drives
/// the delivery (an admin's `POST` versus a poll loop). Dedupe on `next3_assignment_ref` is not
/// this class's job; it lives entirely in the subscribed handler (design.md §6.2, slice 2.1), so a
/// row the poll query returns twice is simply delivered twice — see <see cref="OraclePollRunner"/>.
/// </summary>
public sealed class OraclePollAssignmentSource : IAssignmentSource
{
    private Func<AssignmentReceived, CancellationToken, Task>? _handler;

    public void Subscribe(Func<AssignmentReceived, CancellationToken, Task> handler) => _handler = handler;

    /// <summary>Called by <see cref="OraclePollRunner"/> for each row a poll cycle finds.</summary>
    public Task Deliver(AssignmentReceived assignment, CancellationToken ct)
    {
        var handler = _handler
            ?? throw new InvalidOperationException(
                "No assignment handler subscribed. The handler is registered in slice 2.1.");

        return handler(assignment, ct);
    }
}
