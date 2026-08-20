using Api.Infrastructure;
using Api.Integrations.Next3;

namespace Api.Outbox;

/// <summary>
/// The one-transaction write helper every producer uses (design.md §4, §6.3). **Adds, never saves** —
/// the outbox row joins the caller's SaveChanges so it commits in the same transaction as the domain
/// row it belongs to, exactly like <see cref="Api.Modules.Audit.AuditWriter"/>.
///
/// This is the rule §4 states and CLAUDE.md repeats: if the document row and its outbox row cannot
/// commit together you get either documents that are never pushed or pushes for documents that do not
/// exist — and both surface weeks later as "AXA is missing photos", which is the exact problem this
/// project exists to solve. Calling SaveChanges in here would quietly break that guarantee, which is
/// why <c>WhenTheCommitFails_NeitherRowSurvives</c> exists.
///
/// Every method returns the new row's **id**, not the row (changed in slice 2.3, the first slice with
/// a real producer). Architecture rule 4 forbids any type outside this namespace from referencing
/// <see cref="Next3OutboxMessage"/>, and a called method's return type is part of the calling type's
/// IL — so returning the entity would have made the writer uncallable by the very producers it exists
/// for. The id is all a producer needs: §7.3's cleanup joins `document.outbox_message_id` to it.
/// </summary>
public sealed class OutboxWriter(AppDbContext db, TimeProvider time)
{
    /// <summary>Queues §5.1's Arrived push (date, time and GPS to NEXT3).</summary>
    /// <param name="clientRef">Stable across retries — §5.1 uses the id of the row being pushed.</param>
    public Guid EnqueueArrival(string visaNo, ArrivalInfo info, string clientRef) =>
        Enqueue(
            visaNo,
            Next3OutboxOperations.UpdateArrival,
            new ArrivalOutboxPayload(clientRef, info.Date, info.Time, info.Latitude, info.Longitude));

    /// <summary>Queues a document upload (§5.1, §5.2) into the given NEXT3 folder.</summary>
    public Guid EnqueueDocument(string visaNo, DocumentPush doc, string clientRef) =>
        Enqueue(
            visaNo,
            Next3OutboxOperations.UploadDocument,
            new DocumentOutboxPayload(
                clientRef, doc.Folder, doc.DocType, doc.FileName, doc.ContentType, doc.BlobKey));

    /// <summary>
    /// Queues §5.2's approval PNG into the *Survey* folder. Same wire call as a document upload —
    /// §6.2's interface has no push_approval method — but a distinct operation value, because A2 must
    /// be able to tell "the approval never reached NEXT3" from "a photo never reached NEXT3".
    /// </summary>
    public Guid EnqueueApproval(string visaNo, DocumentPush doc, string clientRef) =>
        Enqueue(
            visaNo,
            Next3OutboxOperations.PushApproval,
            new DocumentOutboxPayload(
                clientRef, doc.Folder, doc.DocType, doc.FileName, doc.ContentType, doc.BlobKey));

    private Guid Enqueue<TPayload>(string visaNo, string operation, TPayload payload)
        where TPayload : notnull
    {
        var now = time.GetUtcNow().UtcDateTime;
        var message = new Next3OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            VisaNo = visaNo,
            Operation = operation,
            Payload = OutboxPayloads.Serialize(payload),
            Status = Next3OutboxStatuses.Pending,
            Attempts = 0,
            // Due immediately: the worker's next pass should pick a new row up without waiting.
            NextRetryAt = now,
            CreatedAt = now,
        };

        db.Set<Next3OutboxMessage>().Add(message);
        return message.Id;
    }
}
