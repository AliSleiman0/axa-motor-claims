using Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Api.Outbox;

/// <summary>
/// The one thing outside this namespace anybody needs to know about outbox rows: which pushes NEXT3
/// has confirmed, and when.
///
/// design.md §7.3 requires the blob-cleanup query to join on `next3_outbox.status = 'sent'`
/// **"structurally, not by convention"** — and HANDOFF §3 predicts that rule being broken during a
/// week-6 cleanup refactor. Handing the media module an <see cref="IQueryable{T}"/> keeps the join in
/// one SQL statement (a `Contains` over this subquery, evaluated by the database) while leaving
/// architecture rule 4 exactly as strict as it was: no type outside <c>Api.Outbox</c> references
/// <see cref="Next3OutboxMessage"/>, so no producer can hand-roll or re-status a row.
///
/// Read-only by construction — there is no write path here, and there must never be one.
/// </summary>
public sealed class OutboxSentQuery(AppDbContext db)
{
    /// <summary>
    /// Ids of pushes NEXT3 confirmed at or before <paramref name="cutoff"/>. Anything `pending`,
    /// `processing` or `failed` is absent, which is the whole safety property: a blob whose row is not
    /// in this set cannot be deleted, and a `failed` row keeps its blob indefinitely because A2's
    /// Retry is what re-sends it.
    /// </summary>
    /// <remarks>
    /// **Do not add `AsNoTracking()` here.** It looks free — this projects to a Guid and materialises
    /// no entity — but the caller composes this into its own query with `Contains`, and EF applies the
    /// tracking behaviour of the *combined* expression tree. The whole outer query then comes back
    /// untracked, so the caller's edits to those entities are silently dropped and `SaveChanges`
    /// reports success having written nothing. Cost the first time: the retention sweep deleted the
    /// bytes and never recorded that it had, so the same rows were swept again on every pass and the
    /// expert's screen went on saying the photo was still held.
    /// </remarks>
    public IQueryable<Guid> MessageIdsSentOnOrBefore(DateTime cutoff) =>
        Sent().Where(m => m.SentAt <= cutoff).Select(m => m.Id);

    /// <summary>
    /// Every push NEXT3 has confirmed, with no cutoff (slice 5.1). The document lists compose this to
    /// answer "has this actually reached AXA?", which `document.push_status` deliberately cannot: §4
    /// keeps live push state on the outbox row alone, because §7.3 deletes blobs on it and two copies
    /// of it can disagree. Read at projection time it is one truth, not a second one.
    /// </summary>
    public IQueryable<Guid> MessageIdsSent() => Sent().Select(m => m.Id);

    private IQueryable<Next3OutboxMessage> Sent() =>
        db.Set<Next3OutboxMessage>()
            .Where(m => m.Status == Next3OutboxStatuses.Sent && m.SentAt != null);
}
