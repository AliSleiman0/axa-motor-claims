using Microsoft.Extensions.Logging;

namespace Api.Tests.Architecture.Fixtures.Rule5;

// Deliberate violation: a "public module" type holding a path to a log sink.
public sealed class ViolatingPublicLogger
{
    public ILogger<ViolatingPublicLogger>? Log { get; set; }
}
