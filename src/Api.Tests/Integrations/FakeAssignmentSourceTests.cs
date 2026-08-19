using Api.Integrations.Next3;

namespace Api.Tests.Integrations;

public sealed class FakeAssignmentSourceTests
{
    [Fact]
    public async Task Inject_DeliversToSubscribedHandler()
    {
        var source = new FakeAssignmentSource();
        var received = new List<AssignmentReceived>();
        source.Subscribe((a, _) =>
        {
            received.Add(a);
            return Task.CompletedTask;
        });

        var assignment = new AssignmentReceived("PLACEHOLDER-VISA-0001", "PLACEHOLDER-EXP-01", "PLACEHOLDER-ASG-01");
        await source.Inject(assignment, CancellationToken.None);

        Assert.Equal(assignment, Assert.Single(received));
    }

    [Fact]
    public async Task Inject_WithoutSubscriber_Throws()
    {
        // Fails loud rather than swallowing: an assignment that vanishes silently looks like a
        // working demo right up until someone asks why the expert never got the popup.
        var source = new FakeAssignmentSource();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            source.Inject(
                new AssignmentReceived("PLACEHOLDER-VISA-0001", "PLACEHOLDER-EXP-01", "PLACEHOLDER-ASG-01"),
                CancellationToken.None));
    }
}
