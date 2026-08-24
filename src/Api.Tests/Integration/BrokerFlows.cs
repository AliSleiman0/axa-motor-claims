using System.Net.Http.Json;
using Api.Modules.Broker;
using Api.Modules.Media;
using Api.Modules.Users;
using Api.Tests.Media;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Integration;

/// <summary>Mirror of the API's BrokerRequestListItemDto (B1).</summary>
internal sealed record BrokerListBodyDto(
    Guid Id,
    int Option,
    string State,
    string? InsuredName,
    string? InsuranceType,
    string? CustomerMobile,
    DateTime CreatedAt,
    DateTime? SubmittedAt,
    DateTime? EmailedAt,
    string? EmailRecipient,
    DateTime? LinkExpiresAt,
    int DocumentCount);

/// <summary>Mirror of the API's BrokerRequestDetailDto (B2).</summary>
internal sealed record BrokerDetailBodyDto(
    Guid Id,
    int Option,
    string State,
    string? InsuredName,
    string? InsuranceType,
    string? InsuredAddress,
    decimal? CarValue,
    decimal? EstimatedPremium,
    DateOnly? EffectiveDate,
    string? CustomerMobile,
    DateTime CreatedAt,
    DateTime? SubmittedAt,
    DateTime? EmailedAt,
    string? EmailRecipient,
    DateTime? LinkExpiresAt);

/// <summary>Mirror of the API's submit/resend answer.</summary>
internal sealed record BrokerActionBodyDto(string State, bool EmailFailed, string? Recipient);

internal static class BrokerFlows
{
    /// <summary>The first placeholder type, and the one `PLACEHOLDER-recipient-1` routes to (#13/#14).</summary>
    public const string InsuranceType = "MOTOR ALL RISK";

    public static async Task<Actor> CreateBroker(this ApiFixture fixture, string? email = null)
    {
        var user = await fixture.CreateUser(UserRole.Broker, UserStatus.Active);

        await using (var db = fixture.CreateDbContext())
        {
            db.BrokerProfiles.Add(new BrokerProfile
            {
                UserId = user.Id,
                IrisCode = $"PLACEHOLDER-IRIS-{user.Id:N}"[..30],
                Email = email ?? $"PLACEHOLDER-{user.Id:N}@example.invalid",
            });
            await db.SaveChangesAsync();
        }

        var client = fixture.CreateClient();
        client.WithBearer((await fixture.Login(client, user.Phone)).AccessToken);
        return new Actor(user, client);
    }

    public static string RequestPath(Guid requestId) => $"/api/broker/requests/{requestId}";

    public static string DocumentsPath(Guid requestId) => $"{RequestPath(requestId)}/documents";

    public static Task<HttpResponseMessage> CreateRequest(
        this Actor broker,
        string? insuredName = "PLACEHOLDER Insured Three",
        string? insuranceType = InsuranceType,
        string? insuredAddress = "PLACEHOLDER Address 1",
        decimal? carValue = 25000m,
        decimal? estimatedPremium = 1200m,
        DateOnly? effectiveDate = null) =>
        broker.Client.PostAsJsonAsync(
            "/api/broker/requests",
            new
            {
                insuredName,
                insuranceType,
                insuredAddress,
                carValue,
                estimatedPremium,
                effectiveDate = effectiveDate ?? new DateOnly(2026, 9, 1),
            });

