namespace Api.Integrations;

/// <summary>
/// What an injected fake failure throws. Transient by contract: the outbox (slice 2.2) must treat it
/// as retryable, which is the whole point of being able to inject it.
/// </summary>
public sealed class FakeTransientException : Exception
{
    public FakeTransientException()
        : base("Injected fake failure.")
    {
    }

    public FakeTransientException(string message)
        : base(message)
    {
    }

    public FakeTransientException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
