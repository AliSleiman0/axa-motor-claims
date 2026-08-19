using Api.Integrations.Next3;

namespace Api.Tests.Architecture.Fixtures.PublicModule;

// Deliberate violation: a "public module" type referencing INext3Client.
public sealed class ViolatingPublicType
{
    public INext3Client? Next3 { get; set; }
}
