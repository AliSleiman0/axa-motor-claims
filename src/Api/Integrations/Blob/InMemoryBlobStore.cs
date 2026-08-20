using System.Collections.Concurrent;

namespace Api.Integrations.Blob;

/// <summary>
/// The blob fake (design.md §6.2's fake contract, applied to the sixth port). A deliverable, not a
/// stub: it is what the whole test suite runs on, so <c>dotnet test</c> needs no storage emulator, and
/// it is what a demo runs on when Azurite is not up.
///
/// Singleton, because the bytes must outlive a request and be visible to the cleanup worker.
/// <c>CreatedAt</c> comes from <see cref="TimeProvider"/> rather than the wall clock, which is the
/// only reason §7.3's orphan grace window can be tested on a frozen <c>FakeTimeProvider</c>.
/// </summary>
public sealed class InMemoryBlobStore(TimeProvider time) : IBlobStore
{
    private readonly ConcurrentDictionary<string, StoredBlob> _blobs = new(StringComparer.Ordinal);

    /// <summary>Keys currently held — test observability, not part of the interface.</summary>
    internal IReadOnlyCollection<string> Keys => [.. _blobs.Keys];

    public async Task Put(string key, Stream content, string contentType, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);

        // Copied eagerly and only then recorded: a Put that throws part-way (the byte cap tripping
        // mid-stream) must leave nothing behind, exactly as a half-written blob would be discarded.
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);

        _blobs[key] = new StoredBlob(buffer.ToArray(), contentType, time.GetUtcNow().UtcDateTime);
    }

    public Task<bool> Exists(string key, CancellationToken ct) =>
        Task.FromResult(_blobs.ContainsKey(key));

    public Task<Stream?> Open(string key, CancellationToken ct) =>
        // A fresh MemoryStream per call, never a shared one: two outbox workers may push different
        // documents concurrently, and a stream handed to both would have one position between them.
        Task.FromResult<Stream?>(
            _blobs.TryGetValue(key, out var blob) ? new MemoryStream(blob.Content, writable: false) : null);

    public Task<bool> Delete(string key, CancellationToken ct) =>
        Task.FromResult(_blobs.TryRemove(key, out _));

    public Task<IReadOnlyList<BlobItem>> List(string prefix, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        IReadOnlyList<BlobItem> items =
        [
            .. _blobs
                .Where(entry => entry.Key.StartsWith(prefix, StringComparison.Ordinal))
                .Select(entry => new BlobItem(entry.Key, entry.Value.Content.Length, entry.Value.CreatedAt))
                .OrderBy(item => item.Key, StringComparer.Ordinal),
        ];

        return Task.FromResult(items);
    }

    /// <summary>The stored bytes — test observability for "was this actually the file we sent?".</summary>
    internal byte[]? Read(string key) => _blobs.TryGetValue(key, out var blob) ? blob.Content : null;

    private sealed record StoredBlob(byte[] Content, string ContentType, DateTime CreatedAt);
}
