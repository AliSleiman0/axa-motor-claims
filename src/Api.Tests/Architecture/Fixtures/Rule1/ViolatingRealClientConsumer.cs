using Api.Integrations.Next3;

namespace Api.Tests.Architecture.Fixtures.Rule1;

// Deliberate violation: references RealNext3Client outside the DI-registration namespace.
public sealed class ViolatingRealClientConsumer
{
    public RealNext3Client? Client { get; set; }
}
