namespace Api.Modules.PublicSurface;

/// <summary>Why a submission's files were refused. <see cref="Ok"/> is the only accepting value.</summary>
public enum UploadCapResult
{
    Ok,
    TooManyFiles,
    FileTooLarge,
}

/// <summary>
/// design.md §9.1's "hard caps on file count and size per submission". Pure, so the boundaries are
/// tested exactly rather than approximately.
/// </summary>
/// <remarks>
/// <para>
/// Written in slice 1.5 with nowhere to store a file, on the reasoning that a cap added after the
/// upload path exists is a cap that gets forgotten on one branch. **Slice 5.3 is its first production
/// caller** — <c>POST /public/{token}/documents</c> — and the wiring is worth being precise about,
/// because the two branches are not equally load-bearing.
/// </para>
/// <para>
/// <see cref="UploadCapResult.TooManyFiles"/> is the half only that call can make: §9.1's `MaxFiles`
/// is a cap *per submission*, and since each request carries exactly one file, nothing but a count of
/// the rows already stored can enforce it. <see cref="UploadCapResult.FileTooLarge"/> is a second
/// layer behind <see cref="PublicBodySizeMiddleware"/>, which rejects the same predicate before the
/// handler runs and additionally lowers <c>IHttpMaxRequestBodySizeFeature</c> so a caller who lies
/// about <c>Content-Length</c> is cut off by the server. Kept anyway — one place owns both codes, and
/// the endpoint should not depend on middleware ordering for a rule it can state itself.
/// </para>
/// </remarks>
public static class PublicUploadCaps
{
    public static UploadCapResult Validate(
        int fileCount, IReadOnlyCollection<long> fileSizesInBytes, PublicLinkOptions options)
    {
        ArgumentNullException.ThrowIfNull(fileSizesInBytes);
        ArgumentNullException.ThrowIfNull(options);

        if (fileCount > options.MaxFiles)
        {
            return UploadCapResult.TooManyFiles;
        }

        return fileSizesInBytes.Any(size => size > options.MaxFileBytes)
            ? UploadCapResult.FileTooLarge
            : UploadCapResult.Ok;
    }
}
