namespace Api.Integrations.Next3;

/// <summary>
/// NEXT3 understood the request and refused it — a 4xx that is not going to become a 2xx by waiting.
///
/// This type exists because of how the outbox classifies failures (§6.3). `OutboxProcessor` retries
/// <c>HttpRequestException</c> and <c>TimeoutException</c> on the §6.3 schedule and sends everything
/// else straight to `failed`, and the full schedule takes **26 h 36 m** to exhaust. So a malformed
/// payload, an unknown visa or a bad credential must not be raised as an <c>HttpRequestException</c>:
/// it would sit in the queue for over a day before appearing on A2, where the person who can actually
/// fix it is looking. Landing on A2 within seconds is the point, and A2's Retry makes it reversible.
///
/// It is its own type rather than a subclass of <c>FakeTransientException</c> (which is sealed, and is
/// the fake's own marker anyway) — and it must never be one of the transient types, or the
/// classification inverts silently.
/// </summary>
public sealed class Next3RejectedException : Exception
{
    public Next3RejectedException()
        : base("NEXT3 rejected the request.")
    {
    }

    public Next3RejectedException(string message)
        : base(message)
    {
    }

    public Next3RejectedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public Next3RejectedException(int statusCode, string operation, string? responseSnippet)
        : base(Describe(statusCode, operation, responseSnippet)) => StatusCode = statusCode;

    /// <summary>The HTTP status NEXT3 answered with, or null when the failure was not a response.</summary>
    public int? StatusCode { get; }

    /// <summary>
    /// The message the outbox row keeps. `last_error` is what A2 shows and what someone reads at
    /// handover, so it carries the status, the operation and a slice of the body — enough to tell
    /// "unknown visa" from "bad document type" without opening a log aggregator.
    /// </summary>
    private static string Describe(int statusCode, string operation, string? responseSnippet) =>
        string.IsNullOrWhiteSpace(responseSnippet)
            ? $"NEXT3 rejected {operation} with status {statusCode}."
            : $"NEXT3 rejected {operation} with status {statusCode}: {responseSnippet}";
}
