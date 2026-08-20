namespace Api.Modules.Media;

/// <summary>Why an upload was refused. <see cref="None"/> is the only accepting value.</summary>
public enum MediaRejection
{
    None,

    /// <summary>§7.1's capture-only rule: this bucket refuses a file the user picked from a gallery.</summary>
    UploadNotAllowedForBucket,

    UnknownOrigin,

    /// <summary>The declared type is not on the bucket's allow-list.</summary>
    ContentTypeNotAllowed,

    /// <summary>The bytes are not what the caller said they were.</summary>
    ContentTypeMismatch,

    /// <summary>An image whose size marker was not within the header prefix the server reads.</summary>
    UnreadableImage,

    /// <summary>Below §7.2's resolution floor.</summary>
    ImageTooSmall,

    FileTooLarge,

    FileEmpty,
}

/// <summary>
/// design.md §7.2 item 5 — "Server re-validates dimensions, size, and content type on every upload
/// (the client check is UX, not security — mandatory on the public surface)" — plus §7.1's
/// capture-only rule. Pure, like <c>PublicUploadCaps</c>, so the boundaries are tested exactly rather
/// than approximately.
///
/// Blur is deliberately absent: §7.2 lists three server checks, not four, and the Laplacian pass is
/// client-side (slice 2.5). That is why <see cref="ClarityResults"/> has no `failed` value.
/// </summary>
public static class MediaValidation
{
    /// <summary>How many leading bytes the caller must supply to <see cref="CheckContent"/>.</summary>
    /// <remarks>
    /// A phone JPEG puts EXIF — often with an embedded thumbnail — ahead of the frame header, so the
    /// dimensions can sit tens of kilobytes in. 128 KB is a bounded peek, not a buffer of the file:
    /// §7.3's "no buffering into memory" is about the 15 MB photo, not about its header.
    /// </remarks>
    public const int HeaderPrefixBytes = 128 * 1024;

    /// <summary>§7.1: does this bucket accept a file with this provenance?</summary>
    public static MediaRejection CheckOrigin(BucketRule rule, string? origin)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (origin != DocumentOrigins.Captured && origin != DocumentOrigins.Uploaded)
        {
            return MediaRejection.UnknownOrigin;
        }

        return origin == DocumentOrigins.Uploaded && !rule.AllowUpload
            ? MediaRejection.UploadNotAllowedForBucket
            : MediaRejection.None;
    }

    /// <summary>
    /// §7.2 item 5's content-type and dimension checks, against the file's leading bytes.
    /// <paramref name="clarityResult"/> is the value to store on the row when this returns
    /// <see cref="MediaRejection.None"/>, and <paramref name="contentType"/> is the type that was
    /// actually validated — the caller stores <em>that</em>, never its own input, so a
    /// <c>audio/webm;codecs=opus</c> upload cannot be checked as one thing and recorded as another
    /// (see <see cref="MediaContentType"/>).
    /// </summary>
    public static MediaRejection CheckContent(
        BucketRule rule,
        string? declaredContentType,
        ReadOnlySpan<byte> headerPrefix,
        MediaOptions media,
        ClarityOptions clarity,
        out string? contentType,
        out string clarityResult)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(media);
        ArgumentNullException.ThrowIfNull(clarity);

        clarityResult = ClarityResults.NotApplicable;
        contentType = MediaContentType.Normalise(declaredContentType);

        if (headerPrefix.IsEmpty)
        {
            return MediaRejection.FileEmpty;
        }

        if (contentType is null
            || !media.AllowedFor(rule.Kind).Contains(contentType, StringComparer.OrdinalIgnoreCase))
        {
            return MediaRejection.ContentTypeNotAllowed;
        }

        if (AudioHeader.IsAudio(contentType))
        {
            // §7.2 item 4: a voice note's gate is a person pressing play, so there is nothing to
            // measure here — only whether the bytes are the container they were declared to be.
            // Keyed on the declared type rather than on the bucket's kind, because that is where the
            // claim was made; an audio type reaches this line only from an audio bucket anyway.
            var sniffed = AudioHeader.Sniff(headerPrefix);
            if (!string.Equals(sniffed, contentType, StringComparison.OrdinalIgnoreCase))
            {
                return MediaRejection.ContentTypeMismatch;
            }

            contentType = sniffed;
            return MediaRejection.None;
        }

        if (ImageHeader.LooksLikePdf(headerPrefix))
        {
            // Nothing to measure. §7.2's floor is about photographs.
            if (!string.Equals(contentType, ImageHeader.Pdf, StringComparison.OrdinalIgnoreCase))
            {
                return MediaRejection.ContentTypeMismatch;
            }

            contentType = ImageHeader.Pdf;
            return MediaRejection.None;
        }

        // Read before comparing types: an image whose frame header sits past the prefix must be
        // reported as unreadable, not as a forgery. One sends a developer to the prefix ceiling, the
        // other sends them hunting an attacker who is not there.
        var status = ImageHeader.Read(headerPrefix, out var info);
        if (status == ImageHeaderStatus.Incomplete)
        {
            return MediaRejection.UnreadableImage;
        }

        // What the bytes are, not what the caller claims they are.
        if (status != ImageHeaderStatus.Ok
            || !string.Equals(info.ContentType, contentType, StringComparison.OrdinalIgnoreCase))
        {
            return MediaRejection.ContentTypeMismatch;
        }

        if (info.Width < clarity.MinWidth || info.Height < clarity.MinHeight)
        {
            return MediaRejection.ImageTooSmall;
        }

        // Every accepting path hands back the **canonical** type — the one the bytes were recognised
        // as, not the one the caller typed. All the comparisons above are case-insensitive, so
        // `AUDIO/WEBM` is accepted; without this it would then be stored on the row, sent to NEXT3 in
        // the push payload, and matched ordinally by the blob-key extension map, which would file a
        // perfectly good voice note as `.bin`. Found by the db-reviewer pass, slice 3.1.
        contentType = info.ContentType;

        clarityResult = ClarityResults.Passed;
        return MediaRejection.None;
    }

    /// <summary>The wire code for a rejection — a stable string the web client can branch on.</summary>
    public static string ToCode(this MediaRejection rejection) => rejection switch
    {
        MediaRejection.UploadNotAllowedForBucket => "upload_not_allowed_for_bucket",
        MediaRejection.UnknownOrigin => "unknown_origin",
        MediaRejection.ContentTypeNotAllowed => "content_type_not_allowed",
        MediaRejection.ContentTypeMismatch => "content_type_mismatch",
        MediaRejection.UnreadableImage => "unreadable_image",
        MediaRejection.ImageTooSmall => "image_too_small",
        MediaRejection.FileTooLarge => "file_too_large",
        MediaRejection.FileEmpty => "file_empty",
        _ => "ok",
    };
}
