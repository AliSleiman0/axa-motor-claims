using System.Net.Http.Json;
using Api.Modules.Media;
using Api.Modules.Users;
using Api.Tests.Media;

namespace Api.Tests.Integration;

internal sealed record CreateLinkResponse(Guid RequestId, string Token, string Url, DateTime ExpiresAt);

internal sealed record PublicLinkViewDto(
    string State,
    DateTime ExpiresAt,
    int MaxFiles,
    int MaxFileMb,
    string? BrokerDisplayName,
    IReadOnlyList<string> InsuranceTypes);

/// <summary>Mirror of the API's PublicDocumentDto — names, buckets and sizes and nothing else.</summary>
internal sealed record PublicDocumentBodyDto(Guid Id, string Bucket, string? FileName, long SizeBytes);

internal static class PublicLinkFlows
{
    /// <summary>
    /// A complete, valid Option 2 submission — the six fields of §5.3. The insurance type is read
    /// from the placeholder config rather than written here: the real list is #14, and a client
    /// literal outside <c>appsettings.Placeholders.json</c> is a bug even in a test.
    /// </summary>
    public static object CompleteSubmission(string insuranceType = "PLACEHOLDER-TYPE-3") => new
    {
        insuredName = "PLACEHOLDER Insured",
        insuranceType,
        insuredAddress = "PLACEHOLDER Address 1",
        carValue = 25000m,
        estimatedPremium = 750m,
        effectiveDate = "2026-09-01",
    };

    /// <summary>Issues a link through the real B3 endpoint as a freshly created broker.</summary>
    public static async Task<CreateLinkResponse> IssueLink(this ApiFixture fixture, string? customerMobile = null)
    {
        using var broker = await fixture.CreateBrokerClient();
        return await fixture.IssueLink(broker, customerMobile);
    }

    public static async Task<CreateLinkResponse> IssueLink(
        this ApiFixture fixture, HttpClient broker, string? customerMobile = null)
    {
        var response = await broker.PostAsJsonAsync(
            "/api/broker/link-requests", new { customerMobile });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreateLinkResponse>())!;
    }

    public static async Task<HttpClient> CreateBrokerClient(this ApiFixture fixture)
    {
        var broker = await fixture.CreateUser(UserRole.Broker, UserStatus.Active);
        var client = fixture.CreateClient();
        return client.WithBearer((await fixture.Login(client, broker.Phone)).AccessToken);
    }

    /// <summary>
    /// Public calls carry a per-test source IP so §9.1's per-IP limiter partitions them apart —
    /// without it every test in the suite would share one bucket and throttle each other.
    /// </summary>
    public static HttpClient WithTestIp(this HttpClient client, string ip)
    {
        client.DefaultRequestHeaders.Remove(RemoteIpTestFilter.HeaderName);
        client.DefaultRequestHeaders.Add(RemoteIpTestFilter.HeaderName, ip);
        return client;
    }

    private static int _ipCounter;

    /// <summary>A source IP no other test is using.</summary>
    public static string NextTestIp()
    {
        var n = Interlocked.Increment(ref _ipCounter);
        return $"203.0.113.{n % 254 + 1}";
    }

    /// <summary>A public client on its own IP — the default for anything touching /public.</summary>
    public static HttpClient CreatePublicClient(this ApiFixture fixture) =>
        fixture.CreateClient().WithTestIp(NextTestIp());

    public static string DocumentsPath(string token) => $"/public/{token}/documents";

    /// <summary>
    /// A supporting document from the customer's phone (slice 5.3). A PDF by default, because a
    /// scanned car-papers document is the case §7.1's public row exists for; pass
    /// <see cref="DocumentOrigins.Captured"/> for the photographed one.
    /// </summary>
    public static Task<HttpResponseMessage> UploadPublicDocument(
        HttpClient customer,
        string token,
        string origin = DocumentOrigins.Uploaded,
        string fileName = "PLACEHOLDER-car-papers.pdf") =>
        MediaFlows.Upload(
            customer,
            DocumentsPath(token),
            origin == DocumentOrigins.Captured
                ? MediaFlows.Multipart(
                    MediaBuckets.PublicDocument, origin, TestImages.Jpeg(1600, 1200),
                    ImageHeader.Jpeg, fileName)
                : MediaFlows.Multipart(
                    MediaBuckets.PublicDocument, origin, TestImages.Pdf(4_000),
                    ImageHeader.Pdf, fileName));

    /// <summary>
    /// The whole customer journey up to but not including the submit: open the link, attach one
    /// supporting document. Since slice 5.3 a submission without one is refused
    /// <c>400 documents_required</c>, so **every** test that submits needs this first — which is the
    /// new precondition being enforced, not an assertion being relaxed.
    /// </summary>
    public static async Task OpenAndAttach(HttpClient customer, string token)
    {
        (await customer.GetAsync($"/public/{token}")).EnsureSuccessStatusCode();
        (await UploadPublicDocument(customer, token)).EnsureSuccessStatusCode();
    }
}
