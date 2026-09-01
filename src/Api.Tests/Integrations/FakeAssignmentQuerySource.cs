using Api.Integrations.Next3;

namespace Api.Tests.Integrations;

/// <summary>
/// Test double for <see cref="IAssignmentQuerySource"/> — not production code, since the only real
/// caller of this port outside tests is <see cref="OracleAssignmentQuerySource"/>, which slice 7.4
/// deliberately leaves unexercised (no Oracle driver, no live connection, anywhere in this suite).
///
/// <see cref="Rows"/> is returned verbatim by every <see cref="PollAsync"/> call rather than being
/// cleared or advanced automatically, so a test can simulate the same row appearing on two
/// consecutive polls simply by not changing it between calls — the honest shape of the reference
/// SQL's rolling `SYSDATE - 1` window (design.md §6.2).
/// </summary>
public sealed class FakeAssignmentQuerySource : IAssignmentQuerySource
{
    public IReadOnlyList<AssignmentReceived> Rows { get; set; } = [];

    public int PollCount { get; private set; }

    public Task<IReadOnlyList<AssignmentReceived>> PollAsync(CancellationToken ct)
    {
        PollCount++;
        return Task.FromResult(Rows);
    }
}
