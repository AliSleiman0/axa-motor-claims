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
        Assert.Empty(await db.Set<Next3OutboxMessage>().AsNoTracking().ToListAsync());
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
        // they forgot and presses Send again.
        (await PublicLinkFlows.UploadPublicDocument(customer, link.Token)).EnsureSuccessStatusCode();
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
}
