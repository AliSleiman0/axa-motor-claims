using System.Net;
using System.Net.Http.Json;
using Api.Modules.Media;
using Api.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Integration;

/// <summary>
/// design.md §7.3's blob lifecycle, and the rule HANDOFF §3 predicts will be broken during a week-6
/// cleanup refactor:
///
/// > **No blob is deleted before its outbox row is `sent`.**
///
/// These are the tests that catch that refactor. The full walk — upload → document row → outbox row →
/// fake push → `sent` → cleanup-eligible — is slice 2.3's definition of done, and every way of
/// getting it wrong (`pending`, `failed`, inside the retention window) has its own case, because the
/// dangerous direction is deleting bytes NEXT3 never received.
/// </summary>
[Collection("api")]
public sealed class BlobLifecycleTests(ApiFixture fixture)
{
    [Fact]
    public async Task TheFullLifecycle_UploadToSentToCleanupEligible()
    {
        var (expert, assignment, _) = await Arrange();

        // Clear first: the dequeue claims a batch across the whole table and cannot be scoped to one
        // test's rows, so another test's leftovers would fill the batch (the 2.2 lesson).
        await fixture.ClearQueue();

        var body = await UploadOk(expert.Client, assignment);
        var document = await fixture.DocumentRow(body.Id);

        Assert.True(await fixture.BlobExists(document.BlobKey));

        // The worker pushes it to the fake NEXT3.
        await fixture.OutboxProcessor.RunOnce(CancellationToken.None);
        var message = await fixture.OutboxRowFor(document.Id);
        Assert.Equal(Next3OutboxStatuses.Sent, message.Status);
        Assert.NotNull(message.SentAt);

        // Just confirmed, so still inside Retention:BlobDays — the sweep must leave it alone.
        await fixture.Sweep();
        Assert.True(await fixture.BlobExists(document.BlobKey));
        Assert.Null((await fixture.DocumentRow(document.Id)).BlobDeletedAt);

        // Past the window (moved on the row rather than on the shared clock — see BackdateSentAt).
        await fixture.BackdateSentAt(message.Id, days: fixture.Retention.CurrentValue.BlobDays + 1);
        await fixture.Sweep();

        Assert.False(await fixture.BlobExists(document.BlobKey));

        // The metadata survives: NEXT3 owns the file now, but §9's "who uploaded which photo, when"
        // is ours to keep, and the row is what answers it.
        var swept = await fixture.DocumentRow(document.Id);
        Assert.NotNull(swept.BlobDeletedAt);
        Assert.Equal(document.BlobKey, swept.BlobKey);
        Assert.Equal(document.Origin, swept.Origin);
    }

    [Fact]
    public async Task APendingPush_KeepsItsBlob_HoweverLongItWaits()
    {
        // The rule, stated as a test. The push has never been attempted; the retention window is
        // irrelevant until NEXT3 has the file.
        var (expert, assignment, _) = await Arrange();

        var body = await UploadOk(expert.Client, assignment);
        var document = await fixture.DocumentRow(body.Id);
        var message = await fixture.OutboxRowFor(document.Id);
        Assert.Equal(Next3OutboxStatuses.Pending, message.Status);

        // A sent_at far in the past would make it eligible *if* the sweep keyed off time alone.
        await fixture.BackdateSentAt(message.Id, days: 3650);
        await fixture.Sweep();

        Assert.True(await fixture.BlobExists(document.BlobKey));
        Assert.Null((await fixture.DocumentRow(document.Id)).BlobDeletedAt);
    }

    [Fact]
    public async Task AFailedPush_KeepsItsBlobIndefinitely()
    {
        // §7.3: "`failed` rows retain their blobs indefinitely (they are what A2's Retry re-sends)".
        // Delete the bytes and A2's Retry button becomes a lie.
        var (expert, assignment, _) = await Arrange();

        await fixture.ClearQueue();
        var body = await UploadOk(expert.Client, assignment);
        var document = await fixture.DocumentRow(body.Id);

        await fixture.WithOutboxOptions(
            options => options.MaxAttempts = 1,
            () => fixture.WithNext3Down(() => fixture.OutboxProcessor.RunOnce(CancellationToken.None)));

        var message = await fixture.OutboxRowFor(document.Id);
        Assert.Equal(Next3OutboxStatuses.Failed, message.Status);
        Assert.Null(message.SentAt);

        await fixture.BackdateSentAt(message.Id, days: 3650);
        await fixture.Sweep();

        Assert.True(await fixture.BlobExists(document.BlobKey));
    }

    [Fact]
    public async Task ASentPushInsideItsRetentionWindow_KeepsItsBlob()
    {
        // The boundary from the other side: one day short of Retention:BlobDays.
        var (expert, assignment, _) = await Arrange();

        await fixture.ClearQueue();
        var body = await UploadOk(expert.Client, assignment);
        var document = await fixture.DocumentRow(body.Id);

        await fixture.OutboxProcessor.RunOnce(CancellationToken.None);
        var message = await fixture.OutboxRowFor(document.Id);
        Assert.Equal(Next3OutboxStatuses.Sent, message.Status);

        await fixture.BackdateSentAt(message.Id, days: fixture.Retention.CurrentValue.BlobDays - 1);
        await fixture.Sweep();

        Assert.True(await fixture.BlobExists(document.BlobKey));
    }

