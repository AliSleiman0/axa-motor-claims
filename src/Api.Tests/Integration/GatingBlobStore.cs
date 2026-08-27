using Api.Integrations.Blob;

namespace Api.Tests.Integration;

/// <summary>
/// A pass-through <see cref="IBlobStore"/> that can be asked, once, to stop inside a
/// <see cref="IBlobStore.Put"/> so a test can commit something else underneath an upload that is
/// already past its authorization checks (slice 7.1).
/// </summary>
/// <remarks>
/// <para>
/// **Inert unless armed** — <see cref="GateNextPutUnder"/> is the only thing that changes its
/// behaviour, and it arms exactly one call under exactly one key prefix. Same shape and same reason
/// as <see cref="RemoteIpTestFilter"/>: a seam the test host needs and production never sees.
/// </para>
/// <para>
/// **It exists because the obvious approach was measured and does not work.** Slice 7.1's card asks
/// for a gated multipart body that stalls the request between its metadata parts and its file part.
/// That was built, and it stalls nothing: <c>TestServer</c>'s client handler materialises the whole
/// request body before it dispatches the request, so the gate opened and closed entirely on the
/// client while the server had not started. The upload then answered 404 from <c>Resolve</c> — the
/// same status code the test asserts, reached without the concurrency token ever being consulted, so
/// the test would have passed while proving nothing. Two measurements pinned it: the response had not
/// arrived when the gate opened (so it *looked* in flight), and no blob was ever written (so the
/// handler had not run). Adding a megabyte of body to force pipe backpressure changed neither — the
/// gate still opened in 0 ms.
/// </para>
/// <para>
/// Stopping in <c>Put</c> is in any case the more exact place. §5.3's window is between the handler
/// reading <c>broker_request.state</c> and its <c>SaveChanges</c>; <c>MediaUploadService</c> writes
/// the blob inside that window and commits after it, so a gate here has the token resolved, the file
/// cap checked, the bucket gate passed, <c>IsModified</c> set, and nothing yet written.
/// </para>
/// </remarks>
internal sealed class GatingBlobStore(InMemoryBlobStore inner) : IBlobStore
{
    private BlobPutGate? _armed;

    /// <summary>
    /// Stalls the next <c>Put</c> whose key starts with <paramref name="prefix"/>. Blob keys are
    /// <c>{ownerKind}/{ownerId:N}/{documentId:N}{ext}</c>, so a prefix scopes the gate to one owner
    /// and cannot be tripped by whatever else the serialized collection is doing.
    /// </summary>
    public BlobPutGate GateNextPutUnder(string prefix)
    {
        var gate = new BlobPutGate(prefix);
        _armed = gate;
        return gate;
    }

    public async Task Put(string key, Stream content, string contentType, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);

        // Claimed atomically so the gate is one-shot even if two uploads race into it.
        var armed = _armed;
        if (armed is not null
            && key.StartsWith(armed.Prefix, StringComparison.Ordinal)
            && ReferenceEquals(Interlocked.CompareExchange(ref _armed, null, armed), armed))
        {
            armed.SignalReached();
            await armed.Released;
        }

        await inner.Put(key, content, contentType, ct);
    }

    public Task<bool> Exists(string key, CancellationToken ct) => inner.Exists(key, ct);

    public Task<Stream?> Open(string key, CancellationToken ct) => inner.Open(key, ct);

    public Task<bool> Delete(string key, CancellationToken ct) => inner.Delete(key, ct);

    public Task<IReadOnlyList<BlobItem>> List(string prefix, CancellationToken ct) =>
        inner.List(prefix, ct);
}

/// <summary>One armed stall: wait for <see cref="Reached"/>, do something, then <see cref="Release"/>.</summary>
internal sealed class BlobPutGate(string prefix) : IDisposable
{
    private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal string Prefix => prefix;

    /// <summary>Completes when the upload is inside <c>Put</c> and holding.</summary>
    public Task Reached => _reached.Task;

    internal Task Released => _released.Task;

    internal void SignalReached() => _reached.TrySetResult();

    public void Release() => _released.TrySetResult();

    /// <summary>A test that throws before releasing must not leave a request parked for ever.</summary>
    public void Dispose() => Release();
}
