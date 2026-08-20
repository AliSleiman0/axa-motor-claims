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
