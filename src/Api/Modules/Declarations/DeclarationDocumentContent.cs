using Api.Infrastructure;
using Api.Integrations.Blob;
using Api.Modules.Media;

namespace Api.Modules.Declarations;

/// <summary>
/// §5.2's document previews, addressed by declaration (slice 4.2).
///
/// **The body of this moved to <see cref="DocumentContent"/> in slice 6.1**, when B4's photo review
/// became its third caller and a broker request its second owner kind. What is left is the one thing
/// that was ever declaration-specific: the owner kind. Kept as a named entry point rather than
/// inlined at both call sites because the two endpoint groups differ in exactly one way — the garage
/// is scoped to its own declarations and the officer is not (§5.2 defines no per-officer assignment)
/// — and this is the seam that says everything after that resolution is shared.
/// </summary>
internal static class DeclarationDocumentContent
{
    /// <summary>
    /// Resolves the document within the declaration and returns its bytes, or 404.
    ///
    /// <paramref name="declarationId"/> is assumed **already authorized** by the caller — the garage
    /// group resolves it scoped to the signed-in garage and answers its own 404 first.
    /// </summary>
    public static Task<IResult> Serve(
        AppDbContext db,
        IBlobStore blobs,
        Guid declarationId,
        Guid documentId,
        CancellationToken ct) =>
        DocumentContent.Serve(
            db, blobs, DocumentOwnerKinds.Declaration, declarationId, documentId, ct);
}
