using System.Net;
using System.Net.Http.Json;
using Api.Modules.Audit;
using Api.Modules.Media;
using Api.Outbox;
using Api.Tests.Media;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Integration;

/// <summary>
/// design.md §7's upload path end to end: §7.1's bucket rules, §7.2 item 5's server re-validation,
/// and §7.3's ordering — blob PUT first, then the document row and its outbox row in one transaction.
/// </summary>
[Collection("api")]
public sealed class MediaUploadTests(ApiFixture fixture)
{
    [Fact]
    public async Task AnUpload_WritesTheBlob_TheDocumentRow_AndItsOutboxRow()
    {
        var (expert, assignment, visa) = await Arrange();

        var response = await MediaFlows.UploadCarPhoto(expert.Client, assignment);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<DocumentBodyDto>();
        Assert.NotNull(body);

        var document = await fixture.DocumentRow(body.Id);
        Assert.Equal(DocumentOwnerKinds.Assignment, document.OwnerKind);
        Assert.Equal(assignment, document.OwnerId);
        Assert.Equal(MediaBuckets.InsuredCarPhoto, document.Bucket);
        Assert.Equal(DocumentOrigins.Captured, document.Origin);
        Assert.Equal(ClarityResults.Passed, document.ClarityResult);
        Assert.Equal(ImageHeader.Jpeg, document.ContentType);
        Assert.True(document.SizeBytes > 0);
        Assert.Equal(expert.User.Id, document.CreatedBy);
        Assert.Null(document.BlobDeletedAt);

        // The doc type comes from the placeholder map (#12), never from a literal in feature code.
        Assert.Equal("PLACEHOLDER-DOC-02", document.DocType);

        // The bytes really are in the transit container, and they are the bytes we sent.
        Assert.True(await fixture.BlobExists(document.BlobKey));
        Assert.Equal(document.SizeBytes, fixture.Blobs.Read(document.BlobKey)!.Length);

        var message = await fixture.OutboxRowFor(document.Id);
        Assert.Equal(Next3OutboxOperations.UploadDocument, message.Operation);
        Assert.Equal(Next3OutboxStatuses.Pending, message.Status);
        Assert.Equal(visa, message.VisaNo);

        // §5.1: "clientRef = document id". Stable across every retry of this push by construction.
        Assert.Contains(document.Id.ToString(), message.Payload, StringComparison.Ordinal);
        Assert.Contains(document.BlobKey, message.Payload, StringComparison.Ordinal);
        Assert.Contains("Expert documents", message.Payload, StringComparison.Ordinal);

        // §4's cross-column invariant: queued means there is a row carrying it.
        Assert.Equal(DocumentPushStatuses.Queued, document.PushStatus);
        Assert.Equal(message.Id, document.OutboxMessageId);
    }

