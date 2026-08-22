using Api.Infrastructure;
using Api.Integrations.Blob;
using Api.Modules.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace Api.Modules.Declarations;

/// <summary>
/// Streams one declaration document's bytes back to a browser (slice 4.2).
///
/// It exists because §5.2's officer review is a screen for *looking at* what a garage sent, and until
/// now nothing in the application ever served a blob over HTTP — <see cref="IBlobStore.Open"/> has
/// existed since slice 3.3 with the outbox worker as its only caller.
///
/// **One helper, called by both endpoint groups**, because the two differ in exactly one way — the
/// garage is scoped to its own declarations and the officer is not (§5.2 defines no per-officer
/// assignment) — and everything after that resolution is a rule that must not drift between them.
///
/// **The API proxies the bytes; there are no SAS URLs.** §9 permits blob reads "via short-lived SAS
/// only", and proxying is stricter than that rather than looser: nothing capable of reading storage
/// ever leaves the server, so a URL copied out of devtools is useless without the caller's token.
/// </summary>
internal static class DeclarationDocumentContent
{
    /// <summary>
    /// Resolves the document within the declaration and returns its bytes, or 404.
    ///
    /// <paramref name="declarationId"/> is assumed **already authorized** by the caller — the garage
    /// group resolves it scoped to the signed-in garage and answers its own 404 first.
    /// </summary>
    public static async Task<IResult> Serve(
        AppDbContext db,
        IBlobStore blobs,
        Guid declarationId,
        Guid documentId,
        CancellationToken ct)
    {
        // Matched on the owner as well as the id. Looking the document up by `documentId` alone
        // would let a garage read any document in the system by quoting its id under a declaration
        // of its own — the authorization would appear to be there and would be checking the wrong
        // thing, which is worse than an absent check because it reads as present.
        var document = await db.Documents.AsNoTracking()
            .SingleOrDefaultAsync(
                d => d.Id == documentId
                    && d.OwnerKind == DocumentOwnerKinds.Declaration
                    && d.OwnerId == declarationId,
                ct);

        if (document is null)
        {
            return Results.NotFound();
        }

        if (document.BlobDeletedAt is not null)
        {
            // §7.3 swept the bytes once NEXT3 confirmed the push, and that is terminal — no retry
            // brings them back. The screens never link one of these: the list DTO carries
            // `blobRetained`, so a swept document renders as "sent to AXA, local copy removed"
            // rather than as a broken image (2.5's lesson, applied one layer up from the alt text).
            return Results.NotFound();
        }

        var stream = await blobs.Open(document.BlobKey, ct);
        if (stream is null)
        {
            // The row says the bytes are here and storage disagrees. Absent is null rather than an
            // exception by `IBlobStore`'s contract, and there is nothing to serve either way — but
            // it is a different fact from "already swept", so it gets its own branch rather than
            // being folded into the check above.
            return Results.NotFound();
        }

        return new DocumentContentResult(stream, document);
    }
}

/// <summary>
/// The response itself, as a type rather than a lambda so the headers below are written once.
///
/// <c>Results.File(..., fileDownloadName)</c> and <c>Results.Stream(..., fileDownloadName)</c> both
/// emit <c>Content-Disposition: attachment</c>, which turns O2's inline preview into a download
/// prompt per photograph — so the header is built here instead.
/// </summary>
internal sealed class DocumentContentResult(Stream stream, Document document) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        await using var body = stream;

        var response = httpContext.Response;

        // The **stored** content type, which is the one the bytes were recognised as at upload
        // (3.1's "accept loosely, store canonically") — not the one the caller claimed then, and not
        // one re-sniffed now.
        response.ContentType = document.ContentType;

        // `nosniff` because these are bytes a garage — and in slice 5.3 a member of the public —
        // uploaded, served from the application's own origin. §7.2 item 5 already refuses a file
        // whose magic bytes disagree with its declared type, so an HTML payload cannot be stored as
        // `image/jpeg`; this is the belt to that braces, and #21 puts an AXA Group InfoSec review on
        // the critical path.
        response.Headers.XContentTypeOptions = "nosniff";

        // `inline`, so an image renders and a PDF opens rather than downloading. FileNameStar rather
        // than FileName: the header value is built by the framework's own encoder, which handles a
        // non-ASCII name correctly — and the stored name has already been stripped of control
        // characters and separators at upload (slice 4.1's `SafeFileName`).
        var disposition = new ContentDispositionHeaderValue("inline");
        if (!string.IsNullOrWhiteSpace(document.FileName))
        {
            disposition.SetHttpFileName(document.FileName);
        }

        response.Headers.ContentDisposition = disposition.ToString();

        // Deliberately no `Accept-Ranges` and no range handling: the largest thing here is capped at
        // `Media:MaxFileMb`, nothing seeks into a photograph, and the audio a voice note produces is
        // played from a blob the browser has already fetched whole.
        await body.CopyToAsync(response.Body, httpContext.RequestAborted);
    }
}
