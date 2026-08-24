using System.Net;
using System.Net.Http.Json;
using Api.Integrations.Next3;
using Api.Modules.Audit;
using Api.Modules.Declarations;
using Api.Modules.Media;
using Api.Modules.Notifications;
using Api.Outbox;
using Api.Tests.Media;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Integration;

/// <summary>
/// design.md §5.2's last two rows — G4 (slice 5.1).
///
/// Two things are being pinned here that nothing else reaches. **The bucket sets are state-dependent
/// now**, so the same POST is accepted or refused depending on where the declaration is in the
/// machine, and each refusal has to say something true about which. And **the repair buckets push
/// immediately**, under a visa read off the declaration row rather than supplied by the caller — the
/// slice's one place where a wrong value would file a garage's invoice under somebody else's claim.
/// </summary>
[Collection("api")]
public sealed class DeclarationRepairTests(ApiFixture fixture)
{
    [Fact]
    public async Task ARepairDocumentCannotBeAttachedBeforeTheRepairHasStarted()
    {
        using var garage = await fixture.CreateGarage();
        var id = await garage.CreateDraft();

        var response = await garage.UploadRepairPhoto(id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("repairs_not_in_progress", await ErrorCode(response));
        Assert.Empty(await fixture.DocumentRows(id));
    }

    [Fact]
    public async Task ADeclarationDocumentCannotBeAttachedOnceTheRepairHasStarted()
    {
        using var garage = await fixture.CreateGarage();
        using var officer = await fixture.CreateOfficer();
        var id = await fixture.RepairingDeclaration(garage, officer, ExpertFlows.NextVisa());

        // The other half of the split: the pre-decision buckets are OnApproval, so one attached now
        // would be a `deferred` row nothing will ever queue — the approve transition that drains that
        // set has already run.
        var response = await garage.UploadSurvey(id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("declaration_already_decided", await ErrorCode(response));
    }

    [Fact]
    public async Task ADeclarationDocumentIsStillAcceptedWhileTheOfficerIsReviewing()
    {
        using var garage = await fixture.CreateGarage();
        var id = await garage.SubmittedDeclaration();

        // `submitted` is not `decided`, and the gate tests `DecidedAt` rather than "state is Draft"
        // precisely so this stays possible: a garage that spots a missing photo while an officer
        // reviews can still add it, and it is queued with the rest at approval. The refusal code
        // would be a lie here, which is the other half of the reason.
        (await garage.UploadSurvey(id)).EnsureSuccessStatusCode();

        var rows = await fixture.DocumentRows(id);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, d => Assert.Equal(DocumentPushStatuses.Deferred, d.PushStatus));
    }

    [Fact]
    public async Task AGarageStillCannotUploadAnApprovalImageDuringTheRepair()
    {
        using var garage = await fixture.CreateGarage();
        using var officer = await fixture.CreateOfficer();
        var id = await fixture.RepairingDeclaration(garage, officer, ExpertFlows.NextVisa());

        // 4.1's authorization hole, re-checked through 5.1's rewritten gate: `approval_image` shares
        // this owner kind, so without a refusal a garage could sign its own approval. It belongs to
        // neither garage set, which is what makes the gate's fall-through the answer rather than an
        // oversight.
        var response = await MediaFlows.Upload(
            garage.Client,
            DeclarationFlows.DeclarationDocumentsPath(id),
            MediaFlows.Multipart(
                MediaBuckets.ApprovalImage, DocumentOrigins.Captured, TestImages.Png(1600, 1200),
                ImageHeader.Png, "PLACEHOLDER-forged-approval.png"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("bucket_not_allowed_for_caller", await ErrorCode(response));
    }

    [Fact]
    public async Task ARepairUploadIsQueuedUnderTheDeclarationsVisaAndReachesTheSurveyFolder()
    {
        using var garage = await fixture.CreateGarage();
        using var officer = await fixture.CreateOfficer();
        var visa = ExpertFlows.NextVisa();

        await fixture.ClearQueue();
        var id = await fixture.RepairingDeclaration(garage, officer, visa);
        await fixture.OutboxProcessor.RunOnce(CancellationToken.None);

        var before = fixture.DocumentsRecordedFor(visa);

        (await garage.UploadRepairPhoto(id)).EnsureSuccessStatusCode();
        (await garage.UploadInvoice(id, "invoice.pdf")).EnsureSuccessStatusCode();

        // Queued at upload, not at some later transition: `push_status = queued` with an outbox row
        // is written in the pipeline's one SaveChanges, which is §4's one-transaction rule holding for
        // free rather than by anything this endpoint does.
        var repairRows = (await fixture.DocumentRows(id))
            .Where(d => MediaBuckets.Repair.Contains(d.Bucket, StringComparer.Ordinal))
            .ToList();

        Assert.Equal(2, repairRows.Count);
        Assert.All(repairRows, d => Assert.Equal(DocumentPushStatuses.Queued, d.PushStatus));
        Assert.All(repairRows, d => Assert.NotNull(d.OutboxMessageId));

        await using (var db = fixture.CreateDbContext())
        {
            var ids = repairRows.Select(d => d.OutboxMessageId!.Value).ToList();
            var rows = await db.Set<Next3OutboxMessage>().AsNoTracking()
                .Where(m => ids.Contains(m.Id))
                .ToListAsync();

            Assert.Equal(2, rows.Count);
            Assert.All(rows, m => Assert.Equal(visa, m.VisaNo));
            Assert.All(rows, m => Assert.Equal(Next3OutboxOperations.UploadDocument, m.Operation));
        }

        // **Before the submit is pressed.** §5.2's terminal transition records that the garage
        // considers the job finished; it is not what sends the paperwork.
        await fixture.OutboxProcessor.RunOnce(CancellationToken.None);

        var received = fixture.FakeNext3().RecordedDocuments
            .Where(d => d.VisaNo == visa)
            .Skip(before)
            .ToList();

        Assert.Equal(2, received.Count);
        Assert.All(received, d => Assert.Equal(Next3Folders.Survey, d.Doc.Folder));

        var clientRefs = repairRows.Select(d => d.Id.ToString()).ToHashSet(StringComparer.Ordinal);
        Assert.All(received, d => Assert.Contains(d.ClientRef, clientRefs));

        // The placeholder codes for these buckets, never invented values (#12).
        Assert.Contains(received, d => d.Doc.DocType == "PLACEHOLDER-DOC-09");
        Assert.Contains(received, d => d.Doc.DocType == "PLACEHOLDER-DOC-11");
        Assert.Contains(received, d => d.Doc.FileName == "invoice.pdf");
    }

    [Fact]
    public async Task ADocumentSaysWhetherNext3HasActuallyAcknowledgedIt()
    {
        using var garage = await fixture.CreateGarage();
        using var officer = await fixture.CreateOfficer();
        var visa = ExpertFlows.NextVisa();

        await fixture.ClearQueue();
        var id = await fixture.RepairingDeclaration(garage, officer, visa);
        (await garage.UploadInvoice(id)).EnsureSuccessStatusCode();

        // `push_status` cannot answer this. §4 keeps live push state on the outbox row alone, so the
        // column reads `queued` from the moment of upload until §7.3 deletes the bytes a week later —
        // and the garage screen's "Sent to AXA" was a string nothing could ever produce. The DTO
        // answers it by joining the outbox at read time instead.
        var before = await Documents(garage, id);
        Assert.All(before, d => Assert.False(d.PushConfirmed));
        Assert.Contains(before, d => d.PushStatus == DocumentPushStatuses.Queued);

        await fixture.OutboxProcessor.RunOnce(CancellationToken.None);

        var after = await Documents(garage, id);
        Assert.All(after, d => Assert.True(d.PushConfirmed));

        // Still `queued` on the row: the two fields are answering different questions, and the point
        // of computing one of them is that there is no second copy to disagree.
        Assert.All(after, d => Assert.Equal(DocumentPushStatuses.Queued, d.PushStatus));
    }

    [Fact]
    public async Task SubmittingWithNothingFromTheRepairIsRefused()
    {
        using var garage = await fixture.CreateGarage();
        using var officer = await fixture.CreateOfficer();
        var id = await fixture.RepairingDeclaration(garage, officer, ExpertFlows.NextVisa());

        var empty = await garage.SubmitRepairDocs(id);
        Assert.Equal(HttpStatusCode.Conflict, empty.StatusCode);
        Assert.Equal("repair_documents_required", await ErrorCode(empty));

        // And the declaration already carries a survey document and an approval image from the chain
        // above, so a precondition over *all* documents would have passed here. The set has to be the
        // repair buckets or the gate means nothing.
        Assert.NotEmpty(await fixture.DocumentRows(id));
        Assert.Equal(DeclarationState.RepairsInProgress, (await fixture.DeclarationRow(id)).State);
    }

    [Fact]
    public async Task AnyOneRepairDocumentIsEnoughAndTheTransitionIsRecorded()
    {
        using var garage = await fixture.CreateGarage();
        using var officer = await fixture.CreateOfficer();
        var id = await fixture.RepairingDeclaration(garage, officer, ExpertFlows.NextVisa());

        var notificationsBefore = await NotificationCount();

        // A discharge alone, with no invoice: the BRD says "documents such like discharge, invoice",
        // so requiring one particular kind would be an invented rule (pass-2 review decision 5).
        (await garage.UploadDischarge(id)).EnsureSuccessStatusCode();

        var response = await garage.SubmitRepairDocs(id);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<StateBody>();
        Assert.Equal("repair_docs_submitted", body?.State);

        var row = await fixture.DeclarationRow(id);
        Assert.Equal(DeclarationState.RepairDocsSubmitted, row.State);
        Assert.NotNull(row.RepairDocsSubmittedAt);

        await using (var db = fixture.CreateDbContext())
        {
            var trail = await db.Set<AuditLog>().AsNoTracking()
                .Where(a => a.EntityId == id
                    && a.Action == AuditActions.DeclarationRepairDocsSubmitted)
                .ToListAsync();

            Assert.Single(trail);
            Assert.Equal(garage.User.Id, trail[0].ActorUserId);
        }

        // **Nobody is told.** §5.2 records the missing officer notification as a gap in the BRD; it is
        // asserted rather than left to inspection so adding one later is a decision, not a drive-by.
        Assert.Equal(notificationsBefore, await NotificationCount());
    }

    [Fact]
    public async Task TheTerminalStateHasNoWayOutAndTakesNoMoreMedia()
    {
        using var garage = await fixture.CreateGarage();
        using var officer = await fixture.CreateOfficer();
        var id = await fixture.RepairingDeclaration(garage, officer, ExpertFlows.NextVisa());

        (await garage.UploadInvoice(id)).EnsureSuccessStatusCode();
        (await garage.SubmitRepairDocs(id)).EnsureSuccessStatusCode();

        var again = await garage.SubmitRepairDocs(id);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("illegal_transition", await ErrorCode(again));

        var backwards = await garage.StartRepairs(id);
        Assert.Equal(HttpStatusCode.Conflict, backwards.StatusCode);
        Assert.Equal("illegal_transition", await ErrorCode(backwards));

        // Media stops too, on both sides of the split — §5.2 defines no state after this one, so a
        // document attached here belongs to no step of the flow.
        var repair = await garage.UploadRepairPhoto(id);
        Assert.Equal(HttpStatusCode.Conflict, repair.StatusCode);
        Assert.Equal("repairs_not_in_progress", await ErrorCode(repair));

        var survey = await garage.UploadSurvey(id);
        Assert.Equal(HttpStatusCode.Conflict, survey.StatusCode);
        Assert.Equal("declaration_already_decided", await ErrorCode(survey));
    }

    [Fact]
    public async Task G3ShowsTheGarageWhenItsPaperworkWasSent()
    {
        using var garage = await fixture.CreateGarage();
        using var officer = await fixture.CreateOfficer();
        var id = await fixture.RepairingDeclaration(garage, officer, ExpertFlows.NextVisa());

        (await garage.UploadInvoice(id)).EnsureSuccessStatusCode();
        (await garage.SubmitRepairDocs(id)).EnsureSuccessStatusCode();

        var detail = await garage.Client
            .GetFromJsonAsync<DeclarationDetailBodyDto>($"/api/garage/declarations/{id}");

        // G4Submitted's four-row timeline. The last of them had no way to reach the browser before
        // this slice, even though the column has existed since 4.1.
        Assert.NotNull(detail);
        Assert.NotNull(detail.SubmittedAt);
        Assert.NotNull(detail.DecidedAt);
        Assert.NotNull(detail.RepairsStartedAt);
        Assert.NotNull(detail.RepairDocsSubmittedAt);
    }

    [Fact]
    public async Task TheOfficerSeesTheRepairDocuments()
    {
        using var garage = await fixture.CreateGarage();
        using var officer = await fixture.CreateOfficer();
        var id = await fixture.RepairingDeclaration(garage, officer, ExpertFlows.NextVisa());

        (await garage.UploadInvoice(id, "invoice.pdf")).EnsureSuccessStatusCode();

        // O2 needs no change for this — the officer's document list is every document on the
        // declaration — but the DoD says the officer sees them, so it is asserted rather than assumed.
        var documents = await officer.Client
            .GetFromJsonAsync<List<DocumentBodyDto>>($"/api/officer/declarations/{id}/documents");

        Assert.NotNull(documents);
        Assert.Contains(documents, d => d.Bucket == MediaBuckets.Invoice && d.FileName == "invoice.pdf");
    }

    private static async Task<List<DocumentBodyDto>> Documents(Actor garage, Guid declarationId) =>
        (await garage.Client.GetFromJsonAsync<List<DocumentBodyDto>>(
            DeclarationFlows.DeclarationDocumentsPath(declarationId)))!;

    private async Task<int> NotificationCount()
    {
        await using var db = fixture.CreateDbContext();
        return await db.Set<Notification>().CountAsync();
    }

    private static async Task<string?> ErrorCode(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ErrorBody>())?.Error;

    private sealed record ErrorBody(string Error);

    private sealed record StateBody(string State);
}
