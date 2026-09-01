using Api.Integrations.Next3;
using Microsoft.Extensions.Logging.Abstractions;

namespace Api.Tests.Integrations;

/// <summary>
/// design.md §6.2's Oracle-poll loop body (slice 7.4, #34) — pure unit tests, same weight as
/// <see cref="FakeAssignmentSourceTests"/>: the runner's job is orchestration, not persistence, so
/// no <c>ApiFixture</c>/database is needed. <c>Api.Modules.Expert.AssignmentHandler</c>'s own
/// DB-level dedupe (unique index + insert-then-catch) is already proven by slice 2.1's
/// <c>ExpertAssignmentTests.ReplayedAssignment_IsANoOp_AndDoesNotPushTwice</c> and is not
/// re-proven here — this slice does not change that handler.
/// </summary>
public sealed class OraclePollRunnerTests
{
    [Fact]
    public async Task RunOnce_WithNoRows_DoesNothing()
    {
        var querySource = new FakeAssignmentQuerySource { Rows = [] };
        var received = new List<AssignmentReceived>();
        var runner = CreateRunner(querySource, received);

        var count = await runner.RunOnce(CancellationToken.None);

        Assert.Equal(0, count);
        Assert.Empty(received);
    }

    [Fact]
    public async Task RunOnce_DeliversEveryPolledRow()
    {
        var a = new AssignmentReceived("PLACEHOLDER-VISA-0001", "PLACEHOLDER-EXP-01", "PLACEHOLDER-ASG-01");
        var b = new AssignmentReceived("PLACEHOLDER-VISA-0002", "PLACEHOLDER-EXP-02", "PLACEHOLDER-ASG-02");
        var querySource = new FakeAssignmentQuerySource { Rows = [a, b] };
        var received = new List<AssignmentReceived>();
        var runner = CreateRunner(querySource, received);

        var count = await runner.RunOnce(CancellationToken.None);

        Assert.Equal(2, count);
        Assert.Equal([a, b], received);
    }

    [Fact]
    public async Task RunOnce_TheSameRowAcrossTwoPolls_IsDeliveredBothTimesUnchanged()
    {
        // The reference SQL's window is a rolling SYSDATE - 1, deliberately overlapping across
        // 15-second polls — the runner does no de-duplication itself (design.md §6.2, #34): it
        // hands a replayed row to the subscribed handler exactly as before, and that handler's own
        // idempotency (slice 2.1) is what makes the replay a safe no-op.
        var assignment = new AssignmentReceived("PLACEHOLDER-VISA-0001", "PLACEHOLDER-EXP-01", "PLACEHOLDER-ASG-01");
        var querySource = new FakeAssignmentQuerySource { Rows = [assignment] };
        var received = new List<AssignmentReceived>();
        var runner = CreateRunner(querySource, received);

        await runner.RunOnce(CancellationToken.None);
        await runner.RunOnce(CancellationToken.None);

        Assert.Equal([assignment, assignment], received);
    }

    [Fact]
    public async Task RunOnce_OneRowsHandlerThrows_TheOthersAreStillDelivered()
    {
        var a = new AssignmentReceived("PLACEHOLDER-VISA-0001", "PLACEHOLDER-EXP-01", "PLACEHOLDER-ASG-01");
        var bad = new AssignmentReceived("PLACEHOLDER-VISA-0002", "PLACEHOLDER-EXP-02", "PLACEHOLDER-ASG-02");
        var c = new AssignmentReceived("PLACEHOLDER-VISA-0003", "PLACEHOLDER-EXP-03", "PLACEHOLDER-ASG-03");
        var querySource = new FakeAssignmentQuerySource { Rows = [a, bad, c] };
        var source = new OraclePollAssignmentSource();
        var received = new List<AssignmentReceived>();
        source.Subscribe((assignment, _) =>
        {
            if (assignment == bad)
            {
                throw new InvalidOperationException("simulated delivery failure");
            }

            received.Add(assignment);
            return Task.CompletedTask;
        });
        var runner = new OraclePollRunner(source, querySource, NullLogger<OraclePollRunner>.Instance);

        var count = await runner.RunOnce(CancellationToken.None);

        // The count reflects what the poll found, not what was delivered successfully — RunOnce's
        // own contract is "how many rows this cycle found", matching OutboxProcessor.RunOnce.
        Assert.Equal(3, count);
        Assert.Equal([a, c], received);
    }

    private static OraclePollRunner CreateRunner(
        FakeAssignmentQuerySource querySource, List<AssignmentReceived> received)
    {
        var source = new OraclePollAssignmentSource();
        source.Subscribe((assignment, _) =>
        {
            received.Add(assignment);
            return Task.CompletedTask;
        });

        return new OraclePollRunner(source, querySource, NullLogger<OraclePollRunner>.Instance);
    }
}
