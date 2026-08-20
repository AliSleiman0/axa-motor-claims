using Api.Modules.Media;
using Api.Tests.Media;

namespace Api.Tests.Unit;

/// <summary>
/// design.md §7.2 item 5 makes the server re-validate dimensions and content type. This is the parser
/// that does it — no image library, for the licensing and portability reasons recorded on
/// <see cref="ImageHeader"/>. These tests are the reason that trade is safe to make.
/// </summary>
public class ImageHeaderTests
{
    [Fact]
    public void APngHeader_YieldsItsDimensions()
    {
        var status = ImageHeader.Read(TestImages.Png(1024, 768), out var info);

        Assert.Equal(ImageHeaderStatus.Ok, status);
        Assert.Equal(ImageHeader.Png, info.ContentType);
        Assert.Equal(1024, info.Width);
        Assert.Equal(768, info.Height);
    }

    [Fact]
    public void ABaselineJpeg_YieldsItsDimensions()
    {
        var status = ImageHeader.Read(TestImages.Jpeg(1600, 1200), out var info);

        Assert.Equal(ImageHeaderStatus.Ok, status);
        Assert.Equal(ImageHeader.Jpeg, info.ContentType);
        Assert.Equal(1600, info.Width);
        Assert.Equal(1200, info.Height);
    }

    [Fact]
    public void AProgressiveJpeg_YieldsItsDimensions()
    {
        // SOF2 rather than SOF0. Phone cameras emit both, and a parser that only knows the baseline
        // marker rejects perfectly good photographs at the roadside.
        var status = ImageHeader.Read(TestImages.Jpeg(1280, 960, startOfFrameMarker: 0xC2), out var info);

        Assert.Equal(ImageHeaderStatus.Ok, status);
        Assert.Equal(1280, info.Width);
        Assert.Equal(960, info.Height);
    }

    [Fact]
    public void AJpegWithALargeExifBlockAheadOfItsFrameHeader_IsStillRead()
    {
        // The realistic case: an EXIF segment with an embedded thumbnail pushes SOF tens of kilobytes
        // in. If the parser only looked at the first few hundred bytes, every phone photo would be
        // rejected as unreadable.
        var status = ImageHeader.Read(TestImages.Jpeg(2048, 1536, exifBytes: 40_000), out var info);

        Assert.Equal(ImageHeaderStatus.Ok, status);
        Assert.Equal(2048, info.Width);
        Assert.Equal(1536, info.Height);
    }

    [Fact]
    public void AJpegWhoseFrameHeaderIsBeyondTheSuppliedBytes_IsIncompleteNotRejected()
    {
        // Distinguishable from "not an image" on purpose: `unreadable_image` tells a developer to look
        // at the prefix ceiling, where `content_type_mismatch` would send them hunting a fake file.
        var jpeg = TestImages.Jpeg(1024, 768, exifBytes: 4_000);

        var status = ImageHeader.Read(jpeg.AsSpan(0, 500), out _);

        Assert.Equal(ImageHeaderStatus.Incomplete, status);
    }

    [Fact]
    public void ANonImage_IsRejected()
    {
        Assert.Equal(ImageHeaderStatus.NotAnImage, ImageHeader.Read("not an image at all"u8, out _));
    }

    [Fact]
    public void ATruncatedPngSignature_IsRejected()
    {
        Assert.Equal(ImageHeaderStatus.NotAnImage, ImageHeader.Read([0x89, 0x50, 0x4E], out _));
    }

    [Fact]
    public void APdf_SniffsAsAPdf_AndIsNotAnImage()
    {
        Assert.Equal(ImageHeader.Pdf, ImageHeader.SniffContentType(TestImages.Pdf()));
        Assert.Equal(ImageHeaderStatus.NotAnImage, ImageHeader.Read(TestImages.Pdf(), out _));
    }

    [Fact]
    public void SniffingReportsWhatTheBytesAre_NotWhatTheyClaim()
    {
        // The check that makes a declared content type worth nothing on its own: this is exactly the
        // payload a caller sends when they label a PDF `image/jpeg` to reach a capture-only bucket.
        Assert.Equal(ImageHeader.Jpeg, ImageHeader.SniffContentType(TestImages.Jpeg(1024, 768)));
        Assert.Null(ImageHeader.SniffContentType("GIF89a and then some"u8));
    }
}
