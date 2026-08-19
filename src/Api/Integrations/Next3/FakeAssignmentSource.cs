namespace Api.Integrations.Next3;

/// <summary>
/// Injects synthetic assignments on demand (design.md §6.2) — this is what drives the week-4 demo
/// and every expert-flow test until NEXT3 can call us for real.
///
/// Deliberately no failure injection: nothing retries assignment delivery, so a dropped assignment
/// has no downstream behaviour to exercise. The interesting failure mode is a *replayed* assignment,
/// and Inject already covers it — call it twice with the same ref (slice 2.1's dedupe test).
/// </summary>
public sealed class FakeAssignmentSource : IAssignmentSource
{
    private Func<AssignmentReceived, CancellationToken, Task>? _handler;

    public void Subscribe(Func<AssignmentReceived, CancellationToken, Task> handler) => _handler = handler;

    /// <summary>Delivers an assignment as if NEXT3 had sent it.</summary>
    public Task Inject(AssignmentReceived assignment, CancellationToken ct)
    {
        // Fail loud: an injected assignment that silently goes nowhere would look like a working
        // demo right up until someone asks why the expert never got the popup.
        var handler = _handler
            ?? throw new InvalidOperationException(
                "No assignment handler subscribed. The handler is registered in slice 2.1.");

        return handler(assignment, ct);
    }
}
