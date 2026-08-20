using Api.Outbox;

namespace Api.Tests.Architecture.Fixtures.Rule4;

// A deliberate violation of rule 4: a type outside Api.Outbox holding an outbox row directly instead
// of going through OutboxWriter. Exists so the rule is proven to detect one.
internal sealed class ViolatingOutboxRowWriter
{
    public Next3OutboxMessage? Queued { get; set; }
}
