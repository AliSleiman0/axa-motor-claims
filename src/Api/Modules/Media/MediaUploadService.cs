using System.Buffers;
using System.Text;
using Api.Infrastructure;
using Api.Integrations.Blob;
using Api.Integrations.Next3;
using Api.Modules.Audit;
using Api.Outbox;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Api.Modules.Media;

/// <summary>Where an upload is going: the owning row, its visa, and who is uploading.</summary>
/// <param name="ActorUserId">Null for the unauthenticated Option 2 customer (§5.3).</param>
public sealed record MediaUploadTarget(string OwnerKind, Guid OwnerId, string VisaNo, Guid? ActorUserId);

/// <summary>Either a created document or a refusal with the code the client branches on.</summary>
public sealed record MediaUploadOutcome(int StatusCode, string? ErrorCode, Document? Document)
{
    public static MediaUploadOutcome Refused(int statusCode, string errorCode) =>
        new(statusCode, errorCode, null);

    public static MediaUploadOutcome Created(Document document) =>
        new(StatusCodes.Status201Created, null, document);
}

/// <summary>
/// design.md §7's server pipeline, in the order §7.3 specifies:
///
/// <code>
/// captured/selected → clarity pass → confirmed
///    → [one transaction: document row + outbox row]  +  blob PUT (transit container)
/// </code>
///
/// **The blob PUT happens outside the DB transaction, and first.** §7.3 spells out why that is the
/// safe order: "a blob without a row is garbage the cleanup job sweeps; a row without a blob is an
/// error surfaced at push time". So a crash after the PUT costs a stray file the orphan sweep will
/// collect, while the reverse order would queue a NEXT3 push naming bytes that do not exist — which
/// reaches AXA as a missing photo, i.e. the problem this project exists to solve.
///
/// **Nothing is buffered.** The request body is read section by section with
/// <see cref="MultipartReader"/> and streamed straight into the blob store; only a bounded
/// <see cref="MediaValidation.HeaderPrefixBytes"/> header peek is held in memory, because §7.2 item 5
/// requires dimensions to be checked *before* the bytes leave for storage. The consequence is a
/// contract: **the metadata parts must precede the file part**, since a bucket learned after the file
/// has been written cannot decide whether the file was allowed.
/// </summary>
public sealed class MediaUploadService(
    AppDbContext db,
    IBlobStore blobs,
    OutboxWriter outbox,
    AuditWriter audit,
    IOptionsMonitor<MediaOptions> mediaOptions,
    IOptionsMonitor<ClarityOptions> clarityOptions,
    IOptions<Next3Options> next3Options,
    TimeProvider time)
{
    private const string BucketField = "bucket";
    private const string OriginField = "origin";
    private const int MaxFieldValueBytes = 256;

    public async Task<MediaUploadOutcome> Upload(
        HttpRequest request, MediaUploadTarget target, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(target);

        var boundary = ReadBoundary(request);
        if (boundary is null)
        {
            return MediaUploadOutcome.Refused(StatusCodes.Status415UnsupportedMediaType, "not_multipart");
        }

        var reader = new MultipartReader(boundary, request.Body);
        string? bucketName = null;
        string? origin = null;

        for (var section = await reader.ReadNextSectionAsync(ct);
             section is not null;
             section = await reader.ReadNextSectionAsync(ct))
        {
            if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition))
            {
                return MediaUploadOutcome.Refused(StatusCodes.Status400BadRequest, "malformed_part");
            }

            if (!disposition.DispositionType.Equals("form-data", StringComparison.OrdinalIgnoreCase))
            {
                return MediaUploadOutcome.Refused(StatusCodes.Status400BadRequest, "malformed_part");
            }

            var isFile = disposition.FileName.HasValue || disposition.FileNameStar.HasValue;
            if (!isFile)
            {
                var name = disposition.Name.Value;
                var value = await ReadSmallValue(section.Body, ct);
                if (value is null)
                {
                    return MediaUploadOutcome.Refused(StatusCodes.Status400BadRequest, "field_too_long");
                }

                if (string.Equals(name, BucketField, StringComparison.Ordinal))
                {
                    bucketName = value;
                }
                else if (string.Equals(name, OriginField, StringComparison.Ordinal))
                {
                    origin = value;
                }

                continue;
            }

            // Everything after this point consumes the file, so the metadata must already be in hand.
            if (bucketName is null || origin is null)
            {
                return MediaUploadOutcome.Refused(StatusCodes.Status400BadRequest, "metadata_must_precede_file");
            }

            var rule = MediaBuckets.Find(bucketName);
            if (rule is null || rule.OwnerKind != target.OwnerKind)
            {
                return MediaUploadOutcome.Refused(StatusCodes.Status400BadRequest, "unknown_bucket");
            }

            return await StoreFile(section, disposition, rule, origin, target, ct);
        }

        return MediaUploadOutcome.Refused(StatusCodes.Status400BadRequest, "file_missing");
    }

    private static string? ReadBoundary(HttpRequest request)
    {
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType)
            || !contentType.MediaType.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var boundary = HeaderUtilities.RemoveQuotes(contentType.Boundary).Value;
        return string.IsNullOrWhiteSpace(boundary) ? null : boundary;
    }

    /// <summary>Reads one small form value, refusing anything a metadata field has no business being.</summary>
    private static async Task<string?> ReadSmallValue(Stream body, CancellationToken ct)
    {
        var buffer = new byte[MaxFieldValueBytes + 1];
        var read = await ReadUpTo(body, buffer, ct);

        if (read > MaxFieldValueBytes)
        {
            return null;
        }

        return Encoding.UTF8.GetString(buffer, 0, read).Trim();
    }

    private async Task<MediaUploadOutcome> StoreFile(
        MultipartSection section,
        ContentDispositionHeaderValue disposition,
        BucketRule rule,
        string origin,
        MediaUploadTarget target,
        CancellationToken ct)
    {
        var originCheck = MediaValidation.CheckOrigin(rule, origin);
        if (originCheck != MediaRejection.None)
        {
            return Refuse(originCheck);
        }

        var media = mediaOptions.CurrentValue;
        var prefix = ArrayPool<byte>.Shared.Rent(MediaValidation.HeaderPrefixBytes);

        try
        {
            var prefixLength = await ReadUpTo(
                section.Body, prefix.AsMemory(0, MediaValidation.HeaderPrefixBytes), ct);

            var contentCheck = MediaValidation.CheckContent(
                rule, section.ContentType, prefix.AsSpan(0, prefixLength),
                media, clarityOptions.CurrentValue, out var validated, out var clarityResult);

            if (contentCheck != MediaRejection.None)
            {
                return Refuse(contentCheck);
            }

            // Non-null once the check has accepted: a null type cannot pass the allow-list. From here
            // on this is the only content type in play — `section.ContentType` may carry parameters
            // (`audio/webm;codecs=opus`), and the blob, the row and the NEXT3 payload must all record
            // the value that was actually validated.
            var contentType = validated!;

            // Resolved before the PUT, deliberately: a missing placeholder key is a configuration
            // error, and discovering it afterwards would leave a stray blob behind on every single
            // upload to that bucket until someone noticed.
            var docType = ResolveDocType(rule);

            var documentId = Guid.CreateVersion7();
            var blobKey = BlobKey(target, documentId, contentType);

            // The counting stream wraps the replayed prefix as well as the remainder, so the cap
            // covers the whole file and BytesRead is its true size.
            var limited = new LimitedStream(
                new PrefixedStream(prefix.AsMemory(0, prefixLength), section.Body), media.MaxFileBytes);

            try
            {
                await blobs.Put(blobKey, limited, contentType, ct);
            }
            catch (Exception ex) when (IsTooLarge(ex))
            {
                // Our own abort, so the partial blob is definitely garbage — remove it now rather than
                // waiting for the orphan sweep. Best effort: the sweep is the backstop either way.
                await TryDelete(blobKey, ct);
                return Refuse(MediaRejection.FileTooLarge);
            }

            var document = await Record(
                documentId, blobKey, limited.BytesRead, contentType, disposition,
                rule, docType, origin, clarityResult, target, ct);

            return MediaUploadOutcome.Created(document);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(prefix);
        }
    }

    /// <summary>
    /// The one transaction of §4: the document row, its outbox row, and the §9 audit entry, committed
    /// by a single SaveChanges. If this throws, the blob is already written and becomes the orphan
    /// §7.3 accepts — deliberately not deleted here, because the alternative failure (a queued push
    /// naming bytes that are gone) is the one that reaches AXA as a missing photo.
    /// </summary>
    private async Task<Document> Record(
        Guid documentId,
        string blobKey,
        long sizeBytes,
        string contentType,
        ContentDispositionHeaderValue disposition,
        BucketRule rule,
        string docType,
        string origin,
        string clarityResult,
        MediaUploadTarget target,
        CancellationToken ct)
    {
        var fileName = SafeFileName(disposition, documentId, contentType);

        // clientRef = the document id (§5.1), so it is stable across every retry of this push by
        // construction and the fake's — later the real client's — dedupe check has something to hold.
        var outboxMessageId = outbox.EnqueueDocument(
            target.VisaNo,
            new DocumentPush(rule.Next3Folder, docType, fileName, contentType, blobKey),
            documentId.ToString());

        var document = new Document
        {
            Id = documentId,
            OwnerKind = target.OwnerKind,
            OwnerId = target.OwnerId,
            Bucket = rule.Bucket,
            DocType = docType,
            Origin = origin,
            ClarityResult = clarityResult,
            BlobKey = blobKey,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            PushStatus = DocumentPushStatuses.Queued,
            OutboxMessageId = outboxMessageId,
            CreatedBy = target.ActorUserId,
            CreatedAt = time.GetUtcNow().UtcDateTime,
        };

        db.Documents.Add(document);

        // §9: "every media upload (who, which claim/declaration/request, when, origin flag)".
        audit.Append(
            target.ActorUserId, AuditActions.DocumentUploaded, AuditEntityKinds.Document, documentId,
            new { target.OwnerKind, target.OwnerId, target.VisaNo, rule.Bucket, Origin = origin, SizeBytes = sizeBytes });

        await db.SaveChangesAsync(ct);
        return document;
    }

    private string ResolveDocType(BucketRule rule) =>
        next3Options.Value.DocTypes.GetValueOrDefault(rule.DocTypeKey)
        ?? throw new InvalidOperationException(
            $"Next3:DocTypes is missing the key '{rule.DocTypeKey}' required by bucket "
            + $"'{rule.Bucket}' (design.md Appendix A, #12).");

    private async Task TryDelete(string blobKey, CancellationToken ct)
    {
        try
        {
            await blobs.Delete(blobKey, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Swallowed on purpose: the caller is already returning a refusal, and §7.3's orphan
            // sweep exists precisely so a failed cleanup is a delay rather than a leak.
        }
    }

    /// <summary>
    /// The cap trips inside the blob client's own copy loop, and a storage SDK is entitled to wrap
    /// whatever the source stream threw — so match on the chain, not only on the top-level type.
    /// </summary>
    private static bool IsTooLarge(Exception exception)
    {
        for (var ex = exception; ex is not null; ex = ex.InnerException)
        {
            if (ex is MediaTooLargeException)
            {
                return true;
            }
        }

        return false;
    }

    private static MediaUploadOutcome Refuse(MediaRejection rejection) =>
        MediaUploadOutcome.Refused(StatusFor(rejection), rejection.ToCode());

    private static int StatusFor(MediaRejection rejection) => rejection switch
    {
        MediaRejection.FileTooLarge => StatusCodes.Status413PayloadTooLarge,
        MediaRejection.ContentTypeNotAllowed or MediaRejection.ContentTypeMismatch =>
            StatusCodes.Status415UnsupportedMediaType,
        _ => StatusCodes.Status400BadRequest,
    };

    /// <summary>
    /// `{ownerKind}/{ownerId}/{documentId}{ext}` — prefixed by owner because §3 scopes the public
    /// surface's SAS "to its own request's prefix", and a layout that only works for authenticated
    /// callers would have to be reshuffled in 5.3.
    /// </summary>
    private static string BlobKey(MediaUploadTarget target, Guid documentId, string contentType) =>
        $"{target.OwnerKind}/{target.OwnerId:N}/{documentId:N}{Extension(contentType)}";

    private static string Extension(string contentType) => contentType switch
    {
        ImageHeader.Jpeg => ".jpg",
        ImageHeader.Png => ".png",
        ImageHeader.Pdf => ".pdf",
        AudioHeader.Webm => ".webm",
        // .m4a rather than .mp4: an audio-only ISO base-media file, which is what a voice note is.
        AudioHeader.Mp4 => ".m4a",
        AudioHeader.Ogg => ".ogg",
        _ => ".bin",
    };

    /// <summary>
    /// The client's filename, stripped to a leaf name. It reaches NEXT3 and a filesystem at the other
    /// end (#5 may yet make that literal), so a path separator in it is not decoration.
    /// </summary>
    private static string SafeFileName(
        ContentDispositionHeaderValue disposition, Guid documentId, string contentType)
    {
        var raw = disposition.FileNameStar.Value ?? disposition.FileName.Value;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return $"{documentId:N}{Extension(contentType)}";
        }

        var leaf = raw.AsSpan()[(raw.LastIndexOfAny(['/', '\\']) + 1)..].ToString();
        var cleaned = string.Concat(leaf.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));

        return string.IsNullOrWhiteSpace(cleaned)
            ? $"{documentId:N}{Extension(contentType)}"
            : cleaned[..Math.Min(cleaned.Length, 128)];
    }

    private static async Task<int> ReadUpTo(Stream source, Memory<byte> buffer, CancellationToken ct)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await source.ReadAsync(buffer[total..], ct);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
