using System.Net;
using System.Net.Http.Json;
using Api.Integrations;
using Api.Modules.Broker;
using Api.Modules.Media;
using Api.Modules.Notifications;
using Api.Modules.PublicSurface;
using Api.Outbox;
using Api.Tests.Media;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Integration;

/// <summary>
/// design.md §5.3's public uploads and the two preconditions slice 5.3 adds to the submit — the
/// customer's half of Broker Option 2, on the only unauthenticated surface in the system (§3, §9.1).
/// </summary>
[Collection("api")]
public sealed class PublicUploadTests(ApiFixture fixture) : IDisposable
{
    // PublicRateLimitTests' pattern: the monitor is shared across the serialized collection, so a test
    // that lowers a cap restores it however it exits.
    private readonly PublicLinkOptions _original = Clone(fixture.PublicLink.CurrentValue);

    public void Dispose() => fixture.PublicLink.CurrentValue = _original;

    private static PublicLinkOptions Clone(PublicLinkOptions source) => new()
    {
        ValidityDays = source.ValidityDays,
        DeliveryChannel = source.DeliveryChannel,
        MaxFiles = source.MaxFiles,
        MaxFileMb = source.MaxFileMb,
        RateLimit = new PublicRateLimitOptions
        {
            PerIpPermitsPerMinute = source.RateLimit.PerIpPermitsPerMinute,
            PerTokenPermitsPerMinute = source.RateLimit.PerTokenPermitsPerMinute,
        },
    };

    private void SetCaps(int maxFiles, int maxFileMb)
    {
        var options = Clone(_original);
        options.MaxFiles = maxFiles;
        options.MaxFileMb = maxFileMb;
        fixture.PublicLink.CurrentValue = options;
    }

