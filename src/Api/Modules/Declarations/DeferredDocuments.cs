using Api.Integrations.Next3;
using Api.Modules.Media;
using Api.Outbox;

namespace Api.Modules.Declarations;

/// <summary>
/// The one place a <c>deferred</c> document becomes a queued NEXT3 push (extracted in slice 7.2).
/// </summary>
/// <remarks>
/// <para>
/// It was a private method on <see cref="DeclarationService"/> until this slice, called from exactly
/// one place: the approve transition. Slice 7.2 gives it a second caller —
/// <see cref="StrandedDeferredRequeueTask"/>, which picks up documents that were admitted a
/// microsecond before an approval committed and would otherwise stay <c>deferred</c> for ever. Two
/// callers of a rule with this many §7.1 lookups in it is exactly the shape that drifts, and the
/// drift would be invisible: a document filed under the wrong folder or the wrong doc type reaches
/// NEXT3 and is simply in the wrong place.
/// </para>
/// <para>
/// **Architecture rule 4 is intact.** The only outbox surface either caller touches is
/// <see cref="OutboxWriter"/>, which hands back a Guid rather than the row it added; nothing here
/// names <c>Next3OutboxMessage</c>.
/// </para>
/// <para>
/// Named for the rows rather than for the act (<c>DeferredDocumentQueue</c> was the working name):
/// CA1711 reserves the <c>Queue</c> suffix for collection types, and the analyzers are errors here.
/// </para>
/// <para>
/// **Adds, never saves** — like <c>AuditWriter</c>, and for the same reason. The caller's
/// <c>SaveChanges</c> is what makes the outbox row and the <c>push_status</c> flip one transaction,
/// which is design.md §4's cross-cutting rule: a document row without its outbox row is a photo that
/// is never pushed, and an outbox row without its document is a push for a file that does not exist.
/// </para>
/// </remarks>
public static class DeferredDocuments
{
    /// <summary>
    /// Flips every document waiting on this declaration to <c>queued</c> and enqueues its push, now
    /// that a visa exists to address them to.
    ///
    /// <c>clientRef</c> is the document id, exactly as the immediate path uses it, so a push queued
    /// here is idempotent on the same key an expert's photo would be. Which of the two enqueue methods
    /// a document gets is read off its **bucket rule** (<see cref="Next3PushKind"/>) rather than
    /// decided here by comparing bucket names: the same wire call either way, but A2 has to tell "the
    /// approval never reached NEXT3" from "a photo never reached NEXT3", and that is a §7.1 fact.
    ///
    /// The doc type comes off the **row**, not from config, because that is the value the upload
    /// validated and stored (3.1's "accept loosely, store canonically"). Reading it again here would
    /// let a config change between upload and approval file the document under a different code.
    /// </summary>
    public static void Queue(OutboxWriter outbox, IEnumerable<Document> deferred, string visaNo)
    {
        foreach (var document in deferred)
        {
            var rule = MediaBuckets.Find(document.Bucket)
                ?? throw new InvalidOperationException(
                    $"Document {document.Id} is in bucket '{document.Bucket}', which §7.1 does not define.");

            var docType = document.DocType
                ?? throw new InvalidOperationException(
                    $"Document {document.Id} is deferred for NEXT3 but carries no document type.");

            // Non-null for every pushing bucket by construction: only PushTiming.Never omits a folder,
            // and a deferred document is by definition not one (slice 5.2 made the field nullable).
            var folder = rule.Next3Folder
                ?? throw new InvalidOperationException(
                    $"Bucket '{rule.Bucket}' is deferred for NEXT3 but §7.1 gives it no folder.");

            var push = new DocumentPush(
                folder,
                docType,
                // Rows written before slice 4.1 have no stored name; nothing else can reconstruct one.
                document.FileName ?? $"{document.Id:N}",
                document.ContentType,
                document.BlobKey);

            var clientRef = document.Id.ToString();

            document.OutboxMessageId = rule.PushKind switch
            {
                Next3PushKind.Approval => outbox.EnqueueApproval(visaNo, push, clientRef),
                Next3PushKind.Document => outbox.EnqueueDocument(visaNo, push, clientRef),
                _ => throw new ArgumentOutOfRangeException(nameof(deferred), rule.PushKind, null),
            };

            document.PushStatus = DocumentPushStatuses.Queued;
        }
    }
}
