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
            document.BlobDeletedAt is null,
            document.CreatedAt);
    }
}
