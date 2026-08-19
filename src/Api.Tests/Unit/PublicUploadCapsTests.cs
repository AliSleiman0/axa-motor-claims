using Api.Modules.PublicSurface;

namespace Api.Tests.Unit;

/// <summary>design.md §9.1's per-submission caps, asserted at the boundary rather than near it.</summary>
public class PublicUploadCapsTests
{
    private static PublicLinkOptions Options(int maxFiles = 3, int maxFileMb = 2) =>
        new() { MaxFiles = maxFiles, MaxFileMb = maxFileMb };

    [Fact]
    public void FileCount_AtTheCap_IsAccepted()
    {
        var options = Options(maxFiles: 3);

        Assert.Equal(UploadCapResult.Ok, PublicUploadCaps.Validate(3, [1, 1, 1], options));
    }

    [Fact]
    public void FileCount_OneOverTheCap_IsRejected()
    {
        var options = Options(maxFiles: 3);

        Assert.Equal(UploadCapResult.TooManyFiles, PublicUploadCaps.Validate(4, [1, 1, 1, 1], options));
    }

    [Fact]
    public void FileSize_ExactlyAtTheCap_IsAccepted()
    {
        var options = Options(maxFileMb: 2);

        Assert.Equal(UploadCapResult.Ok, PublicUploadCaps.Validate(1, [options.MaxFileBytes], options));
    }

    [Fact]
    public void FileSize_OneByteOverTheCap_IsRejected()
    {
        var options = Options(maxFileMb: 2);

        Assert.Equal(
            UploadCapResult.FileTooLarge,
            PublicUploadCaps.Validate(1, [options.MaxFileBytes + 1], options));
    }

    [Fact]
    public void ASingleOversizeFile_AmongAcceptableOnes_IsRejected()
    {
        var options = Options(maxFileMb: 2);

        Assert.Equal(
            UploadCapResult.FileTooLarge,
            PublicUploadCaps.Validate(3, [10, options.MaxFileBytes + 1, 10], options));
    }

    [Fact]
    public void NoFiles_IsAccepted_TheCapsAreUpperBoundsOnly()
    {
        // Whether a submission *requires* documents is §5.3's business rule, not a cap.
        Assert.Equal(UploadCapResult.Ok, PublicUploadCaps.Validate(0, [], Options()));
    }

    [Fact]
    public void CapsComeFromOptions_NotFromLiterals()
    {
        var generous = Options(maxFiles: 50, maxFileMb: 100);
        var strict = Options(maxFiles: 1, maxFileMb: 1);
        long twoMb = 2 * 1024 * 1024;

        Assert.Equal(UploadCapResult.Ok, PublicUploadCaps.Validate(2, [twoMb, twoMb], generous));
        Assert.Equal(UploadCapResult.TooManyFiles, PublicUploadCaps.Validate(2, [twoMb, twoMb], strict));
    }

    [Fact]
    public void MaxFileBytes_ConvertsMegabytesWithoutOverflowing()
    {
        // int arithmetic would wrap somewhere above 2 GB; the property casts to long first.
        var options = Options(maxFileMb: 4096);

        Assert.Equal(4096L * 1024 * 1024, options.MaxFileBytes);
    }
}
