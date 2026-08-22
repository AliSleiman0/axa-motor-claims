using System.Net;
using System.Net.Http.Json;
using Api.Modules.Media;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Integration;

/// <summary>
/// The document-content endpoints (slice 4.2) — the first thing in the application to serve blob
/// bytes over HTTP, and therefore the first place a mistake in `IBlobStore`'s authorization surface
/// would show.
///
/// The interesting assertions are all refusals. §9's resource rule says a garage sees only its own,
/// §5.2 says any officer sees any declaration, and §7.3 says the bytes stop existing once NEXT3 has
/// them — three different reasons for the same bare 404, which is what makes it worth testing that
/// each one actually fires.
/// </summary>
[Collection("api")]
public sealed class DeclarationContentTests(ApiFixture fixture)
{
    [Fact]
    public async Task AGarageReadsItsOwnDocumentBackWithTheStoredContentType()
    {
        using var garage = await fixture.CreateGarage();
        var id = await garage.CreateDraft();

        var uploaded = await Upload(garage, id);

        var response = await garage.Client.GetAsync(ContentPath(id, uploaded.Id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The stored type, which is the one the bytes were *recognised* as at upload — not the one
        // the client claimed then and not one re-sniffed now (3.1's "accept loosely, store
        // canonically", from the reading side).
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        var document = await fixture.DocumentRow(uploaded.Id);
        Assert.Equal(document.SizeBytes, bytes.Length);

        // §9 permits blob reads "via short-lived SAS only"; proxying is stricter, so nothing that
        // could reach storage may appear in the response.
        Assert.DoesNotContain("blob.core.windows.net", response.Headers.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheResponseIsInlineAndNotSniffable()
    {
        using var garage = await fixture.CreateGarage();
        var id = await garage.CreateDraft();
        var uploaded = await Upload(garage, id, "invoice.pdf");

        var response = await garage.Client.GetAsync(ContentPath(id, uploaded.Id));

        // `inline`, or O2's preview becomes a download prompt per photograph — which is what
        // Results.File(..., fileDownloadName) would have produced.
        var disposition = response.Content.Headers.ContentDisposition;
        Assert.NotNull(disposition);
        Assert.Equal("inline", disposition.DispositionType);
        Assert.Contains("invoice.pdf", disposition.ToString(), StringComparison.Ordinal);

        // These are bytes a garage uploaded, served from the application's own origin. §7.2 item 5
        // already refuses a file whose magic bytes disagree with its declared type; this is the
        // second lock, and #21 puts an InfoSec review on the critical path.
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task AGarageCannotReadAnotherGaragesDocument()
    {
        using var owner = await fixture.CreateGarage();
        using var intruder = await fixture.CreateGarage();

        var id = await owner.CreateDraft();
        var uploaded = await Upload(owner, id);

        var response = await intruder.Client.GetAsync(ContentPath(id, uploaded.Id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ADocumentIdFromAnotherDeclarationIsNotReadableUnderMine()
    {
        // The finding this test exists for: looking the document up by its id *alone* would let a
        // garage read any document in the system by quoting its id under a declaration of its own.
        // The authorization would look present and be checking the wrong thing — worse than absent,
        // because nobody re-reads a check that is already there. Drop `OwnerId == declarationId`
        // from DeclarationDocumentContent.Serve and this goes red while every other test stays green.
        using var owner = await fixture.CreateGarage();
        using var intruder = await fixture.CreateGarage();

        var theirs = await owner.CreateDraft();
        var uploaded = await Upload(owner, theirs);

        var mine = await intruder.CreateDraft();

        var response = await intruder.Client.GetAsync(ContentPath(mine, uploaded.Id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AnOfficerReadsAnyDeclarationsDocument()
    {
        // The deliberate asymmetry (§5.2: "no per-officer assignment — any officer may pick it up").
        using var garage = await fixture.CreateGarage();
        using var officer = await fixture.CreateOfficer();

        var id = await garage.CreateDraft();
        var uploaded = await Upload(garage, id);

        var response = await officer.Client.GetAsync(
            new Uri($"/api/officer/declarations/{id}/documents/{uploaded.Id}/content", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task AGarageCannotReachTheOfficerEndpoint()
    {
        using var garage = await fixture.CreateGarage();
        var id = await garage.CreateDraft();
        var uploaded = await Upload(garage, id);

        // The policy on the group, not a check in the handler — but worth pinning, because the
        // officer route is the unscoped one and a role mix-up there reads every garage's documents.
        var response = await garage.Client.GetAsync(
            new Uri($"/api/officer/declarations/{id}/documents/{uploaded.Id}/content", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AsweptDocumentIsGoneRatherThanBroken()
    {
        // §7.3 deletes the bytes once NEXT3 confirms the push, and that is terminal. The row survives
        // — it is §9's record of who uploaded what — so the endpoint has to answer for a document
        // that exists and whose bytes do not.
        using var garage = await fixture.CreateGarage();
        var id = await garage.CreateDraft();
        var uploaded = await Upload(garage, id);

        await using (var db = fixture.CreateDbContext())
        {
            var document = await db.Documents.SingleAsync(d => d.Id == uploaded.Id);
            document.BlobDeletedAt = fixture.Time.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync();
        }

        var response = await garage.Client.GetAsync(ContentPath(id, uploaded.Id));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // And the list says so, which is how the screens know never to link it in the first place.
        var documents = await garage.Client.GetFromJsonAsync<List<DocumentBodyDto>>(
            $"/api/garage/declarations/{id}/documents");
        Assert.NotNull(documents);
        Assert.False(Assert.Single(documents).BlobRetained);
    }

    [Fact]
    public async Task BytesThatVanishedWithoutBeingSweptAreAlsoAFourOhFour()
    {
        // The row still claims its bytes are present and storage disagrees. `IBlobStore.Open` returns
        // null rather than throwing by contract, and there is nothing to serve either way — but it is
        // a different fact from "already swept", so it gets its own branch and its own test.
        using var garage = await fixture.CreateGarage();
        var id = await garage.CreateDraft();
        var uploaded = await Upload(garage, id);

        var document = await fixture.DocumentRow(uploaded.Id);
        await fixture.Blobs.Delete(document.BlobKey, CancellationToken.None);

        var response = await garage.Client.GetAsync(ContentPath(id, uploaded.Id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AnUnknownDocumentIsAFourOhFourRatherThanAnError()
    {
        using var garage = await fixture.CreateGarage();
        var id = await garage.CreateDraft();

        var response = await garage.Client.GetAsync(ContentPath(id, Guid.CreateVersion7()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static Uri ContentPath(Guid declarationId, Guid documentId) =>
        new($"/api/garage/declarations/{declarationId}/documents/{documentId}/content", UriKind.Relative);

    private static async Task<DocumentBodyDto> Upload(
        Actor garage, Guid declarationId, string fileName = "PLACEHOLDER-survey.pdf")
    {
        var response = await garage.UploadSurvey(declarationId, fileName);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DocumentBodyDto>())!;
    }
}