    [Fact]
    public async Task AnUpload_IsAudited_WithItsProvenanceFlag()
    {
        // §9: "every media upload (who, which claim/declaration/request, when, origin flag)" — the
        // InfoSec answer to "who uploaded which photo", and the one a claims dispute turns on.
        var (expert, assignment, visa) = await Arrange();

        var body = await UploadOk(expert.Client, assignment);

        await using var db = fixture.CreateDbContext();
        var entry = await db.Set<AuditLog>().AsNoTracking()
            .SingleAsync(a => a.Action == AuditActions.DocumentUploaded && a.EntityId == body.Id);

        Assert.Equal(expert.User.Id, entry.ActorUserId);
        Assert.Contains(DocumentOrigins.Captured, entry.Detail, StringComparison.Ordinal);
        Assert.Contains(visa, entry.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACaptureOnlyBucket_RefusesAnUploadedFile_AndWritesNothing()
    {
        // The BRD's hard rule, at the surface a hostile or buggy client actually reaches.
        var (expert, assignment, _) = await Arrange();

        var response = await MediaFlows.Upload(
            expert.Client,
            assignment,
            MediaFlows.Multipart(
                MediaBuckets.InsuredCarPhoto, DocumentOrigins.Uploaded, TestImages.Jpeg(1600, 1200)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertError(response, "upload_not_allowed_for_bucket");

        // Refused before the blob PUT, so there is no stray file either.
        Assert.Equal(0, await fixture.DocumentCountFor(assignment));
        Assert.DoesNotContain(
            fixture.Blobs.Keys, k => k.Contains(assignment.ToString("N"), StringComparison.Ordinal));
    }

    [Fact]
    public async Task ADocumentBucket_AcceptsAnUploadedFile()
    {
        var (expert, assignment, _) = await Arrange();

        var response = await MediaFlows.Upload(
            expert.Client,
            assignment,
            MediaFlows.Multipart(
                MediaBuckets.InsuredDocuments, DocumentOrigins.Uploaded, TestImages.Pdf(2_000),
                ImageHeader.Pdf, "PLACEHOLDER-report.pdf"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<DocumentBodyDto>();
        Assert.Equal(DocumentOrigins.Uploaded, body!.Origin);

        // A PDF has no dimensions to measure, so the row records that rather than a verdict.
        Assert.Equal(ClarityResults.NotApplicable, body.ClarityResult);
    }

    [Fact]
    public async Task AnOversizeFile_IsRefused_AndLeavesNoBlobBehind()
    {
        var (expert, assignment, _) = await Arrange();

        await fixture.WithMediaOptions(
            options => options.MaxFileMb = 1,
            async () =>
            {
                var big = TestImages.Jpeg(1600, 1200, padTo: 1_400_000);

                var response = await MediaFlows.UploadCarPhoto(expert.Client, assignment, big);

                Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
                await AssertError(response, "file_too_large");

                Assert.Equal(0, await fixture.DocumentCountFor(assignment));

                // The cap trips mid-write, so a partial blob exists for a moment — the upload path
                // removes it rather than leaving it for the orphan sweep a day later.
                Assert.DoesNotContain(
                    fixture.Blobs.Keys, k => k.Contains(assignment.ToString("N"), StringComparison.Ordinal));
            });
    }

    [Fact]
    public async Task AFileAtTheCap_IsAccepted()
    {
        // The other side of the boundary — a cap that rejects everything would pass the test above.
        var (expert, assignment, _) = await Arrange();

        await fixture.WithMediaOptions(
            options => options.MaxFileMb = 1,
            async () =>
            {
                var body = await UploadOk(expert.Client, assignment, TestImages.Jpeg(1600, 1200, padTo: 900_000));
                Assert.Equal(900_000, body.SizeBytes);
            });
    }

    [Fact]
    public async Task ADisallowedContentType_IsRefused()
    {
        var (expert, assignment, _) = await Arrange();

        var response = await MediaFlows.Upload(
            expert.Client,
            assignment,
            MediaFlows.Multipart(
                MediaBuckets.InsuredCarPhoto, DocumentOrigins.Captured, TestImages.Pdf(),
                ImageHeader.Pdf, "PLACEHOLDER-doc.pdf"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        await AssertError(response, "content_type_not_allowed");
    }

    [Fact]
    public async Task APdfWearingAJpegContentType_IsCaughtByItsBytes()
    {
        // §7.2 item 5's whole point: the declared type is a claim, and the server checks the file.
        var (expert, assignment, _) = await Arrange();

        var response = await MediaFlows.Upload(
            expert.Client,
            assignment,
            MediaFlows.Multipart(
                MediaBuckets.InsuredCarPhoto, DocumentOrigins.Captured, TestImages.Pdf(),
                ImageHeader.Jpeg, "PLACEHOLDER-not-really.jpg"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        await AssertError(response, "content_type_mismatch");
        Assert.Equal(0, await fixture.DocumentCountFor(assignment));
    }

    [Fact]
    public async Task AnImageBelowTheResolutionFloor_IsRefused()
    {
        // The client gate should have caught this (§7.2 items 1-3). This is the server proving it
        // does not depend on the client having done so.
        var (expert, assignment, _) = await Arrange();

        var response = await MediaFlows.UploadCarPhoto(expert.Client, assignment, TestImages.Jpeg(640, 480));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertError(response, "image_too_small");
    }

    [Fact]
    public async Task TheFileMustFollowItsMetadata()
    {
        // The documented cost of not buffering: a bucket learned after the bytes have gone to storage
        // cannot decide whether those bytes were allowed. Refused rather than silently buffered.
        var (expert, assignment, _) = await Arrange();

        var response = await MediaFlows.Upload(
            expert.Client,
            assignment,
            MediaFlows.Multipart(
                MediaBuckets.InsuredCarPhoto, DocumentOrigins.Captured, TestImages.Jpeg(1600, 1200),
                fileFirst: true));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertError(response, "metadata_must_precede_file");
        Assert.Equal(0, await fixture.DocumentCountFor(assignment));
    }

    [Fact]
    public async Task AnUnknownBucket_IsRefused()
    {
        var (expert, assignment, _) = await Arrange();

        var response = await MediaFlows.Upload(
            expert.Client,
            assignment,
            MediaFlows.Multipart(
                "PLACEHOLDER-not-a-bucket", DocumentOrigins.Captured, TestImages.Jpeg(1600, 1200)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertError(response, "unknown_bucket");
    }

    [Fact]
    public async Task ARequestWithNoFile_IsRefused()
    {
        var (expert, assignment, _) = await Arrange();

        var response = await MediaFlows.Upload(
            expert.Client,
            assignment,
            MediaFlows.Multipart(MediaBuckets.InsuredCarPhoto, DocumentOrigins.Captured, file: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertError(response, "file_missing");
    }

    [Fact]
    public async Task AnotherExpertsAssignment_Is404_AndWritesNothing()
    {
        var (_, assignment, _) = await Arrange();
        using var intruder = await fixture.CreateMappedExpert();

        var response = await MediaFlows.UploadCarPhoto(intruder.Client, assignment);

        // Not 403: an expert must not learn which assignment ids exist by watching the status change.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, await fixture.DocumentCountFor(assignment));
    }

    [Fact]
    public async Task TheDocumentList_IsScopedToItsAssignment()
    {
        var (expert, assignment, _) = await Arrange();
        var (_, otherAssignment, _) = await Arrange();

        await UploadOk(expert.Client, assignment);
        await UploadOk(expert.Client, assignment, bucket: MediaBuckets.TpCarPhoto);

        var listed = await expert.Client.GetFromJsonAsync<List<DocumentBodyDto>>(
            $"/api/expert/assignments/{assignment}/documents");

        Assert.Equal(2, listed!.Count);
        Assert.All(listed, d => Assert.True(d.BlobRetained));
        Assert.Contains(listed, d => d.Bucket == MediaBuckets.TpCarPhoto);

        // And nothing leaks between assignments.
        var otherResponse = await expert.Client.GetAsync(
            new Uri($"/api/expert/assignments/{otherAssignment}/documents", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, otherResponse.StatusCode);
    }

    [Fact]
    public async Task E1_ShowsMediaCounts()
    {
        // §5.1: "E1 lists assignments newest-first with media counts". Deferred out of 2.1 because
        // the document table did not exist; this is the deferral being paid.
        using var expert = await fixture.CreateMappedExpert();
        var visa = fixture.SeedClaim();
        await fixture.Inject(visa, expert.Next3Id, ExpertFlows.NextRef());
        var assignment = await AssignmentId(visa);

        await UploadOk(expert.Client, assignment);
        await UploadOk(expert.Client, assignment, bucket: MediaBuckets.TpCarPhoto);

        var list = await expert.Client.GetFromJsonAsync<List<AssignmentListItemDto>>(
            "/api/expert/assignments");

        Assert.Equal(2, list!.Single(a => a.Id == assignment).MediaCount);
    }

    /// <summary>An expert with a claim and an assignment, delivered through the real 2.1 path.</summary>
    private async Task<(MappedExpert Expert, Guid Assignment, string Visa)> Arrange()
    {
        var expert = await fixture.CreateMappedExpert();
        var visa = fixture.SeedClaim();
        await fixture.Inject(visa, expert.Next3Id, ExpertFlows.NextRef());
        return (expert, await AssignmentId(visa), visa);
    }

    private async Task<Guid> AssignmentId(string visaNo)
    {
        await using var db = fixture.CreateDbContext();
        return (await db.ExpertAssignments.AsNoTracking().SingleAsync(a => a.VisaNo == visaNo)).Id;
    }

    private static async Task<DocumentBodyDto> UploadOk(
        HttpClient client, Guid assignment, byte[]? image = null, string? bucket = null)
    {
        var response = await MediaFlows.Upload(
            client,
            assignment,
            MediaFlows.Multipart(
                bucket ?? MediaBuckets.InsuredCarPhoto,
                DocumentOrigins.Captured,
                image ?? TestImages.Jpeg(1600, 1200)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DocumentBodyDto>())!;
    }

    private static async Task AssertError(HttpResponseMessage response, string expected)
    {
        var body = await response.Content.ReadFromJsonAsync<ErrorBody>();
        Assert.Equal(expected, body!.Error);
    }

    private sealed record ErrorBody(string Error);
}
