using System.Net;
using System.Net.Http.Json;
using Api.Modules.Audit;
using Api.Modules.Broker;
using Api.Modules.Media;
using Api.Modules.Notifications;
using Api.Tests.Media;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Integration;

/// <summary>
/// design.md §5.3's Option 1 (slice 5.2) — a broker files a quotation request and the app emails it,
/// with the documents attached, to the AXA recipient for its insurance type.
///
/// Three things are pinned here that nothing else in the suite reaches. **The routing table is
/// configuration** (#13/#14), so every recipient assertion reads it rather than naming an address.
/// **The attachments come from blob storage**, which is a different question from "an email was sent"
/// and has its own test. And the whole module is on the far side of `PushTiming.Never`: no doc type,
/// no outbox row, no NEXT3.
/// </summary>
[Collection("api")]
public sealed class BrokerRequestTests(ApiFixture fixture)
{
    [Fact]
    public async Task AnUnknownInsuranceType_IsRefusedAgainstTheConfiguredList()
    {
        using var broker = await fixture.CreateBroker();

        var response = await broker.CreateRequest(insuranceType: "PLACEHOLDER-NOT-A-TYPE");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("unknown_insurance_type", await ErrorCode(response));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ANonPositiveAmount_IsRefused(decimal carValue)
    {
        using var broker = await fixture.CreateBroker();

        var response = await broker.CreateRequest(carValue: carValue);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_amount", await ErrorCode(response));
    }

    [Fact]
    public async Task AnIncompleteForm_IsRefused()
    {
        using var broker = await fixture.CreateBroker();

        var response = await broker.CreateRequest(insuredName: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("incomplete_request", await ErrorCode(response));
    }

    [Fact]
    public async Task ADraftCarriesTheSixFieldsAndTheBrokersName()
    {
        using var broker = await fixture.CreateBroker();
        var id = await broker.CreateRequestDraft();

        var detail = await broker.RequestDetail(id);
        Assert.Equal(BrokerRequestStates.Draft, detail.State);
        Assert.Equal(1, detail.Option);
        Assert.Equal(BrokerFlows.InsuranceType, detail.InsuranceType);
        Assert.Equal(25000m, detail.CarValue);
        Assert.Equal(new DateOnly(2026, 9, 1), detail.EffectiveDate);

        // The snapshot 5.3's public page reads. Written now so `broker_request` takes one migration,
        // and so `Api.Modules.PublicSurface` never has to read `Users` (architecture rule 2).
        var row = await fixture.RequestRow(id);
        Assert.Equal(broker.User.DisplayName, row.BrokerDisplayName);
    }

    [Fact]
    public async Task ABrokerDocument_IsStoredOutsideTheNext3PipelineEntirely()
    {
        using var broker = await fixture.CreateBroker();
        var id = await broker.CreateRequestDraft();

        var response = await broker.UploadBrokerDocument(id);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var rows = await fixture.BrokerDocumentRows(id);
        var document = Assert.Single(rows);

        // The three halves of PushTiming.Never, asserted separately because each is a different way
        // the pipeline could have leaked a broker's file towards NEXT3.
        Assert.Equal(DocumentPushStatuses.NotApplicable, document.PushStatus);
        Assert.Null(document.DocType);
        Assert.Null(document.OutboxMessageId);

        await using var db = fixture.CreateDbContext();
        Assert.Equal(0, await db.Set<Api.Outbox.Next3OutboxMessage>().CountAsync(m => m.VisaNo == string.Empty));

        Assert.True(await fixture.BlobExists(document.BlobKey));
    }

    [Fact]
    public async Task ADocumentCannotBeAttachedOnceTheRequestIsSubmitted()
    {
        using var broker = await fixture.CreateBroker();
        var id = await broker.CreateRequestDraft();
        (await broker.SubmitRequest(id)).EnsureSuccessStatusCode();

        var response = await broker.UploadBrokerDocument(id);

        // Not merely tidy: the submit builds the email from whatever is attached at that moment, so a
        // file added afterwards would be one nobody receives and nothing ever will — and §7.3 would
        // then delete its bytes on the send's retention clock.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("request_already_submitted", await ErrorCode(response));
        Assert.Empty(await fixture.BrokerDocumentRows(id));
    }

    [Fact]
    public async Task ADeclarationBucket_IsRefusedUnderABrokerRequest()
    {
        using var broker = await fixture.CreateBroker();
        var id = await broker.CreateRequestDraft();

        var response = await MediaFlows.Upload(
            broker.Client,
            BrokerFlows.DocumentsPath(id),
            MediaFlows.Multipart(
                MediaBuckets.ApprovalImage, DocumentOrigins.Captured, TestImages.Png(1600, 1200),
                ImageHeader.Png));

        // `unknown_bucket`, not the caller gate's code: the owner-kind check fires first, and it is
        // what stops a broker forging an officer's approval image under a row of their own. Asserted
        // rather than assumed — 4.2's lesson about authorization checks that look present.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("unknown_bucket", await ErrorCode(response));
    }

    [Fact]
    public async Task SubmitResolvesTheRecipientFromConfigurationAndAttachesEveryDocument()
    {
        using var broker = await fixture.CreateBroker();
        var id = await broker.CreateRequestDraft();
        (await broker.CaptureBrokerDocument(id, "id-card.jpg")).EnsureSuccessStatusCode();
        (await broker.UploadBrokerDocument(id, fileName: "car-papers.pdf")).EnsureSuccessStatusCode();

        var recipient = fixture.RecipientFor();
        var response = await broker.SubmitRequest(id);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<BrokerActionBodyDto>();
        Assert.Equal(BrokerRequestStates.Submitted, body!.State);
        Assert.False(body.EmailFailed);
        Assert.Equal(recipient, body.Recipient);

        var email = fixture.Email.LastTo(recipient);
        Assert.NotNull(email);
        Assert.Equal(NotificationTemplates.BrokerRequestSubmitted, email.Template);

        // The six fields, in the body AXA reads.
        Assert.Contains("PLACEHOLDER Insured Three", email.Body, StringComparison.Ordinal);
        Assert.Contains(BrokerFlows.InsuranceType, email.Body, StringComparison.Ordinal);
        Assert.Contains("25000.00", email.Body, StringComparison.Ordinal);
        Assert.Contains("2026-09-01", email.Body, StringComparison.Ordinal);

        Assert.Equal(
            ["car-papers.pdf", "id-card.jpg"],
            email.Attachments.Select(a => a.FileName).Order(StringComparer.Ordinal));

        var row = await fixture.RequestRow(id);
        Assert.Equal(recipient, row.EmailRecipient);
        Assert.NotNull(row.EmailedAt);
        Assert.NotNull(row.SubmittedAt);
    }

    [Fact]
    public async Task TheAttachmentsAreReadFromBlobStorage_NotFromTheUpload()
    {
        using var broker = await fixture.CreateBroker();
        var id = await broker.CreateRequestDraft();
        (await broker.UploadBrokerDocument(id, fileName: "car-papers.pdf")).EnsureSuccessStatusCode();

        // Overwrite the stored bytes between the upload and the send. Nothing a real deployment does —
        // but it is the only way to tell "the email carried the bytes AXA's copy will be checked
        // against" from "the email carried whatever the upload happened to be holding". A sender
        // reading the upload stream passes every other test in this file.
        var document = Assert.Single(await fixture.BrokerDocumentRows(id));
        var replacement = TestImages.Pdf(9_000);
        await fixture.Blobs.Put(
            document.BlobKey, new MemoryStream(replacement), document.ContentType, CancellationToken.None);

        var recipient = fixture.RecipientFor();
        (await broker.SubmitRequest(id)).EnsureSuccessStatusCode();

        var attachment = Assert.Single(fixture.Email.LastTo(recipient)!.Attachments);
        Assert.Equal(replacement, attachment.Content);
    }

    [Fact]
    public async Task ASendFailure_LeavesTheRequestSubmittedAndVisiblyUnsent()
    {
        using var broker = await fixture.CreateBroker();
        var id = await broker.CreateRequestDraft();
        (await broker.UploadBrokerDocument(id)).EnsureSuccessStatusCode();

        var recipient = fixture.RecipientFor();
        var original = fixture.Fake.CurrentValue;
        HttpResponseMessage response;

        try
        {
            fixture.Fake.CurrentValue = new Api.Integrations.FakeOptions { FailureRate = 1.0 };
            response = await broker.SubmitRequest(id);
        }
        finally
        {
            fixture.Fake.CurrentValue = original;
        }

        // A 200, not an error. The request *is* filed — §5.3 commits the transition before the send
        // precisely so an unreachable mail server does not cost the broker the work again — and what
        // failed is the delivery, which the answer says out loud.
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<BrokerActionBodyDto>();
        Assert.Equal(BrokerRequestStates.Submitted, body!.State);
        Assert.True(body.EmailFailed);

        var row = await fixture.RequestRow(id);
        Assert.Equal(BrokerRequestState.Submitted, row.State);
        Assert.NotNull(row.SubmittedAt);

        // The claim is released, or B1 would read as delivered for ever and never offer the Resend.
        Assert.Null(row.EmailedAt);
        Assert.Null(row.EmailRecipient);

        await using (var db = fixture.CreateDbContext())
        {
            Assert.True(await db.Set<Notification>().AnyAsync(n =>
                n.RecipientAddress == recipient
                && n.Template == NotificationTemplates.BrokerRequestSubmitted
                && n.Status == NotificationStatuses.Failed));
        }

        // And B1 shows it, which is the whole reason the ordering is what it is.
        var listed = Assert.Single(await broker.ListRequests(), r => r.Id == id);
        Assert.Equal(BrokerRequestStates.Submitted, listed.State);
        Assert.Null(listed.EmailedAt);

        // Resend sends, and does not transition.
        var resent = await broker.ResendRequest(id);
        resent.EnsureSuccessStatusCode();
        Assert.False((await resent.Content.ReadFromJsonAsync<BrokerActionBodyDto>())!.EmailFailed);

        var after = await fixture.RequestRow(id);
        Assert.Equal(BrokerRequestState.Submitted, after.State);
        Assert.NotNull(after.EmailedAt);
        Assert.Equal(recipient, after.EmailRecipient);
        Assert.Equal(row.SubmittedAt, after.SubmittedAt);
    }

    [Fact]
    public async Task ADeliveredRequest_RefusesAResend()
    {
        using var broker = await fixture.CreateBroker();
        var id = await broker.CreateRequestDraft();
        (await broker.SubmitRequest(id)).EnsureSuccessStatusCode();

        var response = await broker.ResendRequest(id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("already_emailed", await ErrorCode(response));
    }

    [Fact]
    public async Task AnOption2RequestAwaitingReview_CannotBeMailedByResend()
    {
        // Otherwise a broker who preset the insurance type at B3 could mail the customer's submission
        // to AXA straight past §5.3's B4 review — and start that request's retention clock on
        // documents B4 has not sent. Found by the db-reviewer.
        using var broker = await fixture.CreateBroker();
        var issued = await broker.IssueLink();
        await fixture.MarkReadyToSend(issued.RequestId);

        var response = await broker.ResendRequest(issued.RequestId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("not_submitted", await ErrorCode(response));
        Assert.Null((await fixture.RequestRow(issued.RequestId)).EmailedAt);
    }

    [Fact]
    public async Task ASubmittedRequest_CannotBeSubmittedAgain()
    {
        using var broker = await fixture.CreateBroker();
        var id = await broker.CreateRequestDraft();
        (await broker.SubmitRequest(id)).EnsureSuccessStatusCode();

        var again = await broker.SubmitRequest(id);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("illegal_transition", await ErrorCode(again));
    }

    [Fact]
    public async Task OneBrokerSeesNothingOfAnothers()
    {
        using var owner = await fixture.CreateBroker();
        using var other = await fixture.CreateBroker();
        var id = await owner.CreateRequestDraft();

        // 404 on every route, not 403: a 403 confirms the id exists, and these ids are in URLs.
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync(
            new Uri(BrokerFlows.RequestPath(id), UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync(
            new Uri(BrokerFlows.DocumentsPath(id), UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.UploadBrokerDocument(id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.SubmitRequest(id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.ResendRequest(id)).StatusCode);

        Assert.DoesNotContain(await other.ListRequests(), r => r.Id == id);
        Assert.Contains(await owner.ListRequests(), r => r.Id == id);
    }

    [Fact]
    public async Task TheListShowsProvenanceAndIsNewestFirst()
    {
        using var broker = await fixture.CreateBroker();
        var first = await broker.CreateRequestDraft();
        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        var second = await broker.CreateRequestDraft();

        var rows = await broker.ListRequests();
        var ids = rows.Select(r => r.Id).ToList();
        Assert.True(ids.IndexOf(second) < ids.IndexOf(first));

        (await broker.CaptureBrokerDocument(first)).EnsureSuccessStatusCode();
        (await broker.UploadBrokerDocument(first)).EnsureSuccessStatusCode();

        var documents = await broker.RequestDocuments(first);
        Assert.Equal(2, documents.Count);
        Assert.Contains(documents, d => d.Origin == DocumentOrigins.Captured);
        Assert.Contains(documents, d => d.Origin == DocumentOrigins.Uploaded);

        Assert.Equal(2, Assert.Single(await broker.ListRequests(), r => r.Id == first).DocumentCount);
    }

    [Fact]
    public async Task ALapsedLinkRendersAsExpiredWithoutEverBeingWritten()
    {
        using var broker = await fixture.CreateBroker();
        var issued = await broker.IssueLink();

        Assert.Equal(
            BrokerRequestStates.LinkIssued,
            Assert.Single(await broker.ListRequests(), r => r.Id == issued.RequestId).State);

        await fixture.ExpireLink(issued.RequestId);

        Assert.Equal(
            BrokerRequestStates.Expired,
            Assert.Single(await broker.ListRequests(), r => r.Id == issued.RequestId).State);

        // Computed, never stored: §9.1 makes the token's own expiry the authority, and a state column
        // swept into agreement with it is a second answer that can disagree.
        Assert.Equal(BrokerRequestState.LinkIssued, (await fixture.RequestRow(issued.RequestId)).State);
    }

    [Fact]
    public async Task TheAuditTrailNamesTheCreateTheSubmitAndTheSend()
    {
        using var broker = await fixture.CreateBroker();
        var id = await broker.CreateRequestDraft();
        (await broker.SubmitRequest(id)).EnsureSuccessStatusCode();

        await using var db = fixture.CreateDbContext();
        var actions = await db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.EntityKind == AuditEntityKinds.BrokerRequest && a.EntityId == id)
            .Select(a => a.Action)
            .ToListAsync();

        // Three, not two: the send is separate from the transition because it can fail on its own,
        // and a Resend records one without the other.
        Assert.Contains(AuditActions.BrokerRequestCreated, actions);
        Assert.Contains(AuditActions.BrokerRequestSubmitted, actions);
        Assert.Contains(AuditActions.BrokerRequestEmailed, actions);
    }

    private static async Task<string?> ErrorCode(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ErrorBody>())?.Error;

    private sealed record ErrorBody(string Error);
}
