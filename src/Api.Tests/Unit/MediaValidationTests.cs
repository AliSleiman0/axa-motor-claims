using Api.Modules.Media;
using Api.Tests.Media;

namespace Api.Tests.Unit;

/// <summary>
/// design.md §7.2 item 5: "Server re-validates dimensions, size, and content type on every upload
/// (the client check is UX, not security — mandatory on the public surface)."
///
/// Pure, so the boundaries are exact rather than approximate — the same reason `PublicUploadCaps` is
/// a pure guard. Slice 5.3 reuses this code against an unauthenticated caller, so every case here is
/// eventually a case with a hostile client on the other end.
/// </summary>
public class MediaValidationTests
{
    private static readonly ClarityOptions Clarity = new() { MinWidth = 1024, MinHeight = 768 };

    private static MediaOptions Media()
    {
        var options = new MediaOptions { MaxFileMb = 15 };
        options.ImageContentTypes.Add(ImageHeader.Jpeg);
        options.ImageContentTypes.Add(ImageHeader.Png);
        options.DocumentContentTypes.Add(ImageHeader.Jpeg);
        options.DocumentContentTypes.Add(ImageHeader.Png);
        options.DocumentContentTypes.Add(ImageHeader.Pdf);
        options.AudioContentTypes.Add(AudioHeader.Webm);
        options.AudioContentTypes.Add(AudioHeader.Mp4);
        options.AudioContentTypes.Add(AudioHeader.Ogg);
        return options;
    }

    private static BucketRule CarPhoto => MediaBuckets.Find(MediaBuckets.InsuredCarPhoto)!;

    private static BucketRule Documents => MediaBuckets.Find(MediaBuckets.InsuredDocuments)!;

    private static BucketRule Voice => MediaBuckets.Find(MediaBuckets.VoiceNote)!;

    private static BucketRule Diagram => MediaBuckets.Find(MediaBuckets.DamageDiagram)!;

    [Fact]
    public void AnImageAtTheResolutionFloorPasses()
    {
        var result = MediaValidation.CheckContent(
            CarPhoto, ImageHeader.Jpeg, TestImages.Jpeg(1024, 768), Media(), Clarity, out _, out var clarity);

        Assert.Equal(MediaRejection.None, result);
        Assert.Equal(ClarityResults.Passed, clarity);
    }

    [Theory]
    [InlineData(1023, 768)]
    [InlineData(1024, 767)]
    public void AnImageOneShortOfTheFloorIsRefused(int width, int height)
    {
        var result = MediaValidation.CheckContent(
            CarPhoto, ImageHeader.Jpeg, TestImages.Jpeg(width, height), Media(), Clarity, out _, out _);

        Assert.Equal(MediaRejection.ImageTooSmall, result);
    }

    [Fact]
    public void APdfInADocumentBucketPassesWithNoClarityVerdict()
    {
        // Nothing to measure on a PDF, and §7.2's floor is about photographs — so the row records
        // `not_applicable` rather than a verdict it did not reach.
        var result = MediaValidation.CheckContent(
            Documents, ImageHeader.Pdf, TestImages.Pdf(), Media(), Clarity, out _, out var clarity);

        Assert.Equal(MediaRejection.None, result);
        Assert.Equal(ClarityResults.NotApplicable, clarity);
    }

    [Fact]
    public void APdfInACarPhotoBucketIsRefused()
    {
        // The car-photo buckets take images only; the allow-list is per bucket, not global.
        var result = MediaValidation.CheckContent(
            CarPhoto, ImageHeader.Pdf, TestImages.Pdf(), Media(), Clarity, out _, out _);

        Assert.Equal(MediaRejection.ContentTypeNotAllowed, result);
    }

    [Fact]
    public void APdfDeclaredAsAJpegIsCaughtByItsBytes()
    {
        // The attack the declared content type cannot defend against, and the reason §7.2 item 5 says
        // the client check is not a control.
        var result = MediaValidation.CheckContent(
            CarPhoto, ImageHeader.Jpeg, TestImages.Pdf(), Media(), Clarity, out _, out _);

        Assert.Equal(MediaRejection.ContentTypeMismatch, result);
    }

