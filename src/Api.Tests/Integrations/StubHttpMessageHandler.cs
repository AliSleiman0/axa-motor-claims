using System.Net;

namespace Api.Tests.Integrations;

/// <summary>
/// A scripted transport for <c>RealNext3Client</c> — the codebase's first, because slice 3.3 is the
/// first thing in it that makes an outbound HTTP call.
///
/// Every request is recorded and answered from a queue of responses, so a test can assert what went
/// on the wire (the auth header, the <c>clientRef</c>, the arrival date NEXT3 would actually receive)
/// as well as what came back. Injected rather than intercepted, which is the same idiom the web tests
/// use for <c>createImageBitmap</c> and <c>MediaRecorder</c>: the seam is a constructor parameter.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    /// <summary>Every request sent, in order — including bodies, which are captured before disposal.</summary>
    public List<RecordedRequest> Requests { get; } = [];

    /// <summary>Queues a scripted response. Consumed in order, one per request.</summary>
    public StubHttpMessageHandler Respond(HttpStatusCode status, string? json = null)
    {
        _responses.Enqueue(_ => new HttpResponseMessage(status)
        {
            Content = json is null
                ? new StringContent(string.Empty)
                : new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        });

        return this;
    }

    /// <summary>Queues a thrown exception — a socket failure, or the timeout the client must reshape.</summary>
    public StubHttpMessageHandler Throws(Exception exception)
    {
        _responses.Enqueue(_ => throw exception);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Read the body *now*: the client disposes the request as soon as SendAsync returns, and a
        // test asserting on a multipart body afterwards would otherwise find a disposed stream.
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);

        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri,
            request.Headers.Authorization?.ToString(),
            request.Headers.TryGetValues("X-Api-Key", out var key) ? string.Join(",", key) : null,
            body,
            request.Content?.GetType().Name));

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException(
                $"StubHttpMessageHandler has no scripted response for request "
                + $"{Requests.Count} ({request.Method} {request.RequestUri}).");
        }

        return _responses.Dequeue()(request);
    }
}

/// <summary>One request as it left the client.</summary>
public sealed record RecordedRequest(
    HttpMethod Method,
    Uri? Uri,
    string? Authorization,
    string? ApiKey,
    string Body,
    string? ContentTypeName);
