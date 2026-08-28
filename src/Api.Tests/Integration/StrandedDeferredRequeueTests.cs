using Api.Integrations.Next3;
using Api.Modules.Media;
using Api.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Api.Tests.Integration;

/// <summary>
/// design.md §5.2's "known gap, carried", closed in slice 7.2 — the fifth sweep.
///
/// A garage's upload reads the declaration's state to decide whether the bucket is allowed and
/// commits the document row later in the same request. Slice 4.1 narrowed that window twice and said
/// out loud that the microseconds between approve's read of the deferred set and its commit cannot be
/// closed from either side. A file that lands in them is written <c>deferred</c> with no outbox row,
/// and the only transition that drains that set has already run: invisible on A2, structurally
/// excluded from §7.3's first sweep, spared by the orphan sweep, and silently never sent to AXA.
/// </summary>
[Collection("api")]
public sealed class StrandedDeferredRequeueTests(ApiFixture fixture)
{
    [Fact]
    public async Task ADeferredDocumentOnAnApprovedDeclarationIsRequeuedUnderItsVisa()
    {
        var (declarationId, visa) = await ApprovedDeclaration();
        var stranded = await SeedStrandedDocument(declarationId);

        var swept = await fixture.Cleanup.RunOnce(CancellationToken.None);

        Assert.Equal(1, swept["stranded_deferred"]);

        var requeued = await DocumentRow(stranded);
        Assert.Equal(DocumentPushStatuses.Queued, requeued.PushStatus);
        Assert.NotNull(requeued.OutboxMessageId);

        // Under *this declaration's* visa, and with the clientRef every other push uses — a re-queue
        // that invented either would file the photograph under the wrong claim, which is the failure
        // this project exists to remove.
        var message = await fixture.Row(requeued.OutboxMessageId!.Value);
        Assert.Equal(visa, message.VisaNo);
        Assert.Equal(Next3OutboxOperations.UploadDocument, message.Operation);
        Assert.Contains(stranded.ToString(), message.Payload, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The other half, and the one that keeps the sweep from being a bug of its own: while an officer
    /// is still reviewing, a <c>deferred</c> document is exactly where it should be. Pushing it would
    /// file a garage's photographs under a visa nobody has chosen — §5.2's whole reason for deferring.
    /// </summary>
    [Fact]
    public async Task ADeferredDocumentOnASubmittedDeclarationIsUntouched()
    {
        using var garage = await fixture.CreateGarage();
        var declarationId = await garage.SubmittedDeclaration();
        var document = Assert.Single(await fixture.DocumentRows(declarationId));

        Assert.Equal(DocumentPushStatuses.Deferred, document.PushStatus);

        await fixture.Cleanup.RunOnce(CancellationToken.None);

        var after = await DocumentRow(document.Id);
        Assert.Equal(DocumentPushStatuses.Deferred, after.PushStatus);
        Assert.Null(after.OutboxMessageId);
    }

    /// <summary>
    /// **Two passes at once produce one push, and the guard is <c>push_status</c> as a concurrency
    /// token rather than the read that precedes the write.**
    /// </summary>
    /// <remarks>
    /// <c>CleanupWorker</c> is a hosted service inside the API host, and §3 pins <c>minReplicas: 1</c>
    /// rather than <c>maxReplicas: 1</c> — so two replicas whose hourly ticks overlap really do select
    /// the same stranded rows. The unique index on <c>outbox_message_id</c> catches nothing here,
    /// because each writer generates its own Guid: it is a lost update, not a collision. The result
    /// without the token is one photograph pushed to NEXT3 twice under a <c>clientRef</c> whose
    /// dedupe is still #32, plus an orphan outbox row whose <c>sent</c> licenses no blob deletion.
    ///
    /// Verified by removing <c>IsConcurrencyToken()</c> from <c>DocumentConfiguration</c>, which turns
    /// this red while the two facts above stay green — CLAUDE.md's "verify by removing the guard".
    /// </remarks>
    [Fact]
    public async Task TwoConcurrentPassesRequeueTheDocumentExactlyOnce()
    {
        var (declarationId, _) = await ApprovedDeclaration();
        var stranded = await SeedStrandedDocument(declarationId);

        await Task.WhenAll(
            fixture.Cleanup.RunOnce(CancellationToken.None),
            fixture.Cleanup.RunOnce(CancellationToken.None));

        var requeued = await DocumentRow(stranded);
        Assert.Equal(DocumentPushStatuses.Queued, requeued.PushStatus);

        await using var db = fixture.CreateDbContext();
        var pushes = await db.Set<Next3OutboxMessage>().AsNoTracking()
            .CountAsync(m => m.Payload.Contains(stranded.ToString()));

        Assert.Equal(1, pushes);
    }

    private async Task<(Guid DeclarationId, string VisaNo)> ApprovedDeclaration()
    {
        using var garage = await fixture.CreateGarage();
        using var officer = await fixture.CreateOfficer();
        var visa = fixture.SeedClaim();
        var declarationId = await garage.SubmittedDeclaration();

        (await officer.UploadApprovalImage(declarationId)).EnsureSuccessStatusCode();
        (await officer.Approve(declarationId, visa)).EnsureSuccessStatusCode();

        return (declarationId, visa);
    }

    /// <summary>
    /// A document row in the shape the race produces: <c>deferred</c>, no outbox row, on a
    /// declaration that already has a visa.
    /// </summary>
    /// <remarks>
    /// Written straight to the table, for <c>BrokerFlows.SeedBrokerDocument</c>'s reason — the shape
    /// is by definition unreachable through the API, because every endpoint that could produce it is
    /// the one slice 4.1 narrowed. Going through the upload endpoint would test the gate instead of
    /// the sweep, and the gate correctly refuses.
    /// </remarks>
    private async Task<Guid> SeedStrandedDocument(Guid declarationId)
    {
        var id = Guid.CreateVersion7();

        await using var db = fixture.CreateDbContext();
        db.Documents.Add(new Document
        {
            Id = id,
            OwnerKind = DocumentOwnerKinds.Declaration,
            OwnerId = declarationId,
            Bucket = MediaBuckets.GarageDocuments,
            // Read from the placeholder config, never written here: a #12 document-type code is
            // client data, and Appendix A's rule is that a literal outside that file is a bug.
            DocType = fixture.Services.GetRequiredService<IOptions<Next3Options>>()
                .Value.DocTypes[MediaBuckets.Find(MediaBuckets.GarageDocuments)!.DocTypeKey!],
            Origin = DocumentOrigins.Uploaded,
            ClarityResult = ClarityResults.NotApplicable,
            BlobKey = $"declaration/{declarationId:N}/{id:N}.pdf",
            ContentType = "application/pdf",
            FileName = "PLACEHOLDER-late-arrival.pdf",
            SizeBytes = 1_024,
            PushStatus = DocumentPushStatuses.Deferred,
            CreatedAt = fixture.Time.GetUtcNow().UtcDateTime,
        });

        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Document> DocumentRow(Guid documentId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Documents.AsNoTracking().SingleAsync(d => d.Id == documentId);
    }
}