    [Fact]
    public void APngDeclaredAsAJpegIsRefusedEvenThoughBothAreAllowed()
    {
        var result = MediaValidation.CheckContent(
            CarPhoto, ImageHeader.Jpeg, TestImages.Png(1600, 1200), Media(), Clarity, out _, out _);

        Assert.Equal(MediaRejection.ContentTypeMismatch, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("image/gif")]
    [InlineData("application/octet-stream")]
    public void AContentTypeOutsideTheAllowListIsRefused(string? declared)
    {
        var result = MediaValidation.CheckContent(
            Documents, declared, TestImages.Jpeg(1600, 1200), Media(), Clarity, out _, out _);

        Assert.Equal(MediaRejection.ContentTypeNotAllowed, result);
    }

    [Fact]
    public void AnEmptyFileIsRefused()
    {
        var result = MediaValidation.CheckContent(
            Documents, ImageHeader.Jpeg, [], Media(), Clarity, out _, out _);

        Assert.Equal(MediaRejection.FileEmpty, result);
    }

    [Fact]
    public void AnImageWhoseHeaderIsBeyondThePrefixIsReportedAsUnreadable()
    {
        // Not "not an image": the two need different answers, because one is a hostile upload and the
        // other is a prefix ceiling that may need raising.
        var jpeg = TestImages.Jpeg(1600, 1200, exifBytes: 4_000);

        var result = MediaValidation.CheckContent(
            CarPhoto, ImageHeader.Jpeg, jpeg.AsSpan(0, 600), Media(), Clarity, out _, out _);

        Assert.Equal(MediaRejection.UnreadableImage, result);
    }

    [Fact]
    public void ThePrefixCeilingClearsARealisticPhoneJpeg()
    {
        // 128 KB of peek against a 40 KB EXIF block with an embedded thumbnail. If this ever goes red,
        // the ceiling is the thing to raise — not the check to remove.
        var jpeg = TestImages.Jpeg(4032, 3024, exifBytes: 40_000);
        Assert.True(jpeg.Length < MediaValidation.HeaderPrefixBytes);

        var result = MediaValidation.CheckContent(
            CarPhoto, ImageHeader.Jpeg, jpeg, Media(), Clarity, out _, out var clarity);

        Assert.Equal(MediaRejection.None, result);
        Assert.Equal(ClarityResults.Passed, clarity);
    }

    // ---- slice 3.1: the voice note (§7.2 item 4) and the damage diagram (§1's simplification) ----

    [Fact]
    public void AVoiceNotePassesWithNoClarityVerdict()
    {
        // §7.2 item 4 makes the voice gate a playback-confirm by a person. There is nothing here to
        // measure, so the row records that rather than a verdict it never reached.
        var result = MediaValidation.CheckContent(
            Voice, AudioHeader.Webm, TestAudio.Webm(), Media(), Clarity, out _, out var clarity);

        Assert.Equal(MediaRejection.None, result);
        Assert.Equal(ClarityResults.NotApplicable, clarity);
    }

    [Fact]
    public void ARecordersCodecParameterIsStrippedBeforeTheAllowListCompare()
    {
        // What Chrome actually sends. Compared raw, this is `content_type_not_allowed` and no voice
        // note in the application ever succeeds.
        var result = MediaValidation.CheckContent(
            Voice, "audio/webm;codecs=opus", TestAudio.Webm(), Media(), Clarity,
            out var contentType, out var clarity);

        Assert.Equal(MediaRejection.None, result);
        Assert.Equal(ClarityResults.NotApplicable, clarity);

        // And the value handed back is the one the caller stores — validated and recorded must be
        // the same string, or the row and the bytes disagree about what the file is.
        Assert.Equal(AudioHeader.Webm, contentType);
    }

    [Theory]
    [InlineData(MediaBuckets.VoiceNote, "AUDIO/WEBM", AudioHeader.Webm)]
    [InlineData(MediaBuckets.InsuredCarPhoto, "IMAGE/Jpeg", ImageHeader.Jpeg)]
    [InlineData(MediaBuckets.InsuredDocuments, "Application/PDF", ImageHeader.Pdf)]
    public void TheTypeHandedBackIsCanonical_NotHoweverTheCallerSpelledIt(
        string bucket, string declared, string expected)
    {
        // Every comparison in here is case-insensitive, so `AUDIO/WEBM` is *accepted* — and without
        // canonicalising, that spelling would then be written to `document.content_type`, sent to
        // NEXT3 in the push payload, and missed by the blob-key extension map's ordinal switch,
        // filing a good voice note as `.bin`. Found by the db-reviewer pass, not by the tests.
        var rule = MediaBuckets.Find(bucket)!;
        byte[] bytes = expected switch
        {
            AudioHeader.Webm => TestAudio.Webm(),
            ImageHeader.Pdf => TestImages.Pdf(),
            _ => TestImages.Jpeg(1600, 1200),
        };

        var result = MediaValidation.CheckContent(
            rule, declared, bytes, Media(), Clarity, out var contentType, out _);

        Assert.Equal(MediaRejection.None, result);
        Assert.Equal(expected, contentType);
    }

    [Fact]
    public void AudioDeclaredAsTheWrongContainerIsCaughtByItsBytes()
    {
        var result = MediaValidation.CheckContent(
            Voice, AudioHeader.Mp4, TestAudio.Ogg(), Media(), Clarity, out _, out _);

        Assert.Equal(MediaRejection.ContentTypeMismatch, result);
    }

    [Fact]
    public void AnImageIntoTheVoiceBucketIsRefused()
    {
        // The audio bucket's allow-list is audio only — a photo has no business being a voice note,
        // and it would reach NEXT3 under the voice document type.
        var result = MediaValidation.CheckContent(
            Voice, ImageHeader.Jpeg, TestImages.Jpeg(1600, 1200), Media(), Clarity, out _, out _);

        Assert.Equal(MediaRejection.ContentTypeNotAllowed, result);
    }

    [Fact]
    public void AudioIntoAnImageBucketIsRefused()
    {
        // The same rule from the other side: the car-photo buckets take images only.
        var result = MediaValidation.CheckContent(
            CarPhoto, AudioHeader.Webm, TestAudio.Webm(), Media(), Clarity, out _, out _);

        Assert.Equal(MediaRejection.ContentTypeNotAllowed, result);
    }

    [Fact]
    public void ADamageDiagramIsJudgedAsAnImage()
    {
        // The diagram is a canvas export, so the *server's* floor applies to it exactly as to a
        // photograph. The web module's fixed render size exists to clear this line.
        var result = MediaValidation.CheckContent(
            Diagram, ImageHeader.Png, TestImages.Png(1600, 1200), Media(), Clarity, out _, out var clarity);

        Assert.Equal(MediaRejection.None, result);
        Assert.Equal(ClarityResults.Passed, clarity);
    }

    [Fact]
    public void ADamageDiagramBelowTheFloorIsRefused()
    {
        // If this ever fires in production it means the render size and `Clarity.MinWidth/MinHeight`
        // have drifted apart — see `TheClarityFloorAdmitsADamageDiagram`, which is the guard.
        var result = MediaValidation.CheckContent(
            Diagram, ImageHeader.Png, TestImages.Png(640, 480), Media(), Clarity, out _, out _);

        Assert.Equal(MediaRejection.ImageTooSmall, result);
    }
}
