using Api.Integrations.Next3;

namespace Api.Tests.Architecture.Fixtures.Rule3;

// Deliberate violation: calls an INext3Client push operation outside the outbox namespace.
public sealed class ViolatingPushCaller(INext3Client client)
{
    public Task PushOutsideOutbox() =>
        client.RecordArrival(
            "PLACEHOLDER-VISA-0000",
            new ArrivalInfo(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero), 0, 0),
            "PLACEHOLDER-REF",
            CancellationToken.None);
}
