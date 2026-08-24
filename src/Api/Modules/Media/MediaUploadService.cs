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
/// <param name="VisaNo">
/// Null when the owner has no visa yet — a garage declaration at Draft (§5.2), where the officer has
/// not linked one and may yet reject the whole thing. Required for any
/// <see cref="PushTiming.Immediate"/> bucket, because that is the value its outbox row is addressed
/// to; <see cref="MediaUploadService"/> refuses the combination rather than pushing under an empty
/// string, which NEXT3 would file somewhere nobody looks.
/// </param>
/// <param name="ActorUserId">Null for the unauthenticated Option 2 customer (§5.3).</param>
public sealed record MediaUploadTarget(string OwnerKind, Guid OwnerId, string? VisaNo, Guid? ActorUserId);

/// <summary>Either a created document or a refusal with the code the client branches on.</summary>
public sealed record MediaUploadOutcome(int StatusCode, string? ErrorCode, Document? Document)
{
    public static MediaUploadOutcome Refused(int statusCode, string errorCode) =>
        new(statusCode, errorCode, null);

    public static MediaUploadOutcome Created(Document document) =>
        new(StatusCodes.Status201Created, null, document);
}

/// <summary>
/// A caller's answer to "may this upload use this bucket, here, now?" — null to allow it, otherwise
/// the refusal to return.
///
/// It replaced a flat allow-list in slice 5.1. §5.2's garage now owns two bucket sets whose refusals
/// differ (`declaration_already_decided` for the pre-decision ones, `repairs_not_in_progress` for
/// G4's), and the bucket is not known until the multipart metadata has been read — inside the
/// service. Returning the attempted bucket to the endpoint and re-deciding there would put the same
/// policy in two places, which is the drift <see cref="MediaBuckets"/> exists to prevent.
/// </summary>
public delegate MediaUploadOutcome? BucketGate(BucketRule rule);

