using System.Net;
using System.Net.Http.Json;
using Api.Modules.Declarations;
using Api.Modules.Media;
using Api.Modules.Notifications;
using Api.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Integration;

/// <summary>
/// design.md §5.2's machine over HTTP: the two views, the media that waits for approval, and the
/// notifications between them.
///
/// The transition table itself is covered by <see cref="Unit.DeclarationTransitionTests"/> against the
/// entity. What is here is everything the entity cannot see — ownership, the visa check, the flip to
/// `queued`, and whether anybody was told.
/// </summary>
[Collection("api")]
public sealed class DeclarationFlowTests(ApiFixture fixture)
{
    [Fact]
    public async Task ADraftNeedsAPlateAndNothingElse()
    {
        using var garage = await fixture.CreateGarage();

        var missing = await garage.Client.PostAsJsonAsync(
            "/api/garage/declarations", new { plateNo = "   ", insuredName = "PLACEHOLDER Insured" });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("plate_required", await ErrorCode(missing));

        // §1's interpretation: insured name and note are optional, and nothing else exists on the form.
        var id = await garage.CreateDraft(insuredName: null, note: null);
        var row = await fixture.DeclarationRow(id);

        Assert.Equal(DeclarationState.Draft, row.State);
        Assert.Null(row.VisaNo);
        Assert.Null(row.SubmittedAt);
        Assert.Null(row.OfficerUserId);
    }

    [Fact]
    public async Task AGarageDocumentIsStoredDeferredWithNoOutboxRow()
    {
        // The slice's central schema claim: §5.2's "nothing goes to NEXT3 before approval", enforced by
        // the absence of a row rather than by a policy.
        using var garage = await fixture.CreateGarage();
        var id = await garage.CreateDraft();

        var response = await garage.UploadSurvey(id, "invoice.pdf");
        response.EnsureSuccessStatusCode();

        var body = (await response.Content.ReadFromJsonAsync<DocumentBodyDto>())!;
        Assert.Equal(DocumentPushStatuses.Deferred, body.PushStatus);

        // The name is stored, which is the whole reason `file_name` exists: at approval — a different
        // request — there is no Content-Disposition left to read it from.
        Assert.Equal("invoice.pdf", body.FileName);

        var document = Assert.Single(await fixture.DocumentRows(id));
        Assert.Equal(DocumentPushStatuses.Deferred, document.PushStatus);
        Assert.Null(document.OutboxMessageId);
        Assert.Equal("invoice.pdf", document.FileName);

        await using var db = fixture.CreateDbContext();
        Assert.False(await db.Set<Next3OutboxMessage>().AnyAsync(m => m.Payload.Contains(document.Id.ToString())));
    }

