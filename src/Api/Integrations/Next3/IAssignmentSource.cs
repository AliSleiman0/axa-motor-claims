namespace Api.Integrations.Next3;

/// <summary>
/// NEXT3 assigned a claim to an expert (design.md §6.2). NEXT3AssignmentRef is the dedupe key: a
/// replayed webhook or an overlapping poll must be a no-op in the handler, not a second assignment.
/// </summary>
public sealed record AssignmentReceived(string VisaNo, string ExpertNext3Id, string Next3AssignmentRef);

/// <summary>
/// The inbound half of the NEXT3 boundary (design.md §6.2). Whether assignments arrive by webhook or
/// by polling is #34; the whole point of this interface is that answering it flips a config value and
/// swaps one adapter, and nothing downstream changes.
///
/// Slice 1.4 builds the interface and the fake only. The single idempotent handler that subscribes is
/// slice 2.1; the webhook receiver and poller land when #34 is answered.
/// </summary>
public interface IAssignmentSource
{
    /// <summary>Registers the single handler assignments are delivered to.</summary>
    void Subscribe(Func<AssignmentReceived, CancellationToken, Task> handler);
}
