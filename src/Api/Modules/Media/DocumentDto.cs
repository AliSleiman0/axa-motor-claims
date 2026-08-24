namespace Api.Modules.Media;

/// <summary>
/// A document as any screen sees it. There is no blob URL: §9 allows blob reads through short-lived
/// SAS only, and where bytes genuinely need to be read back — the officer reviewing a garage's
/// documents (§5.2) — slice 4.2 streams them through an authorized endpoint instead.
///
/// It lives in the media module rather than beside one module's endpoints (moved from
/// <c>Api.Modules.Expert</c> in slice 4.1): the expert, garage and officer surfaces all describe the
/// same row, and the alternative was either a second near-identical record or a declaration module
/// that references the expert module to talk about a photograph.
/// </summary>
/// <param name="PushConfirmed">
/// **NEXT3 has acknowledged this document** (slice 5.1). Not the same question as
/// <paramref name="PushStatus"/>, which says only whether the row takes part in the pipeline at all:
/// a `queued` document may be waiting for the worker's next tick, mid-retry, or long since delivered,
/// and a garage watching its invoice needs to know which. Computed at read time by joining
/// <c>OutboxSentQuery</c> rather than stored, because §4 keeps live push state on the outbox row
/// alone — a second copy of it is a second answer that can disagree with the one §7.3 deletes blobs
/// on. False on a freshly created row by definition.
/// </param>
/// <param name="BlobRetained">
/// False once §7.3's sweep has deleted the bytes. The row outlives its blob by design — NEXT3 is the
/// system of record from the moment the push lands.
/// </param>
public sealed record DocumentDto(
    Guid Id,
    string Bucket,
    string? DocType,
    string Origin,
    string ClarityResult,
    string ContentType,
    string? FileName,
    long SizeBytes,
    string PushStatus,
    bool PushConfirmed,
    bool BlobRetained,
    DateTime CreatedAt)
{
    public static DocumentDto From(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new DocumentDto(
            document.Id,
            document.Bucket,
            document.DocType,
            document.Origin,
            document.ClarityResult,
            document.ContentType,
            document.FileName,
            document.SizeBytes,
            document.PushStatus,
            // A document this call has just created cannot have been pushed yet: the worker has not
            // run, and on the deferred path there is not even a row for it to run against.
            false,
            document.BlobDeletedAt is null,
            document.CreatedAt);
    }
}
