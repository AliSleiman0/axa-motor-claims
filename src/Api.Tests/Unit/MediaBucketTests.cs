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
    // Slice 5.2's §5.3 broker bucket. Upload allowed in the *table*; the BRD's kill-switch
    // (`Broker:AllowUpload`) is applied over this row by `BrokerUploadSwitch` and is deliberately not
    // baked into it, because the table is the static rule and the switch is deployment configuration.
    [InlineData(MediaBuckets.BrokerDocument, true, MediaKind.Document)]
    // Slice 5.3's §5.3 public bucket — the supporting documents a member of the public attaches from
    // an Option 2 link. Upload allowed, and unlike the row above **no kill-switch applies**: §7.1's
    // `Broker.AllowUpload` is written against the Broker Option 1 row and the public rows carry none.
    [InlineData(MediaBuckets.PublicDocument, true, MediaKind.Document)]
    // Slice 6.1's five car sides. **Capture-only, and here it is the BRD's own rule** rather than
    // 3.1's "there is no file to pick": these are car photographs, and the project exists because the
    // photograph has to be of the car in front of the person taking it. Images, so §7.2's server-side
    // resolution floor applies to each exactly as to an expert's.
    [InlineData(MediaBuckets.PublicCarFront, false, MediaKind.Image)]
    [InlineData(MediaBuckets.PublicCarRear, false, MediaKind.Image)]
    [InlineData(MediaBuckets.PublicCarLeft, false, MediaKind.Image)]
    [InlineData(MediaBuckets.PublicCarRight, false, MediaKind.Image)]
    [InlineData(MediaBuckets.PublicCarRoof, false, MediaKind.Image)]
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
    public void EveryBrokerBucketIsOutsideTheNext3PipelineEntirely()
    {
        // Slice 5.2's third owner kind, and **seven rows since slice 6.1** — the broker's own
        // documents, the ones a public customer attaches, and their five car sides, which share this
        // owner kind precisely so that B4's review and the Option 2 email reach all of them with one
        // query. Unchanged in 6.1 because it is pinned to `MediaBuckets.BrokerRequest` rather than to
        // a count: the five joined that named set, and this test followed them there. §5.3: "the broker module never
        // touches NEXT3 — its terminal act is an email routed by insurance type". So there is nothing
        // to say about a folder or a document type, and saying nothing is the assertion: a placeholder
        // code invented for a push that cannot happen is exactly the client data CLAUDE.md forbids.
        var brokerRules = MediaBuckets.AllRules
            .Where(r => r.OwnerKind == DocumentOwnerKinds.BrokerRequest)
            .ToList();

        Assert.Equal(MediaBuckets.BrokerRequest.Order(StringComparer.Ordinal), Buckets(brokerRules));

        Assert.All(brokerRules, rule =>
        {
            Assert.Equal(PushTiming.Never, rule.Timing);
            Assert.Null(rule.DocTypeKey);
            Assert.Null(rule.Next3Folder);
        });
    }

    [Fact]
    public void TheFiveCarShotsAreCaptureOnlyImagesUnderTheBrokerRequestOwner()
    {
        // §5.3's hard rule, as data: "all 5 car shots present (front/rear/left/right/roof,
        // capture-only, side selected at capture)". The side **is** the bucket — `MediaUploadTarget`
        // has no side slot and the multipart contract reads only `bucket` and `origin` — so a missing
        // or miscategorised row here is a car side a customer could upload from their gallery, or one
        // the submit could never require.
        //
        // Pinned to `MediaBuckets.PublicCarShots` rather than to a count of five, for the reason 5.1
        // gives about the declaration split: a sixth side has to be classified in that named set
        // rather than inherit whichever flags its author typed. **The order is asserted too** — P1
        // renders its dot list and its "N of 5" from it, and B4 reads the same sequence.
        Assert.Equal(
            [
                MediaBuckets.PublicCarFront,
                MediaBuckets.PublicCarRear,
                MediaBuckets.PublicCarLeft,
                MediaBuckets.PublicCarRight,
                MediaBuckets.PublicCarRoof,
            ],
            MediaBuckets.PublicCarShots);

        Assert.All(MediaBuckets.PublicCarShots, bucket =>
        {
            var rule = MediaBuckets.Find(bucket);

            Assert.NotNull(rule);
            Assert.Equal(DocumentOwnerKinds.BrokerRequest, rule.OwnerKind);
            Assert.False(rule.AllowUpload);
            Assert.Equal(MediaKind.Image, rule.Kind);
        });
    }

    [Fact]
    public void ThePublicCustomersBucketsAreTheOnesTheLinkMayWriteAndReadBack()
    {
        // The set the `/public/*` gate, the document list and the submit's two preconditions all read
        // (slice 6.1). It is the broker's own buckets **minus `broker_document`**, and that exclusion
        // is the load-bearing part: the two families share an owner kind, so the §7.1 rules alone
        // would let an anonymous caller file a document as the broker's own, and `origin` is a claim
        // the client makes rather than a fact.
        Assert.Equal(
            MediaBuckets.PublicCustomer.Order(StringComparer.Ordinal),
            MediaBuckets.BrokerRequest
                .Except([MediaBuckets.BrokerDocument], StringComparer.Ordinal)
                .Order(StringComparer.Ordinal));

        Assert.DoesNotContain(MediaBuckets.BrokerDocument, MediaBuckets.PublicCustomer);

        // And the half `documents_required` depends on: the supporting-document bucket is in the set
        // but is not one of the five, so a car shot can never satisfy it and a supporting document can
        // never satisfy `car_photos_required`.
        Assert.Contains(MediaBuckets.PublicDocument, MediaBuckets.PublicCustomer);
        Assert.DoesNotContain(MediaBuckets.PublicDocument, MediaBuckets.PublicCarShots);
    }

    [Fact]
    public void EveryBucketIsAccountedForByOwnerKind()
    {
        // The non-vacuity guard for the three tests above: together they must cover every rule, or a
        // bucket added under a fourth owner kind would be asserted by none of them and all three would
        // still read green. CLAUDE.md: "a guard that quietly stops covering new code is worse than
        // none".
        //
        // Slice 5.1 left this alone deliberately and said so. **Slice 5.2 had to widen it**, which is
        // the guard working rather than the guard being wrong: `broker_document` is the first bucket
        // under a third owner kind, so it arrived with a test of its own above. Slices 5.3 and 6.1
        // needed no change here — `public_document` and the five car sides reuse that same owner kind
        // — and both times that was checked rather than assumed.
        Assert.Equal(
            MediaBuckets.AllRules.Count,
            MediaBuckets.AllRules.Count(r =>
                r.OwnerKind == DocumentOwnerKinds.Assignment
                || r.OwnerKind == DocumentOwnerKinds.Declaration
                || r.OwnerKind == DocumentOwnerKinds.BrokerRequest));
    }

    [Fact]
    public void EveryBucketsTimingIsOneThePipelineHandles()
    {
        // MediaUploadService.Record switches on Timing and throws on anything unrecognised, and
        // PushStatusFor pairs each timing with the push status its check constraint expects.
        Assert.All(
            MediaBuckets.AllRules,
            rule => Assert.Contains(rule.Timing, Enum.GetValues<PushTiming>()));

        // **Rewritten in slice 5.2, deliberately.** This used to assert `DoesNotContain(… Never)` —
        // that PushTiming.Never had no bucket at all — which was the only thing stopping the mapping
        // rotting while it was unused. `broker_document` now carries it, so the negative is simply
        // false and keeping it would have meant either deleting the guard or the bucket.
        //
        // The positive form guards more than the negative one did: the Never set is pinned to
        // `MediaBuckets.BrokerRequest` rather than to a count, so a second no-push bucket has to be
        // classified there rather than inherit whichever timing its author typed — 5.1's lesson about
        // the declaration split, from the other side.
        Assert.Equal(
            MediaBuckets.BrokerRequest.Order(StringComparer.Ordinal),
            Buckets(MediaBuckets.AllRules, PushTiming.Never));

        // And the biconditional MediaUploadService.ResolveDocType now relies on: it decides whether to
        // demand a #12 placeholder by reading `DocTypeKey is null`, which is only the same question as
        // "does this bucket push?" while these two agree.
        Assert.All(
            MediaBuckets.AllRules,
            rule => Assert.Equal(rule.Timing == PushTiming.Never, rule.DocTypeKey is null));

        Assert.All(
            MediaBuckets.AllRules,
            rule => Assert.Equal(rule.Timing == PushTiming.Never, rule.Next3Folder is null));
    }

    [Theory]
    [InlineData(MediaBuckets.InsuredCarPhoto)]
    [InlineData(MediaBuckets.TpCarPhoto)]
    [InlineData(MediaBuckets.VoiceNote)]
    [InlineData(MediaBuckets.DamageDiagram)]
    [InlineData(MediaBuckets.GarageCarPhoto)]
    [InlineData(MediaBuckets.ApprovalImage)]
    [InlineData(MediaBuckets.RepairPhoto)]
    [InlineData(MediaBuckets.PublicCarFront)]
    [InlineData(MediaBuckets.PublicCarRear)]
    [InlineData(MediaBuckets.PublicCarLeft)]
    [InlineData(MediaBuckets.PublicCarRight)]
    [InlineData(MediaBuckets.PublicCarRoof)]
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
    [InlineData(MediaBuckets.BrokerDocument)]
    [InlineData(MediaBuckets.PublicDocument)]
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
        Buckets(rules.Where(r => r.Timing == timing));

    private static IEnumerable<string> Buckets(IEnumerable<BucketRule> rules) =>
        rules.Select(r => r.Bucket).Order(StringComparer.Ordinal);
}