    [Fact]
    public async Task AGarageCannotUploadAnApprovalImageForItself()
    {
        // Authorization, not tidiness: `approval_image` shares the declaration owner kind, so without
        // the caller allow-list a garage could attach its own "approval" and walk straight through the
        // gate that exists to make an officer sign the decision.
        using var garage = await fixture.CreateGarage();
        var id = await garage.CreateDraft();

        var response = await MediaFlows.Upload(
            garage.Client,
            DeclarationFlows.DeclarationDocumentsPath(id),
            MediaFlows.Multipart(
                MediaBuckets.ApprovalImage, DocumentOrigins.Captured, Media.TestImages.Png(1600, 1200),
                ImageHeader.Png, "PLACEHOLDER-forged-approval.png"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("bucket_not_allowed_for_caller", await ErrorCode(response));

        // Refused before the file was read, so there is no row and no blob to sweep up later.
        Assert.Empty(await fixture.DocumentRows(id));
    }

    [Fact]
    public async Task AnOfficerCannotUploadEvidence()
    {
        // §5.2 grants the officer review, not the ability to add to the garage's evidence.
        using var garage = await fixture.CreateGarage();
        using var officer = await fixture.CreateOfficer();
        var id = await garage.SubmittedDeclaration();

        var response = await MediaFlows.Upload(
            officer.Client,
            $"/api/officer/declarations/{id}/documents",
            MediaFlows.Multipart(
                MediaBuckets.GarageCarPhoto, DocumentOrigins.Captured, Media.TestImages.Jpeg(1600, 1200)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("bucket_not_allowed_for_caller", await ErrorCode(response));
    }

    [Fact]
    public async Task UploadingAfterTheDecisionIsRefused()
    {
        // The db-reviewer's finding, pinned. These buckets are OnApproval, so a document uploaded once
        // the approve transition has already run would be `deferred` with nothing left to enqueue it:
        // never pushed, absent from A2 (no outbox row exists to list), excluded from §7.3's first sweep,
        // and its blob retained for ever — a document that silently never reaches AXA, which is the one
        // outcome this project exists to prevent. Delete the DecidedAt guard and this goes red.
        using var garage = await fixture.CreateGarage();
        using var officer = await fixture.CreateOfficer();
        var visa = fixture.SeedClaim();
        var id = await garage.SubmittedDeclaration();

        (await officer.UploadApprovalImage(id)).EnsureSuccessStatusCode();
        (await officer.Approve(id, visa)).EnsureSuccessStatusCode();

        var late = await garage.UploadSurvey(id, "PLACEHOLDER-late.pdf");

        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        Assert.Equal("declaration_already_decided", await ErrorCode(late));
        Assert.DoesNotContain(await fixture.DocumentRows(id), d => d.PushStatus == DocumentPushStatuses.Deferred);
    }

    [Fact]
    public async Task ApprovalWithoutAnImageIsRefusedAndChangesNothing()
    {
        using var garage = await fixture.CreateGarage();
        using var officer = await fixture.CreateOfficer();
        var visa = fixture.SeedClaim();
        var id = await garage.SubmittedDeclaration();

        var response = await officer.Approve(id, visa, "PLACEHOLDER approved");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("approval_image_required", await ErrorCode(response));

        var row = await fixture.DeclarationRow(id);
        Assert.Equal(DeclarationState.Submitted, row.State);
        Assert.Null(row.VisaNo);
        Assert.Equal(0, await fixture.CommentCount(id));
    }

    [Fact]
    public async Task ApprovalUnderAVisaNext3DoesNotKnowIsRefused()
    {
        // 422 rather than 400: the request is well formed and the officer cannot fix it by editing it
        // — they have to go and create the visa in NEXT3 (#16) and search again. Without this gate the
        // failure would surface 26 h 36 m later as a `failed` push on A2.
        using var garage = await fixture.CreateGarage();
        using var officer = await fixture.CreateOfficer();
        var id = await garage.SubmittedDeclaration();
        (await officer.UploadApprovalImage(id)).EnsureSuccessStatusCode();

        var response = await officer.Approve(id, "PLACEHOLDER-VISA-NOT-REAL");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("visa_not_found", await ErrorCode(response));
        Assert.Equal(DeclarationState.Submitted, (await fixture.DeclarationRow(id)).State);
    }

    [Fact]
    public async Task WhenNext3IsDownApprovalIsRefusedAndNothingIsWritten()
    {
        // The reachable half of the atomicity question. `WithNext3Down` fails the visa check itself —
        // FakeBehavior is one instance shared by NEXT3 and all three senders — so there is no seam to
        // fail a request part way through over HTTP. Said plainly rather than dressed up: this proves
        // the 503 branch writes nothing, and DeclarationConcurrencyTests proves the transaction is
        // atomic, using the concurrency token as the seam that does exist.
        using var garage = await fixture.CreateGarage();
        using var officer = await fixture.CreateOfficer();
        var visa = fixture.SeedClaim();
        var id = await garage.SubmittedDeclaration();
        (await officer.UploadApprovalImage(id)).EnsureSuccessStatusCode();

        var response = await fixture.WithNext3Down(() => officer.Approve(id, visa, "PLACEHOLDER comment"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("next3_unavailable", await ErrorCode(response));

        var row = await fixture.DeclarationRow(id);
        Assert.Equal(DeclarationState.Submitted, row.State);
        Assert.Null(row.VisaNo);
        Assert.Null(row.DecidedAt);
        Assert.Equal(0, await fixture.CommentCount(id));
        Assert.All(
            await fixture.DocumentRows(id),
            d => Assert.Equal(DocumentPushStatuses.Deferred, d.PushStatus));
    }

    [Fact]
    public async Task RejectionIsTerminalAndPushesNothing()
    {
        using var garage = await fixture.CreateGarage();
        using var officer = await fixture.CreateOfficer();
        var visa = fixture.SeedClaim();
        var id = await garage.SubmittedDeclaration();

        (await officer.Reject(id, "PLACEHOLDER rejection reason")).EnsureSuccessStatusCode();

        var row = await fixture.DeclarationRow(id);
        Assert.Equal(DeclarationState.Rejected, row.State);
        Assert.Null(row.VisaNo);
        Assert.NotNull(row.DecidedAt);
        Assert.Equal(officer.User.Id, row.OfficerUserId);

        // The comment is stored — §5.2 says so — and the documents stay deferred, because diagram 02
        // pushes only on approval.
        Assert.Equal(1, await fixture.CommentCount(id));
        Assert.All(
            await fixture.DocumentRows(id),
            d =>
            {
                Assert.Equal(DocumentPushStatuses.Deferred, d.PushStatus);
                Assert.Null(d.OutboxMessageId);
            });

        // Terminal: no resubmit edge, and no second decision.
        Assert.Equal(HttpStatusCode.Conflict, (await garage.Submit(id)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await officer.Approve(id, visa)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await garage.StartRepairs(id)).StatusCode);
    }

    [Fact]
    public async Task TheGarageSeesCommentsOnlyWhenApproved()
    {
        // §1, and it will surprise users: the BRD grants comment visibility "in case of confirmation"
        // only, so a rejected declaration shows its status and nothing else. Filtered in the query, so
        // the rows never leave the database rather than being hidden by a DTO one refactor from
        // leaking them.
        using var garage = await fixture.CreateGarage();
        using var officer = await fixture.CreateOfficer();
        var rejected = await garage.SubmittedDeclaration();

        (await officer.Reject(rejected, "PLACEHOLDER why not")).EnsureSuccessStatusCode();

        var garageView = await Detail(garage, rejected);
        Assert.Equal(DeclarationStates.Rejected, garageView.State);
        Assert.Empty(garageView.Comments);
        Assert.Null(garageView.Claim);

        // The officer sees them in every state — they are who wrote them.
        var officerView = await officer.Client
            .GetFromJsonAsync<DeclarationDetailBodyDto>($"/api/officer/declarations/{rejected}");
        Assert.NotNull(officerView);
        Assert.Single(officerView.Comments);
    }

    [Fact]
    public async Task ApprovalUnlocksTheClaimDetailAndTheComments()
    {
        using var garage = await fixture.CreateGarage();
        using var officer = await fixture.CreateOfficer();
        var visa = fixture.SeedClaim();
        var id = await garage.SubmittedDeclaration();

        var before = await Detail(garage, id);
        Assert.Null(before.Claim);
        Assert.Empty(before.Comments);

        (await officer.UploadApprovalImage(id)).EnsureSuccessStatusCode();
        (await officer.Approve(id, visa, "PLACEHOLDER approved, proceed")).EnsureSuccessStatusCode();

        var after = await Detail(garage, id);
        Assert.Equal(DeclarationStates.Approved, after.State);
        Assert.Equal(visa, after.VisaNo);
        Assert.NotNull(after.Claim);
        Assert.Equal(visa, after.Claim.VisaNo);
        Assert.Equal("fresh", after.ClaimStatus);
        Assert.Single(after.Comments);
    }

    [Fact]
    public async Task SubmitNotifiesEveryActiveOfficerAndNobodyElse()
    {
        // §5.2: "no per-officer assignment — any officer may pick it up". So the fan-out is every
        // active officer, and an inactive one is not an officer for this purpose.
        using var garage = await fixture.CreateGarage();
        using var first = await fixture.CreateOfficer();
        using var second = await fixture.CreateOfficer();
        using var inactive = await fixture.CreateOfficer(active: false);

        var id = await garage.SubmittedDeclaration();

        Assert.Equal(1, await PushRowCount(first.User.Id));
        Assert.Equal(1, await PushRowCount(second.User.Id));
        Assert.Equal(0, await PushRowCount(inactive.User.Id));

        // And the transition itself does not depend on any of it.
        Assert.Equal(DeclarationState.Submitted, (await fixture.DeclarationRow(id)).State);
    }

    [Fact]
    public async Task WhenThePushFailsTheOfficerIsEmailedInstead()
    {
        // §8's "+ email fallback". The catch is deliberately broad — the fake sender throws
        // FakeTransientException, which is exactly what slice 4.3's demo raises when it turns
        // Fake:FailureRate up to 1.0 to "kill NEXT3". A catch naming only PushNotDeliveredException
        // would have let that fault a transition which had already committed.
        using var garage = await fixture.CreateGarage();
        using var officer = await fixture.CreateOfficer();
        var id = await garage.CreateDraft();
        (await garage.UploadCarPhoto(id)).EnsureSuccessStatusCode();

        await fixture.WithNext3Down(async () => (await garage.Submit(id)).EnsureSuccessStatusCode());

        // The transition survived the outage on both channels...
        Assert.Equal(DeclarationState.Submitted, (await fixture.DeclarationRow(id)).State);

        // ...and both channels were genuinely attempted for this officer.
        await using var db = fixture.CreateDbContext();
        var rows = await db.Set<Notification>().AsNoTracking()
            .Where(n => n.RecipientUserId == officer.User.Id
                && n.Template == NotificationTemplates.DeclarationSubmitted)
            .ToListAsync();

        Assert.Contains(rows, n => n.Channel == NotificationChannels.Push);
        Assert.Contains(rows, n => n.Channel == NotificationChannels.Email);
    }

    [Fact]
    public async Task AGarageCannotTouchAnotherGaragesDeclaration()
    {
        // §9's resource-level rule. Not-found and not-yours are the same bare 404 throughout, so a
        // garage cannot enumerate declaration ids by watching the status code change.
        using var owner = await fixture.CreateGarage();
        using var intruder = await fixture.CreateGarage();
        var id = await owner.CreateDraft();

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await intruder.Client.GetAsync(new Uri($"/api/garage/declarations/{id}", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.UploadCarPhoto(id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.Submit(id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.StartRepairs(id)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await intruder.Client.GetAsync(
                new Uri($"/api/garage/declarations/{id}/documents", UriKind.Relative))).StatusCode);

        // The owner's list never contains anyone else's, either.
        var mine = await intruder.Client
            .GetFromJsonAsync<List<DeclarationListBodyDto>>("/api/garage/declarations");
        Assert.NotNull(mine);
        Assert.DoesNotContain(mine, d => d.Id == id);
    }

    [Fact]
    public async Task TheOfficerInboxIsEveryGaragesSubmittedDeclarations()
    {
        // The deliberate asymmetry: the officer group is *not* ownership-scoped, because §5.2 defines
        // no queueing and we do not invent one.
        using var first = await fixture.CreateGarage();
        using var second = await fixture.CreateGarage();
        using var officer = await fixture.CreateOfficer();

        var a = await first.SubmittedDeclaration();
        var b = await second.SubmittedDeclaration();
        var draft = await first.CreateDraft();

        var inbox = await officer.Client
            .GetFromJsonAsync<List<OfficerDeclarationListBodyDto>>("/api/officer/declarations");

        Assert.NotNull(inbox);
        Assert.Contains(inbox, d => d.Id == a);
        Assert.Contains(inbox, d => d.Id == b);
        Assert.DoesNotContain(inbox, d => d.Id == draft);

        // The garage contact is on the row — it is what the officer needs to chase a declaration.
        var row = inbox.Single(d => d.Id == a);
        Assert.Equal("PLACEHOLDER Garage Contact", row.GarageName);
        Assert.Equal(1, row.MediaCount);
    }

    [Fact]
    public async Task TheVisaSearchNeedsATermAndSurvivesNext3BeingDown()
    {
        // §6.1: "neither term supplied returns empty, never every claim". This endpoint is
        // SearchClaims' first production caller — 3.2 removed the expert's, because a NEXT3-wide
        // search there would have shown an expert claims they were never assigned.
        using var officer = await fixture.CreateOfficer();
        var visa = fixture.SeedClaim(plateNo: DeclarationFlows.NextPlate());

        var blank = await officer.Client.GetAsync(new Uri("/api/officer/claims/search", UriKind.Relative));
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Equal("search_terms_required", await ErrorCode(blank));

        var hit = await officer.Client
            .GetFromJsonAsync<List<ClaimSearchBodyDto>>($"/api/officer/claims/search?visaNo={visa}");
        Assert.NotNull(hit);
        Assert.Contains(hit, c => c.VisaNo == visa);

        var down = await fixture.WithNext3Down(() => officer.Client.GetAsync(
            new Uri($"/api/officer/claims/search?visaNo={visa}", UriKind.Relative)));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, down.StatusCode);
        Assert.Equal("next3_unavailable", await ErrorCode(down));
    }

    [Fact]
    public async Task DeferredDocumentsSurviveBothRetentionSweeps()
    {
        // §7.3 needed no change for `deferred`, and this is why: sweep 1 requires an outbox row that is
        // `sent` and a deferred document has none at all, while sweep 2 spares any blob a live row
        // still claims. Both halves asserted, because the two sweeps fail in opposite directions.
        using var garage = await fixture.CreateGarage();
        var id = await garage.CreateDraft();
        (await garage.UploadSurvey(id)).EnsureSuccessStatusCode();

        var document = Assert.Single(await fixture.DocumentRows(id));
        Assert.True(await fixture.BlobExists(document.BlobKey));

        // Well past both windows: the retention window measured from a push that never happened, and
        // the orphan grace window measured from the blob's own age.
        await fixture.WithRetention(
            r =>
            {
                r.BlobDays = 0;
                r.OrphanBlobHours = 0;
            },
            async () =>
            {
                await fixture.Sweep();

                Assert.True(await fixture.BlobExists(document.BlobKey));
                var after = Assert.Single(await fixture.DocumentRows(id));
                Assert.Null(after.BlobDeletedAt);
                Assert.Equal(DocumentPushStatuses.Deferred, after.PushStatus);
            });
    }

    private static async Task<DeclarationDetailBodyDto> Detail(Actor garage, Guid id)
    {
        var body = await garage.Client
            .GetFromJsonAsync<DeclarationDetailBodyDto>($"/api/garage/declarations/{id}");
        Assert.NotNull(body);
        return body;
    }

    private async Task<int> PushRowCount(Guid userId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Set<Notification>()
            .CountAsync(n => n.RecipientUserId == userId
                && n.Channel == NotificationChannels.Push
                && n.Template == NotificationTemplates.DeclarationSubmitted);
    }

    private static async Task<string?> ErrorCode(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<ErrorBody>();
        return body?.Error;
    }

    private sealed record ErrorBody(string Error);
}
