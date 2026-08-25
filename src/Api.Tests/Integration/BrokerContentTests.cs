using System.Net;
using System.Net.Http.Json;
using Api.Modules.Media;

namespace Api.Tests.Integration;

/// <summary>
/// B4's document-content endpoint (slice 6.1) — the broker's half of the same seam
/// <see cref="DeclarationContentTests"/> covers, and the reason
/// <c>DeclarationDocumentContent.Serve</c> became <c>Api.Modules.Media.DocumentContent.Serve</c>.
///
/// The interesting assertions are refusals again, and one of them is new in kind: a broker request
/// and a declaration are different tables whose ids meet in the same polymorphic <c>owner_id</c>
/// column (§4, no FK), so the owner **kind** has to be matched as well as the id.
/// </summary>
[Collection("api")]
public sealed class BrokerContentTests(ApiFixture fixture)
{
    private static Uri ContentPath(Guid requestId, Guid documentId) =>
        new($"{BrokerFlows.DocumentsPath(requestId)}/{documentId}/content", UriKind.Relative);

    [Fact]
    public async Task ABrokerReadsTheCustomersCarShotBackWithTheStoredContentType()
    {
        using var broker = await fixture.CreateBroker();
        var link = await fixture.IssueLink(broker.Client);
        using var customer = fixture.CreatePublicClient();
        await customer.GetAsync($"/public/{link.Token}");
        (await PublicLinkFlows.UploadCarShot(customer, link.Token, MediaBuckets.PublicCarFront))
            .EnsureSuccessStatusCode();

        var document = Assert.Single(await fixture.BrokerDocumentRows(link.RequestId));

        var response = await broker.Client.GetAsync(ContentPath(link.RequestId, document.Id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The stored type, recognised at upload rather than claimed then or re-sniffed now.
        Assert.Equal(ImageHeader.Jpeg, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            document.SizeBytes, (await response.Content.ReadAsByteArrayAsync()).Length);

        // `inline`, so B4's five slots render rather than downloading five files; `nosniff` because
        // these are bytes a member of the public supplied, served from our own origin.
        Assert.Equal("inline", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());

        // §9 permits blob reads "via short-lived SAS only"; proxying is stricter, so nothing capable
        // of reaching storage may appear in the response.
        Assert.DoesNotContain(
            "blob.core.windows.net", response.Headers.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnotherBrokersRequestIsNotReadable()
    {
        // §9's resource rule, on the route that serves bytes rather than metadata. Ownership is
        // resolved through the same `Find` the list uses, *before* the document is looked up at all.
        using var owner = await fixture.CreateBroker();
        using var intruder = await fixture.CreateBroker();

        var link = await fixture.IssueLink(owner.Client);
        using var customer = fixture.CreatePublicClient();
        await customer.GetAsync($"/public/{link.Token}");
        (await PublicLinkFlows.UploadPublicDocument(customer, link.Token)).EnsureSuccessStatusCode();

        var document = Assert.Single(await fixture.BrokerDocumentRows(link.RequestId));

        var response = await intruder.Client.GetAsync(ContentPath(link.RequestId, document.Id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ADocumentIdFromAnotherRequestIsNotReadableUnderMine()
    {
        // DeclarationContentTests' finding, checked in the sibling rather than assumed to have been
        // carried across (CLAUDE.md: "a lesson learned in one adapter is not learned until it is
        // checked in the others"). Drop `OwnerId == ownerId` from `DocumentContent.Serve` and this
        // goes red while every other test in the file stays green.
        using var broker = await fixture.CreateBroker();

        var theirs = await fixture.IssueLink(broker.Client);
        using var customer = fixture.CreatePublicClient();
        await customer.GetAsync($"/public/{theirs.Token}");
        (await PublicLinkFlows.UploadPublicDocument(customer, theirs.Token)).EnsureSuccessStatusCode();
        var document = Assert.Single(await fixture.BrokerDocumentRows(theirs.RequestId));

        var mine = await broker.CreateRequestDraft();

        var response = await broker.Client.GetAsync(ContentPath(mine, document.Id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ADeclarationDocumentIsNotReadableThroughTheBrokerRoute()
    {
        // The refusal that is new in kind. `owner_id` is polymorphic and carries no FK (§4), so the id
        // spaces of `declaration` and `broker_request` are not separated by anything the database
        // enforces — the owner **kind** in the predicate is what keeps them apart. Passing the
        // declaration's own id as the request id makes the ownership check answer 404 first, so this
        // asserts the second half: the broker's own request, someone else's document kind.
        using var garage = await fixture.CreateGarage();
        var declarationId = await garage.CreateDraft();
        var uploaded = await garage.UploadSurvey(declarationId);
        uploaded.EnsureSuccessStatusCode();
        var uploadedId = (await uploaded.Content.ReadFromJsonAsync<DocumentBodyDto>())!.Id;

        using var broker = await fixture.CreateBroker();
        var requestId = await broker.CreateRequestDraft();

        var response = await broker.Client.GetAsync(ContentPath(requestId, uploadedId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ASweptBlobIs404_WhichIsExpectedRatherThanAFault()
    {
        // §7.3 deletes a broker request's bytes `Retention:BrokerBlobDays` after `emailed_at`, and
        // that is terminal — no retry brings them back. The row survives, so B4 can still say what the
        // document was; `DocumentDto.BlobRetained` is how it knows never to link one.
        using var broker = await fixture.CreateBroker();
        var link = await fixture.IssueLink(broker.Client);
        using var customer = fixture.CreatePublicClient();
        await customer.GetAsync($"/public/{link.Token}");
        (await PublicLinkFlows.UploadPublicDocument(customer, link.Token)).EnsureSuccessStatusCode();

        var document = Assert.Single(await fixture.BrokerDocumentRows(link.RequestId));
        await fixture.MarkBlobDeleted(link.RequestId);

        var response = await broker.Client.GetAsync(ContentPath(link.RequestId, document.Id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
