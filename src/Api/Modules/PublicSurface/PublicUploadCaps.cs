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
/// There is nowhere to store a file until the media pipeline lands (slice 2.3), so this guard is
/// wired to the <c>/public/*</c> body-size filter today and called for real by the Option 2 form
/// in slice 5.3. It exists now because a cap added after the upload path is written is a cap that
/// gets forgotten on one branch.
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
