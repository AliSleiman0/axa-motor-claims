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
    // Slice 3.1. Both are produced in-app, so there is no file to pick; the diagram is an *image*
    // because it is a canvas export and §7.2's server floor applies to it.
    [InlineData(MediaBuckets.VoiceNote, false, MediaKind.Audio)]
    [InlineData(MediaBuckets.DamageDiagram, false, MediaKind.Image)]
    // Slice 4.1's §5.2 declaration buckets. The garage's car photos are capture-only for the BRD's
    // reason; the approval image is not, it simply has no file to pick (the officer's browser renders
    // it), which is the 3.1 distinction.
    [InlineData(MediaBuckets.GarageDocuments, true, MediaKind.Document)]
    [InlineData(MediaBuckets.GarageCarPhoto, false, MediaKind.Image)]
    [InlineData(MediaBuckets.ApprovalImage, false, MediaKind.Image)]
    // Slice 5.1's G4 buckets. The repair photo is capture-only for the BRD's reason again — the point
    // of a post-repair photograph is that it is of the car that was repaired. The discharge and the
    // invoice are paperwork, so both provenances are acceptable.
    [InlineData(MediaBuckets.RepairPhoto, false, MediaKind.Image)]
    [InlineData(MediaBuckets.Discharge, true, MediaKind.Document)]
    [InlineData(MediaBuckets.Invoice, true, MediaKind.Document)]
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
        //
        // **Scoped to assignment rows in slice 4.1.** It swept AllRules unfiltered, and it was right
        // to: until §5.2's buckets existed, every bucket in the system was an expert's. The guarantee
        // it asserted has genuinely narrowed rather than been weakened — expert media still all lands
        // in *Expert documents*, and the declaration rows that now exist land in *Survey*, which the
        // companion test below pins just as tightly. Left unscoped it would have had to be deleted.
        Assert.All(
            MediaBuckets.AllRules.Where(r => r.OwnerKind == DocumentOwnerKinds.Assignment),
            rule => Assert.Equal(Next3Folders.ExpertDocuments, rule.Next3Folder));
    }

    [Fact]
    public void EveryDeclarationBucketLandsInTheSurveyFolderAndOnlyThePreDecisionOnesWait()
    {
        // §5.2's other half. *Survey* is the folder the BRD names for the garage-initiated path, and
        // that half of the guarantee is unconditional: all six declaration buckets land there.
        //
        // **Renamed in slice 5.1**, because the timing half stopped being true of every row and this
        // test was right about a guarantee that has genuinely narrowed. OnApproval is what makes
        // "nothing goes to NEXT3 before approval" true of the pipeline rather than of one endpoint —
        // a *pre-decision* bucket that shipped as Immediate would push a photo under a visa that does
        // not exist yet. G4's three are the opposite case: they are attached at `repairs_in_progress`,
        // where `CK_declaration_decision` guarantees a visa, and the only transition that drains the
        // deferred set is `approve`, which has already run — so deferring them would strand them for
        // ever rather than protect anything.
        var declarationRules = MediaBuckets.AllRules
            .Where(r => r.OwnerKind == DocumentOwnerKinds.Declaration)
            .ToList();

        Assert.Equal(6, declarationRules.Count);
        Assert.All(declarationRules, rule => Assert.Equal(Next3Folders.Survey, rule.Next3Folder));

        // The split is pinned to the two named sets, not to a count of three and three: that is what
        // makes a fourth repair bucket added without touching `MediaBuckets.Repair` fail here rather
        // than quietly inherit whichever timing its author typed.
        Assert.Equal(
            MediaBuckets.Repair.Order(StringComparer.Ordinal),
            Buckets(declarationRules, PushTiming.Immediate));

        Assert.Equal(
            MediaBuckets.GarageDeclaration.Append(MediaBuckets.ApprovalImage).Order(StringComparer.Ordinal),
            Buckets(declarationRules, PushTiming.OnApproval));
    }

    [Fact]
    public void TheTwoNamedDeclarationSetsAreExactlyTheGaragesOwnBuckets()
    {
        // The gate in GarageDeclarationEndpoints branches on these two sets and refuses everything
        // else, so a bucket that belongs to neither is unreachable by a garage — which is correct for
        // `approval_image` and would be a silently dead feature for anything else. Pinned here so
        // adding a seventh declaration bucket forces the decision instead of defaulting to "refused".
        var garageBuckets = MediaBuckets.AllRules
            .Where(r => r.OwnerKind == DocumentOwnerKinds.Declaration && r.Bucket != MediaBuckets.ApprovalImage)
            .Select(r => r.Bucket)
            .Order(StringComparer.Ordinal);

        Assert.Equal(
            MediaBuckets.GarageDeclaration.Concat(MediaBuckets.Repair).Order(StringComparer.Ordinal),
            garageBuckets);

        Assert.Empty(MediaBuckets.GarageDeclaration.Intersect(MediaBuckets.Repair, StringComparer.Ordinal));
    }

    [Fact]
    public void EveryBucketIsAccountedForByOwnerKind()
    {
        // The non-vacuity guard for the two tests above: together they must cover every rule, or a
        // bucket added under a third owner kind would be asserted by neither and both would still
        // read green. CLAUDE.md: "a guard that quietly stops covering new code is worse than none".
        //
        // Slice 5.1 left this alone deliberately. Its three buckets are declaration rows, so the
        // guard still covers every rule and is still non-vacuous — the assertion needed no widening,
        // only re-reading to confirm that.
        Assert.Equal(
            MediaBuckets.AllRules.Count,
            MediaBuckets.AllRules.Count(r =>
                r.OwnerKind == DocumentOwnerKinds.Assignment || r.OwnerKind == DocumentOwnerKinds.Declaration));
    }

    [Fact]
    public void EveryBucketsTimingIsOneThePipelineHandles()
    {
        // MediaUploadService.Record switches on Timing and throws on anything unrecognised, and
        // PushStatusFor pairs each timing with the push status its check constraint expects. This is
        // what stops PushTiming.Never rotting: it has no bucket yet (slice 5.2's broker media), so
        // nothing else exercises the mapping.
        Assert.All(
            MediaBuckets.AllRules,
            rule => Assert.Contains(rule.Timing, Enum.GetValues<PushTiming>()));

        Assert.DoesNotContain(MediaBuckets.AllRules, r => r.Timing == PushTiming.Never);
    }

    [Theory]
    [InlineData(MediaBuckets.InsuredCarPhoto)]
    [InlineData(MediaBuckets.TpCarPhoto)]
    [InlineData(MediaBuckets.VoiceNote)]
    [InlineData(MediaBuckets.DamageDiagram)]
    [InlineData(MediaBuckets.GarageCarPhoto)]
    [InlineData(MediaBuckets.ApprovalImage)]
    [InlineData(MediaBuckets.RepairPhoto)]
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
    [InlineData(MediaBuckets.GarageDocuments)]
    [InlineData(MediaBuckets.Discharge)]
    [InlineData(MediaBuckets.Invoice)]
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

    private static IEnumerable<string> Buckets(IEnumerable<BucketRule> rules, PushTiming timing) =>
        rules.Where(r => r.Timing == timing).Select(r => r.Bucket).Order(StringComparer.Ordinal);
}
