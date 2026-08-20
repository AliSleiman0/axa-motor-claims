namespace Api.Integrations.Blob;

/// <summary>
/// The transit buffer of design.md §3 and §7.3. Azure Blob is named as a component there but no
/// interface is; this is the sixth port, built to the same contract as the five of §6.2 — a working
/// fake that is selectable by config all the way to handover, so nothing in the build ever blocks on
/// a storage account existing.
///
/// **Transit only.** NEXT3 is the system of record for media (§3, #4). Blobs live for days: the
/// cleanup job deletes one only after its outbox row reads `sent` (§7.3), which is the single rule
/// this whole port exists to make possible.
/// </summary>
public interface IBlobStore
{
    /// <summary>
    /// Writes <paramref name="content"/> under <paramref name="key"/>, overwriting any existing blob.
    /// The stream is consumed as it is written — callers stream a request body straight through here
    /// rather than materialising a file (§7.3's "no buffering into memory").
    /// </summary>
    Task Put(string key, Stream content, string contentType, CancellationToken ct);

    Task<bool> Exists(string key, CancellationToken ct);

    /// <summary>
    /// Opens the stored bytes for reading, or returns null when no such blob exists.
    ///
    /// Added slice 3.3, because until then nothing ever read a blob back: the media pipeline writes
    /// (§7.3) and the cleanup job deletes, and both are satisfied by <see cref="Exists"/> and
    /// <see cref="List"/>. <c>RealNext3Client</c> is the first reader — <c>DocumentPush</c> carries a
    /// blob key rather than bytes (§4's "payload: JSON, blob keys"), so the pushing client is what
    /// fetches the file when it sends it.
    ///
    /// The caller disposes the stream. It is returned rather than copied so the push can stream
    /// straight into its multipart body: a 15 MB photo (`Media:MaxFileMb`) buffered in the outbox
    /// worker would be held for the whole request, times the batch size.
    ///
    /// **Null is not an error here.** §7.3 deletes a blob once its outbox row reads `sent`, so a
    /// missing blob is a real state the caller must decide about — and its decision is that the bytes
    /// are gone and no retry can bring them back.
    /// </summary>
    Task<Stream?> Open(string key, CancellationToken ct);

    /// <summary>Deletes the blob. Returns false when there was nothing there — deletion is idempotent.</summary>
    Task<bool> Delete(string key, CancellationToken ct);

    /// <summary>
    /// Everything under <paramref name="prefix"/>. Used by §7.3's orphan sweep — "a blob without a row
    /// is garbage the cleanup job sweeps" — and by nothing else; this is not a browse surface.
    /// </summary>
    Task<IReadOnlyList<BlobItem>> List(string prefix, CancellationToken ct);
}

/// <summary>
/// One stored blob as the cleanup job sees it. <paramref name="CreatedAt"/> is what makes the orphan
/// grace window (`Retention:OrphanBlobHours`) enforceable: a blob written seconds ago may simply be
/// an upload whose transaction has not committed yet.
/// </summary>
public sealed record BlobItem(string Key, long SizeBytes, DateTime CreatedAt);
