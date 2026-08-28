namespace Api.Infrastructure;

/// <summary>
/// A request the client abandoned is not a server fault, and since slice 7.2 it stops being logged
/// as one.
/// </summary>
/// <remarks>
/// <para>
/// The week-5 browser pass (finding 10) caught three <c>503 → 200</c> pairs on the broker's document
/// reads, each with an unhandled <c>Microsoft.Data.SqlClient.SqlException: A severe error occurred on
/// the current command. Operation cancelled by user.</c> in the log. The cause is ordinary: react-query
/// passes an <see cref="System.Threading.CancellationToken"/> through to <c>fetch</c>, React
/// StrictMode double-mounts in development, and the first mount's request is aborted mid-query. A user
/// navigating away from a slow screen does exactly the same thing in production. The harm is not the
/// status code nobody receives — it is that a real fault is now indistinguishable from a person
/// changing their mind, in the one place somebody looks when something has gone wrong.
/// </para>
/// <para>
/// **The broad catch is safe only under the guard, and the guard is not the exception's type.** An
/// aborted <c>SqlCommand</c> surfaces as <c>SqlException</c> or <c>TaskCanceledException</c>
/// interchangeably depending on where in the round trip the abort lands, and an <c>HttpClient</c>
/// reports its own timeout as a <c>TaskCanceledException</c> that has nothing to do with the caller —
/// so a filter naming either type would both miss cases it should catch and swallow ones it must not.
/// <see cref="HttpContext.RequestAborted"/> answers the only question that matters: is anybody still
/// there to receive an answer? CLAUDE.md records this class of mistake twice, in two adapters.
/// </para>
/// <para>
/// **This is deliberately not a global exception handler.** With a live client the exception
/// propagates exactly as it does today. That matters beyond taste: design.md §9.1 requires every
/// refusal on <c>/public/*</c> to be indistinguishable, and a handler that turned faults into a
/// uniform body would be a second thing deciding what that surface says. Consequence recorded rather
/// than closed — slice 7.1's <c>/security-review</c> noted that an app-faulted 500 carries no security
/// headers, because Kestrel skips <c>OnStarting</c> callbacks when the application threw. That is
/// still true after this slice, and its fix is the global handler this one declines to add.
/// </para>
/// <para>
/// **Registered outermost**, above <see cref="SecurityHeadersMiddleware"/>, so it covers the whole
/// pipeline including the rate limiter. The 499 it writes still carries §9's headers: the inner
/// middleware's <c>OnStarting</c> callback is registered before anything throws, and the response is
/// then started normally by the server rather than being abandoned as a fault.
/// </para>
/// <para>
/// 499 is nginx's <c>client closed request</c> — not an IANA code, and chosen for exactly that reason:
/// it is a log line, not a contract. Nobody receives it; the socket is already gone. It is written
/// only when nothing has been sent yet, because a response that has begun cannot have its status
/// rewritten and pretending otherwise would throw inside the handler that exists to stop throwing.
/// </para>
/// </remarks>
public sealed class CancelledRequestMiddleware(RequestDelegate next)
{
    /// <summary>The status stamped on an abandoned request. See the remarks: a log line, not a contract.</summary>
    public const int ClientClosedRequest = 499;

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            await next(context);
        }
        catch (Exception) when (context.RequestAborted.IsCancellationRequested)
        {
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = ClientClosedRequest;
            }
        }
    }
}