/// <summary>The gates that are not state-dependent.</summary>
public static class BucketGates
{
    /// <summary>
    /// Narrows a caller to a fixed set of buckets, refusing anything else with
    /// <c>400 bucket_not_allowed_for_caller</c> — slice 4.1's rule, unchanged. §5.2's officer needs
    /// it: the approval image and the garage's own documents share an owner kind, so the bucket rules
    /// alone would accept a `garage_car_photo` from an officer, and §5.2 grants the officer review,
    /// not the ability to add evidence to a claim.
    /// </summary>
    public static BucketGate Only(params string[] buckets) =>
        rule => buckets.Contains(rule.Bucket, StringComparer.Ordinal)
            ? null
            : MediaUploadOutcome.Refused(
                StatusCodes.Status400BadRequest, "bucket_not_allowed_for_caller");
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
    BucketRules buckets,
    IOptionsMonitor<MediaOptions> mediaOptions,
    IOptionsMonitor<ClarityOptions> clarityOptions,
    IOptions<Next3Options> next3Options,
    TimeProvider time)
{
    private const string BucketField = "bucket";
    private const string OriginField = "origin";
    private const int MaxFieldValueBytes = 256;

    /// <param name="gate">
    /// Narrows the caller to a subset of the buckets its owner kind allows, consulted **before the
    /// file is read** so a refusal costs no blob and leaves no row. See <see cref="BucketGate"/> for
    /// why it is a callback rather than a list.
    /// </param>
    public async Task<MediaUploadOutcome> Upload(
        HttpRequest request,
        MediaUploadTarget target,
        CancellationToken ct,
        BucketGate? gate = null)
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

            var rule = buckets.Find(bucketName);
            if (rule is null || rule.OwnerKind != target.OwnerKind)
            {
                return MediaUploadOutcome.Refused(StatusCodes.Status400BadRequest, "unknown_bucket");
            }

            // A distinct refusal from `unknown_bucket`: the bucket is real and this owner kind has
            // it, the *caller* may not use it — or may not use it in the state its owner is in.
            // Consulted here, before StoreFile, so the rejection costs no blob and leaves no row for
            // the orphan sweep to find later.
            if (gate?.Invoke(rule) is { } refusal)
            {
                return refusal;
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
    ///
    /// **The outbox row is conditional on <see cref="BucketRule.Timing"/> (slice 4.1).** For an
    /// <see cref="PushTiming.OnApproval"/> bucket the row is stored <c>deferred</c> with no outbox row
    /// at all, and §5.2's approve transition enqueues it once a visa exists. That is what makes
    /// "nothing goes to NEXT3 before approval" a property of the schema — <c>CK_document_push_status_outbox</c>
    /// makes a deferred document with an outbox row unrepresentable — rather than a rule the garage
    /// endpoint has to remember.
    /// </summary>
    private async Task<Document> Record(
        Guid documentId,
        string blobKey,
        long sizeBytes,
        string contentType,
        ContentDispositionHeaderValue disposition,
        BucketRule rule,
        string? docType,
        string origin,
        string clarityResult,
        MediaUploadTarget target,
        CancellationToken ct)
    {
        var fileName = SafeFileName(disposition, documentId, contentType);

        // clientRef = the document id (§5.1), so it is stable across every retry of this push by
        // construction and the fake's — later the real client's — dedupe check has something to hold.
        // Null for a deferred bucket: §5.2's approve transition enqueues those, under the visa the
        // officer has by then chosen, using the same document id as the clientRef.
        var outboxMessageId = rule.Timing switch
        {
            // `docType` and `Next3Folder` are non-null on this arm by construction: only a
            // PushTiming.Never bucket may omit them, and Never does not reach here.
            PushTiming.Immediate => outbox.EnqueueDocument(
                RequireVisa(target, rule),
                new DocumentPush(
                    RequireNext3Fields(rule.Next3Folder, rule, nameof(BucketRule.Next3Folder)),
                    RequireNext3Fields(docType, rule, nameof(BucketRule.DocTypeKey)),
                    fileName, contentType, blobKey),
                documentId.ToString()),
            PushTiming.OnApproval or PushTiming.Never => (Guid?)null,
            _ => throw new ArgumentOutOfRangeException(nameof(rule), rule.Timing, null),
        };

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
            FileName = fileName,
            SizeBytes = sizeBytes,
            PushStatus = PushStatusFor(rule.Timing),
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

    /// <summary>
    /// The push status that goes with a timing. Paired with the outbox-row decision above by
    /// <c>CK_document_push_status_outbox</c>, which rejects any row where the two disagree — so a
    /// future fourth timing cannot half-land.
    /// </summary>
    private static string PushStatusFor(PushTiming timing) => timing switch
    {
        PushTiming.Immediate => DocumentPushStatuses.Queued,
        PushTiming.OnApproval => DocumentPushStatuses.Deferred,
        PushTiming.Never => DocumentPushStatuses.NotApplicable,
        _ => throw new ArgumentOutOfRangeException(nameof(timing), timing, null),
    };

    /// <summary>
    /// An immediate push has to be addressed to a visa, and the only callers that can supply one are
    /// the ones whose owner row already carries it. A miswired endpoint is a bug here rather than at
    /// the far end of the queue 26 hours later, where the symptom would be a `failed` row on A2 for a
    /// document nobody can re-file.
    /// </summary>
    /// <summary>
    /// The other half of the <see cref="PushTiming.Never"/> biconditional, at the one place it matters
    /// (slice 5.2). A pushing bucket with no document type or no folder would queue a row NEXT3 cannot
    /// file; this makes that a startup-shaped bug here rather than a `failed` row on A2 a day later.
    /// </summary>
    private static string RequireNext3Fields(string? value, BucketRule rule, string field) =>
        value is { Length: > 0 } set
            ? set
            : throw new InvalidOperationException(
                $"Bucket '{rule.Bucket}' pushes to NEXT3 ({rule.Timing}) but has no {field} "
                + "(design.md §7.1).");

    private static string RequireVisa(MediaUploadTarget target, BucketRule rule) =>
        target.VisaNo is { Length: > 0 } visaNo
            ? visaNo
            : throw new InvalidOperationException(
                $"Bucket '{rule.Bucket}' pushes immediately, so its upload target must carry a visa "
                + $"(owner kind '{target.OwnerKind}', id {target.OwnerId}).");

    /// <summary>
    /// Null for a bucket that never reaches NEXT3 (slice 5.2). A <see cref="PushTiming.Never"/> rule
    /// carries no <see cref="BucketRule.DocTypeKey"/>, and `MediaBucketTests` pins that biconditional,
    /// so reading the key here is the same question as reading the timing and cannot disagree with it.
    /// The throw stays for every bucket that *does* have a key: a missing placeholder is a
    /// configuration error and §7.3 would rather find it before the blob PUT than after.
    /// </summary>
    private string? ResolveDocType(BucketRule rule) =>
        rule.DocTypeKey is not { } key
            ? null
            : next3Options.Value.DocTypes.GetValueOrDefault(key)
              ?? throw new InvalidOperationException(
                  $"Next3:DocTypes is missing the key '{key}' required by bucket "
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
    /// Characters a stored file name may never contain, spelled out because the framework's answer is
    /// not the same on two operating systems.
    ///
    /// <c>Path.GetInvalidFileNameChars()</c> was used here until slice 4.1's db-reviewer pass. On
    /// Windows it returns 41 characters and the sanitiser looks thorough; **on Linux it returns
    /// exactly <c>'\0'</c> and <c>'/'</c>**, and design.md §3 and §10 put this application on Linux
    /// containers. So the guard did almost nothing precisely where it runs, and everything where it is
    /// developed and tested — the worst arrangement available.
    ///
    /// CR and LF are the ones that matter beyond tidiness: this value is written into a
    /// <c>Content-Disposition</c> header by <c>RealNext3Client</c>, and a newline in a header value is
    /// a header-injection primitive supplied by whoever picked the file.
    /// </summary>
    private const string UnsafeFileNameChars = "\"<>|:*?\\/";

    /// <summary>
    /// Every control character goes as well as <see cref="UnsafeFileNameChars"/> — that is the half
    /// <c>Path.GetInvalidFileNameChars()</c> silently stopped covering the moment this ran on Linux.
    /// </summary>
    private static bool IsSafeFileNameChar(char c) =>
        !char.IsControl(c) && !UnsafeFileNameChars.Contains(c, StringComparison.Ordinal);

    /// <summary>
    /// The client's filename, stripped to a leaf name. It reaches NEXT3 and a filesystem at the other
    /// end (#5 may yet make that literal), so a path separator in it is not decoration.
    ///
    /// Since slice 4.1 the result is also *stored* (<see cref="Document.FileName"/>) rather than only
    /// used in the moment, because a deferred push is queued in a later request — which makes it worth
    /// stating that the sanitising happens once, here, and the stored value is the sanitised one.
    /// </summary>
    private static string SafeFileName(
        ContentDispositionHeaderValue disposition, Guid documentId, string contentType)
    {
        // FileNameStar first: it is the RFC 5987 form and is already percent-decoded, so a name with
        // non-ASCII characters survives. Both getters strip surrounding quotes — verified rather than
        // assumed, because the ASP.NET section helpers call RemoveQuotes and it is a fair question
        // whether that is because these do not.
        var raw = disposition.FileNameStar.Value ?? disposition.FileName.Value;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return $"{documentId:N}{Extension(contentType)}";
        }

        var leaf = raw.AsSpan()[(raw.LastIndexOfAny(['/', '\\']) + 1)..].ToString();
        var cleaned = string.Concat(leaf.Where(IsSafeFileNameChar)).Trim();

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