    [Fact]
    public async Task WhenTheCommitFails_TheBlobIsOrphanedAndNeitherRowSurvives()
    {
        // §7.3's chosen failure order, proven rather than asserted in a comment: "a blob without a row
        // is garbage the cleanup job sweeps; a row without a blob is an error surfaced at push time".
        // The second is the one that reaches AXA as a missing photo, so the first is what we accept.
        var (expert, assignment, _) = await Arrange();

        // A check-constraint violation on a second row in the same batch makes SQL Server roll back
        // the whole transaction — the realistic version of "the commit did not happen". Injected by
        // handing the fixture a document row the schema refuses.
        var before = fixture.Blobs.Keys.Count;

        await using (var db = fixture.CreateDbContext())
        {
            db.Documents.Add(new Document
            {
                Id = Guid.CreateVersion7(),
                OwnerKind = DocumentOwnerKinds.Assignment,
                OwnerId = assignment,
                Bucket = MediaBuckets.InsuredCarPhoto,
                Origin = DocumentOrigins.Captured,
                ClarityResult = ClarityResults.Passed,
                BlobKey = "PLACEHOLDER/orphan-probe",
                ContentType = ImageHeader.Jpeg,
                SizeBytes = 1,
                // 'queued' with no outbox row violates CK_document_push_status_outbox: the invariant
                // that stops a document existing that is never pushed and never cleaned up.
                PushStatus = DocumentPushStatuses.Queued,
                OutboxMessageId = null,
                CreatedAt = fixture.Time.GetUtcNow().UtcDateTime,
            });

            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        Assert.Equal(before, fixture.Blobs.Keys.Count);

        // And the real path still works afterwards, which is what makes the invariant safe to have.
        var body = await UploadOk(expert.Client, assignment);
        Assert.True(await fixture.BlobExists((await fixture.DocumentRow(body.Id)).BlobKey));
    }

    [Fact]
    public async Task AnOrphanBlobPastTheGraceWindow_IsSwept()
    {
        // §7.3: "a blob without a row is garbage the cleanup job sweeps". Without this, every upload
        // that died between the PUT and its transaction leaks a file for ever.
        const string OrphanKey = "assignment/PLACEHOLDER-orphan/PLACEHOLDER-orphan.jpg";
        using var content = new MemoryStream([0x01, 0x02, 0x03]);
        await fixture.Blobs.Put(OrphanKey, content, ImageHeader.Jpeg, CancellationToken.None);

        // OrphanBlobHours = 0 makes every blob older than "now" a candidate, which is the boundary
        // without moving the clock the whole serialized collection shares.
        await fixture.WithRetention(
            options => options.OrphanBlobHours = 0,
            async () =>
            {
                await fixture.Sweep();
                Assert.False(await fixture.BlobExists(OrphanKey));
            });
    }

    [Fact]
    public async Task AFreshOrphan_SurvivesTheGraceWindow()
    {
        // The reason the grace window exists: a blob written seconds ago is far more likely to be an
        // upload mid-transaction than garbage, and sweeping it would delete a live document's bytes.
        const string FreshKey = "assignment/PLACEHOLDER-fresh/PLACEHOLDER-fresh.jpg";
        using var content = new MemoryStream([0x01, 0x02, 0x03]);
        await fixture.Blobs.Put(FreshKey, content, ImageHeader.Jpeg, CancellationToken.None);

        await fixture.Sweep();

        Assert.True(await fixture.BlobExists(FreshKey));
    }

    [Fact]
    public async Task ALiveDocumentsBlob_IsNeverTakenForAnOrphan()
    {
        // The failure mode that would make the orphan sweep worse than not having one.
        var (expert, assignment, _) = await Arrange();
        var body = await UploadOk(expert.Client, assignment);
        var document = await fixture.DocumentRow(body.Id);

        await fixture.WithRetention(
            options => options.OrphanBlobHours = 0,
            async () =>
            {
                await fixture.Sweep();
                Assert.True(await fixture.BlobExists(document.BlobKey));
            });
    }

    private async Task<(MappedExpert Expert, Guid Assignment, string Visa)> Arrange()
    {
        var expert = await fixture.CreateMappedExpert();
        var visa = fixture.SeedClaim();
        await fixture.Inject(visa, expert.Next3Id, ExpertFlows.NextRef());

        await using var db = fixture.CreateDbContext();
        var assignment = await db.ExpertAssignments.AsNoTracking().SingleAsync(a => a.VisaNo == visa);
        return (expert, assignment.Id, visa);
    }

    private static async Task<DocumentBodyDto> UploadOk(HttpClient client, Guid assignment)
    {
        var response = await MediaFlows.UploadCarPhoto(client, assignment);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DocumentBodyDto>())!;
    }
}
