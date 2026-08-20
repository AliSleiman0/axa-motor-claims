using Api.Integrations.Next3;
using Api.Modules.Media;

namespace Api.Tests.Unit;

/// <summary>
/// design.md §7.1's table, asserted row for row.
///
/// The BRD's hard rule is that car photos are **capture-only** — it is the whole reason the app is
/// worth building, because the photographs have to be the ones taken at the scene. A rule that lives
/// only in a table in a document is a rule that gets misremembered in slice 5.1; this is the copy
/// that fails the build.
/// </summary>
public class MediaBucketTests
{
    [Theory]
    // Bucket, upload allowed per §7.1, the kind that decides its content-type allow-list.
    [InlineData(MediaBuckets.InsuredDocuments, true, MediaKind.Document)]
    [InlineData(MediaBuckets.InsuredCarPhoto, false, MediaKind.Image)]
    [InlineData(MediaBuckets.TpDocuments, true, MediaKind.Document)]
    [InlineData(MediaBuckets.TpCarPhoto, false, MediaKind.Image)]
    [InlineData(MediaBuckets.ExpertReport, true, MediaKind.Document)]
    public void TheBucketMatrixMatchesSection71(string bucket, bool allowUpload, MediaKind kind)
    {
        var rule = MediaBuckets.Find(bucket);

        Assert.NotNull(rule);
        Assert.Equal(allowUpload, rule.AllowUpload);
        Assert.Equal(kind, rule.Kind);
    }

    [Fact]
    public void EveryExpertBucketLandsInTheExpertDocumentsFolder()
    {
        // §5.1 names that folder for expert media — including the damage diagram, which is not one of
        // the four buckets but goes to the same place.
        Assert.All(
            MediaBuckets.AllRules,
            rule => Assert.Equal(Next3Folders.ExpertDocuments, rule.Next3Folder));
    }

    [Theory]
    [InlineData(MediaBuckets.InsuredCarPhoto)]
    [InlineData(MediaBuckets.TpCarPhoto)]
    public void ACaptureOnlyBucketRefusesAnUploadedFile(string bucket)
    {
        var rule = MediaBuckets.Find(bucket)!;

        Assert.Equal(
            MediaRejection.UploadNotAllowedForBucket,
            MediaValidation.CheckOrigin(rule, DocumentOrigins.Uploaded));

        Assert.Equal(
            MediaRejection.None,
            MediaValidation.CheckOrigin(rule, DocumentOrigins.Captured));
    }

    [Theory]
    [InlineData(MediaBuckets.InsuredDocuments)]
    [InlineData(MediaBuckets.TpDocuments)]
    [InlineData(MediaBuckets.ExpertReport)]
    public void ADocumentBucketTakesEitherProvenance(string bucket)
    {
        var rule = MediaBuckets.Find(bucket)!;

        Assert.Equal(MediaRejection.None, MediaValidation.CheckOrigin(rule, DocumentOrigins.Uploaded));
        Assert.Equal(MediaRejection.None, MediaValidation.CheckOrigin(rule, DocumentOrigins.Captured));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("scanned")]
    public void AnUnrecognisedProvenanceIsRefused(string? origin)
    {
        // Not defaulted to `uploaded` or `captured`: the flag is evidence in a claims dispute (§9's
        // audit trail records it), so guessing it is worse than refusing the upload.
        var rule = MediaBuckets.Find(MediaBuckets.InsuredDocuments)!;

        Assert.Equal(MediaRejection.UnknownOrigin, MediaValidation.CheckOrigin(rule, origin));
    }

    [Fact]
    public void AnUnknownBucketIsNotFound()
    {
        Assert.Null(MediaBuckets.Find("PLACEHOLDER-not-a-bucket"));
        Assert.Null(MediaBuckets.Find(null));
    }
}