    /// <summary>Creates a draft and returns its id, failing loudly if the create was refused.</summary>
    public static async Task<Guid> CreateRequestDraft(this Actor broker, string? insuranceType = InsuranceType)
    {
        var response = await broker.CreateRequest(insuranceType: insuranceType);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BrokerDetailBodyDto>())!.Id;
    }

    /// <summary>A broker's supporting document. Upload-allowed in §7.1, subject to the kill-switch.</summary>
    public static Task<HttpResponseMessage> UploadBrokerDocument(
        this Actor broker,
        Guid requestId,
        string origin = DocumentOrigins.Uploaded,
        string fileName = "PLACEHOLDER-car-papers.pdf") =>
        MediaFlows.Upload(
            broker.Client,
            DocumentsPath(requestId),
            MediaFlows.Multipart(
                MediaBuckets.BrokerDocument, origin, TestImages.Pdf(4_000), ImageHeader.Pdf, fileName));

    /// <summary>A captured one — the provenance that survives the kill-switch being off.</summary>
    public static Task<HttpResponseMessage> CaptureBrokerDocument(
        this Actor broker, Guid requestId, string fileName = "PLACEHOLDER-id-card.jpg") =>
        MediaFlows.Upload(
            broker.Client,
            DocumentsPath(requestId),
            MediaFlows.Multipart(
                MediaBuckets.BrokerDocument, DocumentOrigins.Captured, TestImages.Jpeg(1600, 1200),
                ImageHeader.Jpeg, fileName));

    public static Task<HttpResponseMessage> SubmitRequest(this Actor broker, Guid requestId) =>
        broker.Client.PostAsync(new Uri($"{RequestPath(requestId)}/submit", UriKind.Relative), null);

    public static Task<HttpResponseMessage> ResendRequest(this Actor broker, Guid requestId) =>
        broker.Client.PostAsync(new Uri($"{RequestPath(requestId)}/resend", UriKind.Relative), null);

    /// <summary>B4's Send email (slice 5.3) — Option 2's `ready_to_send` -> `sent`.</summary>
    public static Task<HttpResponseMessage> SendRequest(this Actor broker, Guid requestId) =>
        broker.Client.PostAsync(new Uri($"{RequestPath(requestId)}/send", UriKind.Relative), null);

    /// <summary>
    /// The whole Option 2 journey up to B4: this broker issues a link, a customer opens it, attaches
    /// one supporting document and submits. Returns the request and the (now locked) token.
    ///
    /// Driven through the **real** public endpoints rather than by writing `ready_to_send` into the
    /// row, because half of what B4's tests are asserting is that the customer's documents survive the
    /// crossing into the broker's email — and a seeded state would prove that of a request no customer
    /// ever touched.
    /// </summary>
    public static async Task<(Guid RequestId, string Token)> ReadyToSendRequest(
        this Actor broker, ApiFixture fixture, string insuranceType = InsuranceType)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var link = await fixture.IssueLink(broker.Client);
        using var customer = fixture.CreatePublicClient();

        await PublicLinkFlows.OpenAndAttach(customer, link.Token);
        (await customer.PostAsJsonAsync(
            $"/public/{link.Token}/submit", PublicLinkFlows.CompleteSubmission(insuranceType)))
            .EnsureSuccessStatusCode();

        return (link.RequestId, link.Token);
    }

    public static async Task<IReadOnlyList<BrokerListBodyDto>> ListRequests(this Actor broker)
    {
        var response = await broker.Client.GetAsync(new Uri("/api/broker/requests", UriKind.Relative));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<BrokerListBodyDto>>())!;
    }

    public static async Task<BrokerDetailBodyDto> RequestDetail(this Actor broker, Guid requestId)
    {
        var response = await broker.Client.GetAsync(new Uri(RequestPath(requestId), UriKind.Relative));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BrokerDetailBodyDto>())!;
    }

    public static async Task<IReadOnlyList<DocumentBodyDto>> RequestDocuments(this Actor broker, Guid requestId)
    {
        var response = await broker.Client.GetAsync(new Uri(DocumentsPath(requestId), UriKind.Relative));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<DocumentBodyDto>>())!;
    }

    /// <summary>B3, as this broker — so ownership assertions have a request that is genuinely theirs.</summary>
    public static async Task<CreateLinkResponse> IssueLink(
        this Actor broker, string? customerMobile = null, string? insuranceType = null)
    {
        var response = await broker.Client.PostAsJsonAsync(
            "/api/broker/link-requests", new { customerMobile, insuranceType });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreateLinkResponse>())!;
    }

    /// <summary>
    /// Puts an Option 2 request where §5.3's B4 review would find it, without building 5.3's public
    /// submission. Written through the entity, so it goes through the same edge the public page uses.
    /// </summary>
    public static async Task MarkReadyToSend(this ApiFixture fixture, Guid requestId)
    {
        await using var db = fixture.CreateDbContext();
        var request = await db.BrokerRequests.SingleAsync(r => r.Id == requestId);
        request.ReadyToSend(fixture.Time.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Moves a link's expiry into the past. Raw SQL rather than the shared clock: advancing that past
    /// the access-token TTL would sign every other test in this serialized collection out (2.1).
    /// </summary>
    public static async Task ExpireLink(this ApiFixture fixture, Guid requestId)
    {
        await using var db = fixture.CreateDbContext();
        var expiresAt = fixture.Time.GetUtcNow().UtcDateTime.AddDays(-1);
        await db.Database.ExecuteSqlAsync(
            $"UPDATE public_link_token SET expires_at = {expiresAt} WHERE broker_request_id = {requestId}");
    }

    public static async Task<BrokerRequest> RequestRow(this ApiFixture fixture, Guid requestId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.BrokerRequests.AsNoTracking().SingleAsync(r => r.Id == requestId);
    }

    /// <summary>
    /// A `broker_document` row written straight to the table (slice 5.3).
    ///
    /// Deliberately not through B2's upload endpoint: the tests that need this are asking what happens
    /// when a request carries a broker's file *and* a customer's, and B2's gate is draft-only, so an
    /// Option 2 request could not legitimately acquire one. Writing the row directly asks the question
    /// the assertion is about — does the public surface distinguish the two buckets — rather than
    /// whether some other endpoint would have allowed it.
    /// </summary>
    public static async Task SeedBrokerDocument(
        this ApiFixture fixture, Guid requestId, string fileName = "PLACEHOLDER-brokers-own.pdf")
    {
        await using var db = fixture.CreateDbContext();
        db.Documents.Add(new Document
        {
            Id = Guid.CreateVersion7(),
            OwnerKind = DocumentOwnerKinds.BrokerRequest,
            OwnerId = requestId,
            Bucket = MediaBuckets.BrokerDocument,
            DocType = null,
            Origin = DocumentOrigins.Uploaded,
            ClarityResult = ClarityResults.NotApplicable,
            BlobKey = $"broker_request/{requestId}/{Guid.CreateVersion7():N}.pdf",
            ContentType = "application/pdf",
            FileName = fileName,
            SizeBytes = 1_024,
            PushStatus = DocumentPushStatuses.NotApplicable,
            CreatedAt = fixture.Time.GetUtcNow().UtcDateTime,
        });

        await db.SaveChangesAsync();
    }

    public static async Task<List<Document>> BrokerDocumentRows(this ApiFixture fixture, Guid requestId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Documents.AsNoTracking()
            .Where(d => d.OwnerKind == DocumentOwnerKinds.BrokerRequest && d.OwnerId == requestId)
            .OrderBy(d => d.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Moves a delivered request's <c>emailed_at</c> back, so §7.3's broker window can be tested at its
    /// real boundary without advancing the shared clock past the access-token TTL (2.1's lesson).
    /// </summary>
    public static async Task BackdateEmailedAt(this ApiFixture fixture, Guid requestId, int days)
    {
        await using var db = fixture.CreateDbContext();
        var emailedAt = fixture.Time.GetUtcNow().UtcDateTime.AddDays(-days);
        await db.Database.ExecuteSqlAsync(
            $"UPDATE broker_request SET emailed_at = {emailedAt} WHERE id = {requestId}");
    }

    /// <summary>Marks a request's documents as swept, without running the sweep that would not run.</summary>
    public static async Task MarkBlobDeleted(this ApiFixture fixture, Guid requestId)
    {
        await using var db = fixture.CreateDbContext();
        var at = fixture.Time.GetUtcNow().UtcDateTime;
        await db.Database.ExecuteSqlAsync(
            $"UPDATE document SET blob_deleted_at = {at} WHERE owner_kind = 'broker_request' AND owner_id = {requestId}");
    }

    /// <summary>The recipient `MOTOR ALL RISK` routes to — read from config, never written here (#13).</summary>
    public static string RecipientFor(this ApiFixture fixture, string insuranceType = InsuranceType) =>
        fixture.Broker.CurrentValue.EmailRouting[insuranceType];

    /// <summary>
    /// Runs a scenario with the BRD's upload kill-switch thrown, then restores it. Restoring matters
    /// more here than for most options: the integration classes are one serialized collection, so a
    /// leaked `AllowUpload = false` would refuse uploads in every test that ran afterwards.
    /// </summary>
    public static async Task WithUploadSwitch(this ApiFixture fixture, bool allowUpload, Func<Task> scenario)
    {
        var original = fixture.Broker.CurrentValue;
        var patched = new BrokerOptions { AllowUpload = allowUpload };

        foreach (var type in original.InsuranceTypes)
        {
            patched.InsuranceTypes.Add(type);
        }

        foreach (var (type, recipient) in original.EmailRouting)
        {
            patched.EmailRouting[type] = recipient;
        }

        fixture.Broker.CurrentValue = patched;
        try
        {
            await scenario();
        }
        finally
        {
            fixture.Broker.CurrentValue = original;
        }
    }
}