    [Fact]
    public async Task AnUploadedDocument_IsStoredAgainstTheRequest_WithNoActorAndNoPush()
    {
        var link = await fixture.IssueLink();
        using var customer = fixture.CreatePublicClient();
        await customer.GetAsync($"/public/{link.Token}");

        var response = await PublicLinkFlows.UploadPublicDocument(customer, link.Token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<PublicDocumentBodyDto>();
        Assert.NotNull(body);
        Assert.Equal(MediaBuckets.PublicDocument, body.Bucket);
        Assert.Equal("PLACEHOLDER-car-papers.pdf", body.FileName);

        await using var db = fixture.CreateDbContext();
        var document = await db.Documents.AsNoTracking().SingleAsync(d => d.Id == body.Id);

        Assert.Equal(DocumentOwnerKinds.BrokerRequest, document.OwnerKind);
        Assert.Equal(link.RequestId, document.OwnerId);

        // §5.3's public customer, in the data model: no actor at all. `document.created_by` has been
        // nullable since slice 2.3 for this caller and no other, and this is the first row to use it.
        Assert.Null(document.CreatedBy);

        // `PushTiming.Never`: no NEXT3 document type, no outbox row, `push_status = n/a`. The broker
        // module never touches NEXT3 (§5.3) and neither does the page in front of it.
        Assert.Null(document.DocType);
        Assert.Null(document.OutboxMessageId);
        Assert.Equal(DocumentPushStatuses.NotApplicable, document.PushStatus);

        // **Narrowed in slice 7.2, deliberately.** This read `Assert.Empty(...ToListAsync())` over the
        // whole `next3_outbox` table, which is a claim about the run rather than about this upload:
        // every suite in the serialized collection shares that table, so the assertion held only
        // while this class happened to run before any of them. Adding a class that seeds two hundred
        // rows turned it red without anything about the public surface changing. What the comment
        // above says — and what §7.1's `PushTiming.Never` actually promises — is that *this document*
        // has no push, which is what is asserted now.
        Assert.Empty(await db.Set<Next3OutboxMessage>().AsNoTracking()
            .Where(m => m.Payload.Contains(document.Id.ToString()))
            .ToListAsync());
    }

    /// <summary>
    /// §7.1's public row allows both provenances — a customer photographs an ID card as readily as
    /// they pick a scan of it — and `origin` records which it was on every row.
    /// </summary>
    [Fact]
    public async Task ACapturedDocument_IsAcceptedAndRecordsItsProvenance()
    {
        var link = await fixture.IssueLink();
        using var customer = fixture.CreatePublicClient();
        await customer.GetAsync($"/public/{link.Token}");

        var response = await PublicLinkFlows.UploadPublicDocument(
            customer, link.Token, DocumentOrigins.Captured, "PLACEHOLDER-id-card.jpg");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var documents = await fixture.BrokerDocumentRows(link.RequestId);
        Assert.Equal(DocumentOrigins.Captured, Assert.Single(documents).Origin);
    }

    /// <summary>
    /// The bucket allow-list, and it is load-bearing rather than tidy: `broker_document` shares this
    /// owner kind, so the §7.1 rules alone would accept it here — and `origin` is a claim the client
    /// makes, so capture-only would not have caught it either. Without the gate an anonymous token
    /// holder could file documents as the broker's own.
    /// </summary>
    [Fact]
    public async Task ThePublicPageCannotFileADocumentAsTheBrokersOwn()
    {
        var link = await fixture.IssueLink();
        using var customer = fixture.CreatePublicClient();
        await customer.GetAsync($"/public/{link.Token}");

        var response = await MediaFlows.Upload(
            customer,
            PublicLinkFlows.DocumentsPath(link.Token),
            MediaFlows.Multipart(
                MediaBuckets.BrokerDocument, DocumentOrigins.Uploaded, TestImages.Pdf(4_000),
                ImageHeader.Pdf, "PLACEHOLDER-forged.pdf"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("bucket_not_allowed_for_caller", await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        // Refused before the file is read, so it costs no blob and leaves no row.
        Assert.Empty(await fixture.BrokerDocumentRows(link.RequestId));
    }

    /// <summary>
    /// §9.1's "hard caps on file count per submission", and `PublicUploadCaps`' first production
    /// caller. `MaxFiles` is a cap on the submission, not on the request — each request carries one
    /// file — so only a count of what is already stored can enforce it.
    /// </summary>
    [Fact]
    public async Task TheFileCountCapIsEnforcedAcrossRequests_NotWithinOne()
    {
        SetCaps(maxFiles: 2, maxFileMb: _original.MaxFileMb);

        var link = await fixture.IssueLink();
        using var customer = fixture.CreatePublicClient();
        await customer.GetAsync($"/public/{link.Token}");

        for (var i = 0; i < 2; i++)
        {
            var allowed = await PublicLinkFlows.UploadPublicDocument(
                customer, link.Token, fileName: $"PLACEHOLDER-{i}.pdf");
            Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
        }

        var refused = await PublicLinkFlows.UploadPublicDocument(
            customer, link.Token, fileName: "PLACEHOLDER-3.pdf");

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("too_many_files", await refused.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        // The refusal is decided before anything is stored, so the third file left no trace.
        Assert.Equal(2, (await fixture.BrokerDocumentRows(link.RequestId)).Count);
    }

    /// <summary>
    /// §9.1's size cap. Both layers answer 413 — <c>PublicBodySizeMiddleware</c> on the way in and
    /// <c>PublicUploadCaps</c> in the handler — and the point of the test is that the *status* is the
    /// same whichever fires, because a customer who picked too big a file must be told the same thing
    /// either way.
    /// </summary>
    [Fact]
    public async Task AFileOverTheCapIs413()
    {
        SetCaps(maxFiles: _original.MaxFiles, maxFileMb: 1);

        var link = await fixture.IssueLink();
        using var customer = fixture.CreatePublicClient();
        await customer.GetAsync($"/public/{link.Token}");

        var response = await MediaFlows.Upload(
            customer,
            PublicLinkFlows.DocumentsPath(link.Token),
            MediaFlows.Multipart(
                MediaBuckets.PublicDocument, DocumentOrigins.Uploaded,
                TestImages.Pdf(2 * 1024 * 1024), ImageHeader.Pdf, "PLACEHOLDER-huge.pdf"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(await fixture.BrokerDocumentRows(link.RequestId));
    }

    /// <summary>
    /// The list is scoped to the public bucket, so a broker's own attachments are never handed back to
    /// a member of the public — the same reason the submit precondition counts on the bucket rather
    /// than the owner.
    /// </summary>
    [Fact]
    public async Task TheDocumentList_ShowsTheCustomersFilesOnly()
    {
        using var brokerClient = await fixture.CreateBrokerClient();
        var link = await fixture.IssueLink(brokerClient);
        using var customer = fixture.CreatePublicClient();
        await customer.GetAsync($"/public/{link.Token}");

        await PublicLinkFlows.UploadPublicDocument(customer, link.Token, fileName: "PLACEHOLDER-mine.pdf");

        // A broker document on the same request, written straight to the row so this test does not
        // depend on whether B2's upload gate would allow one on an Option 2 request.
        await fixture.SeedBrokerDocument(link.RequestId);

        var listed = await (await customer.GetAsync(PublicLinkFlows.DocumentsPath(link.Token)))
            .Content.ReadFromJsonAsync<List<PublicDocumentBodyDto>>();

        Assert.NotNull(listed);
        Assert.Equal("PLACEHOLDER-mine.pdf", Assert.Single(listed).FileName);
    }

    [Fact]
    public async Task Submit_WithoutADocument_IsRefused_AndLeavesTheTokenUsable()
    {
        var link = await fixture.IssueLink();
        using var customer = fixture.CreatePublicClient();
        await customer.GetAsync($"/public/{link.Token}");

        var refused = await customer.PostAsJsonAsync(
            $"/public/{link.Token}/submit", PublicLinkFlows.CompleteSubmission());

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("documents_required", await refused.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        await using (var db = fixture.CreateDbContext())
        {
            var token = await db.PublicLinkTokens.AsNoTracking()
                .SingleAsync(t => t.BrokerRequestId == link.RequestId);
            Assert.Null(token.LockedAt);
        }

        // 1.5's rule: a refused submission must not burn the link. The customer attaches the document
        // they forgot — and, since 6.1, photographs the car — and presses Send again.
        (await PublicLinkFlows.UploadPublicDocument(customer, link.Token)).EnsureSuccessStatusCode();
        await PublicLinkFlows.AttachCarShots(customer, link.Token);
        var retry = await customer.PostAsJsonAsync(
            $"/public/{link.Token}/submit", PublicLinkFlows.CompleteSubmission());
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    /// <summary>
    /// A broker's own attachment does not satisfy a rule about what the *customer* provided — which is
    /// why the count is on the bucket and not on the owner kind the two buckets share.
    /// </summary>
    [Fact]
    public async Task ABrokerDocumentDoesNotSatisfyTheCustomersDocumentRequirement()
    {
        var link = await fixture.IssueLink();
        await fixture.SeedBrokerDocument(link.RequestId);

        using var customer = fixture.CreatePublicClient();
        await customer.GetAsync($"/public/{link.Token}");

        var refused = await customer.PostAsJsonAsync(
            $"/public/{link.Token}/submit", PublicLinkFlows.CompleteSubmission());

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("documents_required", await refused.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The type list is #14's configuration, and the recipient B4 later resolves is keyed on it — so
    /// an off-list type accepted here would produce a `ready_to_send` request that Send email cannot
    /// deliver, discovered by the broker with the customer already gone.
    /// </summary>
    [Fact]
    public async Task Submit_WithAnUnknownInsuranceType_IsRefused_AndLeavesTheTokenUsable()
    {
        var link = await fixture.IssueLink();
        using var customer = fixture.CreatePublicClient();
        await PublicLinkFlows.OpenAndAttach(customer, link.Token);

        var refused = await customer.PostAsJsonAsync(
            $"/public/{link.Token}/submit",
            PublicLinkFlows.CompleteSubmission("PLACEHOLDER-NOT-A-TYPE"));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("unknown_insurance_type", await refused.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        await using (var db = fixture.CreateDbContext())
        {
            var token = await db.PublicLinkTokens.AsNoTracking()
                .SingleAsync(t => t.BrokerRequestId == link.RequestId);
            Assert.Null(token.LockedAt);
        }

        var retry = await customer.PostAsJsonAsync(
            $"/public/{link.Token}/submit", PublicLinkFlows.CompleteSubmission());
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    /// <summary>
    /// §8's "Option 2 file ready to send → Push → Broker". The notification is issued from
    /// <c>Api.Modules.Broker</c> because resolving the broker's devices and email means reading
    /// <c>Users</c>, which architecture rule 2 forbids the public module from doing — the public
    /// endpoint hands over an id and learns nothing.
    /// </summary>
    [Fact]
    public async Task ASubmission_NotifiesTheBroker_WithADeepLinkToB4()
    {
        using var broker = await fixture.CreateBroker();
        var link = await fixture.IssueLink(broker.Client);
        using var customer = fixture.CreatePublicClient();
        await PublicLinkFlows.OpenAndAttach(customer, link.Token);

        (await customer.PostAsJsonAsync(
            $"/public/{link.Token}/submit", PublicLinkFlows.CompleteSubmission()))
            .EnsureSuccessStatusCode();

        await using var db = fixture.CreateDbContext();

        // Scoped to *this* broker, not to the template: the collection is serialized but shared, and
        // every other Option 2 test in it writes one of these rows too. A `Single` over the template
        // alone passes in isolation and fails in the suite — which is how this one was written first.
        var notification = await db.Set<Notification>().AsNoTracking()
            .SingleAsync(n => n.Template == NotificationTemplates.BrokerRequestReady
                && n.RecipientUserId == broker.User.Id);

        // `/broker/{id}` is B4 — the review screen, not the list. A broker woken by this is being
        // asked to read one submission and press one button.
        Assert.Contains($"/broker/{link.RequestId}", notification.Payload ?? string.Empty,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A notification failure must not reach the customer. The submission is committed and the token
    /// is spent by the time the broker is told, so a push service that is down is the broker's problem
    /// and not a member of the public's — §5.3's commit-then-send ordering, third outing.
    /// </summary>
    [Fact]
    public async Task ASubmissionSucceedsEvenWhenTheBrokerCannotBeNotifiedAtAll()
    {
        var link = await fixture.IssueLink();
        using var customer = fixture.CreatePublicClient();
        await PublicLinkFlows.OpenAndAttach(customer, link.Token);

        // One `FakeBehavior` serves NEXT3 and all three senders, so this takes down the push *and*
        // the email fallback beneath it — every channel §8 has, which is the case the notifier's
        // log-and-swallow exists for (4.1's note about `WithNext3Down`, from the useful direction).
        var original = fixture.Fake.CurrentValue;
        HttpResponseMessage submit;
        try
        {
            fixture.Fake.CurrentValue = new FakeOptions { FailureRate = 1.0 };
            submit = await customer.PostAsJsonAsync(
                $"/public/{link.Token}/submit", PublicLinkFlows.CompleteSubmission());
        }
        finally
        {
            fixture.Fake.CurrentValue = original;
        }

        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
        Assert.Equal(BrokerRequestState.ReadyToSend, (await fixture.RequestRow(link.RequestId)).State);
    }

    // ── slice 6.1: §5.3's five mandatory car sides ───────────────────────────────────────────────

    /// <summary>
    /// The side **is** the bucket (slice 6.1). <c>MediaUploadTarget</c> has no side slot and the
    /// multipart contract reads only <c>bucket</c> and <c>origin</c>, so a car side is a §7.1 row
    /// rather than a wire field somebody could mistype into a sixth value nothing validates.
    /// </summary>
    [Theory]
    [InlineData(MediaBuckets.PublicCarFront)]
    [InlineData(MediaBuckets.PublicCarRear)]
    [InlineData(MediaBuckets.PublicCarLeft)]
    [InlineData(MediaBuckets.PublicCarRight)]
    [InlineData(MediaBuckets.PublicCarRoof)]
    public async Task ACarShot_IsStoredUnderItsOwnSideBucket(string bucket)
    {
        var link = await fixture.IssueLink();
        using var customer = fixture.CreatePublicClient();
        await customer.GetAsync($"/public/{link.Token}");

        var response = await PublicLinkFlows.UploadCarShot(customer, link.Token, bucket);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<PublicDocumentBodyDto>();
        Assert.NotNull(body);
        Assert.Equal(bucket, body.Bucket);

        await using var db = fixture.CreateDbContext();
        var document = await db.Documents.AsNoTracking().SingleAsync(d => d.Id == body.Id);

        Assert.Equal(bucket, document.Bucket);
        Assert.Equal(DocumentOwnerKinds.BrokerRequest, document.OwnerKind);
        Assert.Equal(link.RequestId, document.OwnerId);
        Assert.Equal(DocumentOrigins.Captured, document.Origin);

        // Still outside the NEXT3 pipeline entirely, like every other broker-owned bucket.
        Assert.Null(document.CreatedBy);
        Assert.Null(document.DocType);
        Assert.Null(document.OutboxMessageId);
        Assert.Equal(DocumentPushStatuses.NotApplicable, document.PushStatus);
    }

    /// <summary>
    /// The BRD's hard rule, on the surface where it matters most: a quotation priced from a photograph
    /// of somebody else's car is the failure this whole application exists to remove. <c>origin</c> is
    /// a claim the client makes, so §7.1's <c>AllowUpload: false</c> is the only thing that refuses it.
    /// </summary>
    [Fact]
    public async Task ACarShotCannotBeUploadedFromTheGallery()
    {
        var link = await fixture.IssueLink();
        using var customer = fixture.CreatePublicClient();
        await customer.GetAsync($"/public/{link.Token}");

        var response = await PublicLinkFlows.UploadCarShot(
            customer, link.Token, MediaBuckets.PublicCarFront, DocumentOrigins.Uploaded);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "upload_not_allowed_for_bucket",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        Assert.Empty(await fixture.BrokerDocumentRows(link.RequestId));
    }

    /// <summary>
    /// §5.3's "all 5 car shots present". Every one of the five is required — four sides and a missing
    /// roof is a quotation AXA cannot price — and the refusal **leaves the token alive** (1.5's rule),
    /// so the customer photographs the side they missed and presses Send again.
    ///
    /// The 400 does not name the missing side. The page holds the same list and computes it from its
    /// own document list, and §9.1's surface says as little as it can.
    /// </summary>
    [Theory]
    [InlineData(MediaBuckets.PublicCarFront)]
    [InlineData(MediaBuckets.PublicCarRoof)]
    public async Task Submit_WithADocumentButAMissingSide_IsRefused_AndLeavesTheTokenUsable(string missing)
    {
        var link = await fixture.IssueLink();
        using var customer = fixture.CreatePublicClient();
        await customer.GetAsync($"/public/{link.Token}");
        (await PublicLinkFlows.UploadPublicDocument(customer, link.Token)).EnsureSuccessStatusCode();

        foreach (var bucket in MediaBuckets.PublicCarShots.Where(b => b != missing))
        {
            (await PublicLinkFlows.UploadCarShot(customer, link.Token, bucket))
                .EnsureSuccessStatusCode();
        }

        var refused = await customer.PostAsJsonAsync(
            $"/public/{link.Token}/submit", PublicLinkFlows.CompleteSubmission());

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var body = await refused.Content.ReadAsStringAsync();
        Assert.Contains("car_photos_required", body, StringComparison.Ordinal);
        Assert.DoesNotContain(missing, body, StringComparison.Ordinal);

        await using (var db = fixture.CreateDbContext())
        {
            var token = await db.PublicLinkTokens.AsNoTracking()
                .SingleAsync(t => t.BrokerRequestId == link.RequestId);
            Assert.Null(token.LockedAt);
        }

        (await PublicLinkFlows.UploadCarShot(customer, link.Token, missing)).EnsureSuccessStatusCode();
        var retry = await customer.PostAsJsonAsync(
            $"/public/{link.Token}/submit", PublicLinkFlows.CompleteSubmission());
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    /// <summary>
    /// The two preconditions are **disjoint in both directions**, which is the whole reason the submit
    /// asks which buckets are filled rather than how many rows there are. A customer who photographs
    /// their car has not thereby sent their identity card.
    /// </summary>
    [Fact]
    public async Task Submit_WithFiveCarShotsAndNoDocument_IsRefusedForTheDocument()
    {
        var link = await fixture.IssueLink();
        using var customer = fixture.CreatePublicClient();
        await customer.GetAsync($"/public/{link.Token}");
        await PublicLinkFlows.AttachCarShots(customer, link.Token);

        var refused = await customer.PostAsJsonAsync(
            $"/public/{link.Token}/submit", PublicLinkFlows.CompleteSubmission());

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var body = await refused.Content.ReadAsStringAsync();
        Assert.Contains("documents_required", body, StringComparison.Ordinal);
        Assert.DoesNotContain("car_photos_required", body, StringComparison.Ordinal);
    }

    /// <summary>The whole §5.3 precondition set, met: one supporting document and all five sides.</summary>
    [Fact]
    public async Task Submit_WithAllFiveSidesAndADocument_IsAccepted_AndLocksTheLink()
    {
        var link = await fixture.IssueLink();
        using var customer = fixture.CreatePublicClient();
        await PublicLinkFlows.OpenAndAttach(customer, link.Token);

        var response = await customer.PostAsJsonAsync(
            $"/public/{link.Token}/submit", PublicLinkFlows.CompleteSubmission());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var db = fixture.CreateDbContext();
        var request = await db.BrokerRequests.AsNoTracking().SingleAsync(r => r.Id == link.RequestId);
        Assert.Equal(BrokerRequestState.ReadyToSend, request.State);

        var token = await db.PublicLinkTokens.AsNoTracking()
            .SingleAsync(t => t.BrokerRequestId == link.RequestId);
        Assert.NotNull(token.LockedAt);
    }

    /// <summary>
    /// §9.1's <c>MaxFiles</c> is a cap on the **submission**, and the five car sides are part of that
    /// submission — 5 shots plus 10 supporting at the placeholder cap of 15. The count has always been
    /// scoped to the owner rather than to a bucket, so this holds by construction; pinned here because
    /// "by construction" is exactly the kind of claim that stops being true quietly.
    /// </summary>
    [Fact]
    public async Task TheFileCountCapCountsCarShotsAndDocumentsTogether()
    {
        SetCaps(maxFiles: 6, maxFileMb: _original.MaxFileMb);

        var link = await fixture.IssueLink();
        using var customer = fixture.CreatePublicClient();
        await customer.GetAsync($"/public/{link.Token}");

        // Five shots and one document is exactly the cap.
        await PublicLinkFlows.AttachCarShots(customer, link.Token);
        (await PublicLinkFlows.UploadPublicDocument(customer, link.Token)).EnsureSuccessStatusCode();

        var refused = await PublicLinkFlows.UploadPublicDocument(
            customer, link.Token, fileName: "PLACEHOLDER-one-too-many.pdf");

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains(
            "too_many_files", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        Assert.Equal(6, (await fixture.BrokerDocumentRows(link.RequestId)).Count);
    }

    /// <summary>
    /// The list is what P1 computes its done marks and its "N of 5" from, so it has to carry the car
    /// shots as well as the documents — and still never a broker's own attachment.
    /// </summary>
    [Fact]
    public async Task TheDocumentList_CarriesTheCarShotsWithTheirBuckets()
    {
        using var brokerClient = await fixture.CreateBrokerClient();
        var link = await fixture.IssueLink(brokerClient);
        using var customer = fixture.CreatePublicClient();
        await PublicLinkFlows.OpenAndAttach(customer, link.Token);
        await fixture.SeedBrokerDocument(link.RequestId);

        var listed = await (await customer.GetAsync(PublicLinkFlows.DocumentsPath(link.Token)))
            .Content.ReadFromJsonAsync<List<PublicDocumentBodyDto>>();

        Assert.NotNull(listed);
        Assert.Equal(
            MediaBuckets.PublicCarShots.Order(StringComparer.Ordinal),
            listed.Select(d => d.Bucket)
                .Where(b => b != MediaBuckets.PublicDocument)
                .Order(StringComparer.Ordinal));

        Assert.Single(listed, d => d.Bucket == MediaBuckets.PublicDocument);
        Assert.DoesNotContain(listed, d => d.Bucket == MediaBuckets.BrokerDocument);
    }

    /// <summary>
    /// A retake **replaces** rather than adds (slice 6.1). §5.3's sides are one photograph each, and
    /// P1 offers "Take a photo" on a side that is already done, so this is one tap away rather than a
    /// corner case.
    ///
    /// Two rows would both reach AXA as attachments — <c>BrokerRequestEmail.Attachments</c> selects on
    /// the owner alone — with nothing on the email to say which is current, and both would consume
    /// §9.1's <c>MaxFiles</c> on a surface with no delete route.
    /// </summary>
    [Fact]
    public async Task RetakingASide_ReplacesThatSidesPhotograph()
    {
        var link = await fixture.IssueLink();
        using var customer = fixture.CreatePublicClient();
        await customer.GetAsync($"/public/{link.Token}");

        var first = await PublicLinkFlows.UploadCarShot(
            customer, link.Token, MediaBuckets.PublicCarFront, fileName: "PLACEHOLDER-blurry.jpg");
        first.EnsureSuccessStatusCode();
        var replaced = (await first.Content.ReadFromJsonAsync<PublicDocumentBodyDto>())!;

        var second = await PublicLinkFlows.UploadCarShot(
            customer, link.Token, MediaBuckets.PublicCarFront, fileName: "PLACEHOLDER-sharp.jpg");
        second.EnsureSuccessStatusCode();
        var kept = (await second.Content.ReadFromJsonAsync<PublicDocumentBodyDto>())!;

        var rows = await fixture.BrokerDocumentRows(link.RequestId);
        var front = Assert.Single(rows, d => d.Bucket == MediaBuckets.PublicCarFront);

        Assert.Equal(kept.Id, front.Id);
        Assert.Equal("PLACEHOLDER-sharp.jpg", front.FileName);
        Assert.DoesNotContain(rows, d => d.Id == replaced.Id);

        // The **row** goes and the bytes do not: §7.3's safe failure order, so the old photograph is
        // an unclaimed blob for the orphan sweep rather than something deleted inside a transaction
        // that might yet roll back.
        Assert.Null(front.BlobDeletedAt);
    }

    /// <summary>
    /// Retakes cost nothing against §9.1's <c>MaxFiles</c>, which is the half of the replacement that
    /// matters most: without it a customer who retook each side twice would reach the cap on a surface
    /// with no delete route, leaving a request nobody can send and nobody can repair.
    /// </summary>
    [Fact]
    public async Task RetakesDoNotConsumeTheFileCountCap()
    {
        SetCaps(maxFiles: 6, maxFileMb: _original.MaxFileMb);

        var link = await fixture.IssueLink();
        using var customer = fixture.CreatePublicClient();
        await customer.GetAsync($"/public/{link.Token}");

        // Every side photographed three times over — fifteen uploads against a cap of six.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await PublicLinkFlows.AttachCarShots(customer, link.Token);
        }

        (await PublicLinkFlows.UploadPublicDocument(customer, link.Token)).EnsureSuccessStatusCode();

        Assert.Equal(6, (await fixture.BrokerDocumentRows(link.RequestId)).Count);

        var submitted = await customer.PostAsJsonAsync(
            $"/public/{link.Token}/submit", PublicLinkFlows.CompleteSubmission());
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
    }

    /// <summary>
    /// The guarantee is the **index**, not the code that leans on it. A filtered unique index over the
    /// five car buckets is what makes a duplicate unrepresentable — CLAUDE.md's first recurring bug
    /// class, which says a read-then-write on a "may only happen once" rule is not a rule at all.
    ///
    /// Asserted by writing the second row directly, past every endpoint: this goes green only while
    /// the database itself refuses it, so removing `IsUnique()` from `DocumentConfiguration` turns it
    /// red while the replacement path above stays green and proves nothing.
    /// </summary>
    [Fact]
    public async Task TheDatabaseRefusesASecondPhotographOfOneSide()
    {
        var link = await fixture.IssueLink();
        using var customer = fixture.CreatePublicClient();
        await customer.GetAsync($"/public/{link.Token}");
        (await PublicLinkFlows.UploadCarShot(customer, link.Token, MediaBuckets.PublicCarFront))
            .EnsureSuccessStatusCode();

        await using var db = fixture.CreateDbContext();
        db.Documents.Add(new Document
        {
            Id = Guid.CreateVersion7(),
            OwnerKind = DocumentOwnerKinds.BrokerRequest,
            OwnerId = link.RequestId,
            Bucket = MediaBuckets.PublicCarFront,
            DocType = null,
            Origin = DocumentOrigins.Captured,
            ClarityResult = ClarityResults.Passed,
            BlobKey = $"broker_request/{link.RequestId:N}/{Guid.CreateVersion7():N}.jpg",
            ContentType = ImageHeader.Jpeg,
            FileName = "PLACEHOLDER-duplicate.jpg",
            SizeBytes = 64,
            PushStatus = DocumentPushStatuses.NotApplicable,
            CreatedAt = fixture.Time.GetUtcNow().UtcDateTime,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    /// <summary>
    /// And the other side of that index: the filter is scoped to the five, so the buckets that are
    /// legitimately many-per-request stay that way. A customer attaches an identity card *and* the car
    /// papers; an index that caught those too would refuse the second one.
    /// </summary>
    [Fact]
    public async Task TheUniquenessRuleDoesNotReachTheSupportingDocuments()
    {
        var link = await fixture.IssueLink();
        using var customer = fixture.CreatePublicClient();
        await customer.GetAsync($"/public/{link.Token}");

        (await PublicLinkFlows.UploadPublicDocument(customer, link.Token, fileName: "PLACEHOLDER-id.pdf"))
            .EnsureSuccessStatusCode();
        (await PublicLinkFlows.UploadPublicDocument(customer, link.Token, fileName: "PLACEHOLDER-papers.pdf"))
            .EnsureSuccessStatusCode();

        var rows = await fixture.BrokerDocumentRows(link.RequestId);
        Assert.Equal(2, rows.Count(d => d.Bucket == MediaBuckets.PublicDocument));
    }

    /// <summary>
    /// §5.3's carried-across guard, finally raced (slice 7.1). The upload handler writes
    /// <c>broker_request.state</c> back unchanged so that a file admitted while the link was open
    /// **fails** if the submit commits underneath it — rather than committing afterwards, attached to
    /// a request the broker has already reviewed, carried by no email, and then deleted by
    /// <c>BrokerMediaCleanupTask</c> on the send's clock. Bytes a customer watched upload, destroyed
    /// silently, on a surface with no account and no receipt.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The window is real but narrow — <c>PublicEndpoints</c>' own comment calls it "the microseconds
    /// between that read and its commit" — so it is held open deliberately, at the one point inside it
    /// that a test can reach: <see cref="GatingBlobStore"/> stops the upload inside
    /// <c>IBlobStore.Put</c>, which is after the handler has resolved a live token, counted the files,
    /// passed the bucket gate and enlisted in the concurrency check, and before anything is committed.
    /// **The card's mechanism — a gated multipart body — was built first and measured not to work**;
    /// the reason is written up on <see cref="GatingBlobStore"/> and is worth reading before touching
    /// this.
    /// </para>
    /// <para>
    /// The blob assertion is what keeps this test about the concurrency token rather than about the
    /// gate. If the submit had landed first, <c>Resolve</c> would answer 404 on a locked token and
    /// **no blob would exist at all** — the same status code from a completely different mechanism,
    /// and a green test proving nothing. A new blob says the handler got as far as <c>Put</c> and lost
    /// the race at <c>SaveChanges</c>. Confirmed the other way by deleting the <c>IsModified</c> line,
    /// which turns this red.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnUploadInFlightWhenTheSubmitCommits_DiesAsTheUniform404()
    {
        var link = await fixture.IssueLink();
        using var customer = fixture.CreatePublicClient();

        // The full journey first: without a supporting document and all five sides the submit is a
        // 400 and the race never happens.
        await PublicLinkFlows.OpenAndAttach(customer, link.Token);
        var settled = (await fixture.BrokerDocumentRows(link.RequestId)).Select(d => d.BlobKey).ToList();
        Assert.Equal(6, settled.Count);

        var before = fixture.Blobs.Keys.ToHashSet(StringComparer.Ordinal);

        // Scoped to this request's own prefix, so the gate cannot be tripped by anything else.
        using var gate = fixture.BlobGate.GateNextPutUnder(
            $"{DocumentOwnerKinds.BrokerRequest}/{link.RequestId:N}/");

        // A second supporting document rather than a car-side retake, so `ReplacePreviousCarShot`
        // stays out of the picture and the only thing under test is the token.
        var upload = PublicLinkFlows.UploadPublicDocument(
            customer, link.Token, fileName: "PLACEHOLDER-in-flight.pdf");
        await gate.Reached;

        var submit = await customer.PostAsJsonAsync(
            $"/public/{link.Token}/submit", PublicLinkFlows.CompleteSubmission());
        submit.EnsureSuccessStatusCode();

        gate.Release();
        using var response = await upload;

        // It leaves through the same door as every other dead token — not a coded 409 like the
        // broker's equivalent, and above all not a 500, which §9.1 has already been broken by once
        // (slice 5.3's torn read).
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());

        // Nothing was recorded: the six from the journey, and no seventh.
        var rows = await fixture.BrokerDocumentRows(link.RequestId);
        Assert.Equal(6, rows.Count);

        // §7.3's safe failure order, both halves. The bytes are on disk with no row claiming them —
        // deliberately, because deleting bytes whose row then survives a rollback is unrecoverable
        // while an unclaimed blob is garbage with a sweep behind it.
        var orphan = Assert.Single(fixture.Blobs.Keys.Except(before, StringComparer.Ordinal));
        Assert.True(await fixture.BlobExists(orphan));

        // And the sweep behind it. The grace window is scoped rather than waited out: the fake clock
        // is shared by the whole serialized collection and moving it a day forward to prove a
        // subtraction is the expensive half of an equivalent proof (`MediaFlows.BackdateSentAt`
        // exists for the same reason).
        await fixture.WithRetention(
            options => options.OrphanBlobHours = 0,
            async () =>
            {
                await fixture.Sweep();

                Assert.False(await fixture.BlobExists(orphan));

                // The other half, and the one a too-eager sweep would break: a live `document` row
                // still claims these, so a zero grace window must not touch them.
                foreach (var key in settled)
                {
                    Assert.True(await fixture.BlobExists(key), $"claimed blob {key} was swept");
                }
            });
    }
}
