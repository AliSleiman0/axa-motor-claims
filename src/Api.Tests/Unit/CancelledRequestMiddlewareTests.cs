using Api.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Api.Tests.Unit;

/// <summary>
/// Slice 7.2's outermost middleware: a request the client abandoned is not a server fault.
/// </summary>
/// <remarks>
/// Unit tests over <see cref="DefaultHttpContext"/> rather than integration tests, because the thing
/// under test is a two-line decision — swallow or rethrow — taken on the state of
/// <c>RequestAborted</c>, and a real client that has genuinely gone away is exactly what
/// <c>TestServer</c> cannot produce. The week-5 browser pass produced it three times in one session;
/// this is that behaviour pinned where it can be pinned.
///
/// **The fact that matters is `AnExceptionWithALiveClient_Propagates`.** The others describe what the middleware does for
/// a caller who has left; only <see cref="AnExceptionWithALiveClient_Propagates"/> says what it must
/// never do for a caller who is still there. Delete the <c>when</c> clause and that one goes red
/// while the first three stay green — which is the asymmetry, and the reason a broad
/// <c>catch (Exception)</c> is defensible here at all.
/// </remarks>
public class CancelledRequestMiddlewareTests
{
    [Fact]
    public async Task AnExceptionAfterTheClientHasGone_IsSwallowed()
    {
        var context = Aborted();

        // A SqlException is what the browser pass actually logged, but the type is deliberately not
        // what the middleware discriminates on: an aborted command surfaces as SqlException or
        // TaskCanceledException depending on where in the round trip the abort lands.
        var exception = await Record.ExceptionAsync(() => Run(context, throwing: new InvalidOperationException()));

        Assert.Null(exception);
    }

    [Fact]
    public async Task AnExceptionAfterTheClientHasGone_Answers499_WhenNothingWasSent()
    {
        var context = Aborted();

        await Run(context, throwing: new InvalidOperationException());

        Assert.Equal(CancelledRequestMiddleware.ClientClosedRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task AnExceptionAfterTheResponseStarted_LeavesTheStatusAlone()
    {
        var context = Aborted();
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());

        await Run(context, throwing: new InvalidOperationException());

        // Rewriting a status after the headers have gone throws — inside the handler whose whole job
        // is to stop things throwing. 200 is also the honest answer: the client received one. The
        // feature's setter throws if it is called at all, so this is a claim about the guard and not
        // only about the value.
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    /// <summary>
    /// **The safety property.** A broad catch is only defensible under the guard; without it this
    /// middleware would swallow every unhandled exception in the application and answer 499 to a
    /// caller who is sitting there waiting — turning real faults into silence, and giving
    /// design.md §9.1's uniform public surface a second thing deciding what it says.
    /// </summary>
    [Fact]
    public async Task AnExceptionWithALiveClient_Propagates()
    {
        var context = new DefaultHttpContext();
        var thrown = new InvalidOperationException("a real fault");

        var caught = await Record.ExceptionAsync(() => Run(context, throwing: thrown));

        Assert.Same(thrown, caught);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode); // untouched default
    }

    [Fact]
    public async Task ARequestThatDoesNotThrow_IsUntouched()
    {
        var context = Aborted();
        var reached = false;

        var middleware = new CancelledRequestMiddleware(_ =>
        {
            reached = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.True(reached);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    private static DefaultHttpContext Aborted()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        return new DefaultHttpContext { RequestAborted = source.Token };
    }

    private static Task Run(HttpContext context, Exception throwing) =>
        new CancelledRequestMiddleware(_ => Task.FromException(throwing)).InvokeAsync(context);

    /// <summary>
    /// A response feature that reports <c>HasStarted</c>. <see cref="DefaultHttpContext"/>'s own
    /// always reports false, and "the headers have already gone" is precisely the case the middleware
    /// has to leave alone.
    /// </summary>
    private sealed class StartedResponseFeature : IHttpResponseFeature
    {
        public Stream Body { get; set; } = Stream.Null;

        public bool HasStarted => true;

        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        public string? ReasonPhrase { get; set; }

        public int StatusCode
        {
            get;
            set => throw new InvalidOperationException("Headers are read-only after the response started.");
        } = StatusCodes.Status200OK;

        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }

        public void OnStarting(Func<object, Task> callback, object state)
        {
        }
    }
}
