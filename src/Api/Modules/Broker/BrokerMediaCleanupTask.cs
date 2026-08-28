using Api.Infrastructure;
using Api.Infrastructure.Cleanup;
using Api.Modules.Media;
using Microsoft.Extensions.Options;

namespace Api.Modules.Broker;

/// <summary>
/// design.md §7.3's broker branch — the third sweep, and the one <c>RetentionOptions.BrokerBlobDays</c>
/// has been waiting for since slice 2.3 (slice 5.2).
///
/// Broker media is **structurally invisible** to the confirmed-push sweep: it has `push_status = n/a`
/// and no outbox row at all, and that sweep's eligibility set is a join on outbox `sent`. It is
/// equally invisible to the orphan sweep, which spares any blob a live row still claims. So without
/// this task a broker's documents are retained for ever — and §9's "the app is deliberately not a
/// long-term PII store" stops being true the first time somebody attaches an identity document.
///
/// **The rule is keyed on <c>emailed_at</c>, not on a state**, and that is a deliberate reading of
/// §7.3's "until `BrokerBlobDays` after `sent`/terminal state":
///
/// <list type="bullet">
/// <item>Option 1's terminal state is `submitted`, not `sent` — `sent` is Option 2's — so a state test
/// on `sent` alone would sweep nothing Option 1 produces, which is everything that exists today.</item>
/// <item>`expired` is computed in B1's projection from the token's own expiry and is **never written**,
/// so there is no column to test it with.</item>
/// <item><c>emailed_at</c> says the thing that actually licenses the delete: the documents have left
/// the building as attachments, so the transit copy is surplus. It covers Option 1 `submitted` and
/// Option 2 `sent` in one predicate.</item>
/// <item>And it gets the failure case right for free. §5.3 commits the transition before the send, so
/// a failed send leaves <c>emailed_at</c> null — and those blobs are retained, which is exactly what
/// B1's Resend needs.</item>
/// </list>
///
/// **What this deliberately does not cover:** an abandoned or expired Option 2 request. **Slice 5.3
/// turned that from hypothetical into real and the sentence here has to change with it.** 5.2 could
/// say the case "carries no documents at all today", because `public_document` did not exist; it does
/// now, and a customer who photographs their identity card and then never presses Send leaves those
/// bytes in the transit container with nothing to move them on. Sweep 1 needs an outbox row a
/// `PushTiming.Never` bucket never has; sweep 2 spares any blob a live `document` row claims; and this
/// sweep needs `emailed_at`, which only B4's Send writes.
///
/// The delete itself is not the hard part — this query is keyed on the owner kind, so those rows are
/// already in its set the moment `emailed_at` lands. What is missing is a rule for the requests where
/// it never will, and deciding how long a member of the public's identity documents are kept after
/// they never finished is a **client answer (#4/#22)**, not a sweep to add quietly. Same shape, and
/// now the same weight, as §7.3's carried rejected-declaration gap: both are tickets for 7.2, and both
/// are written down here rather than left as an absence somebody has to notice.
/// </summary>
public sealed class BrokerMediaCleanupTask(
    AppDbContext db,
    DocumentBlobSweeper sweeper,
    IOptionsMonitor<RetentionOptions> options,
    TimeProvider time) : ICleanupTask
{
    public string Name => "broker_blobs";

    public async Task<int> Run(CancellationToken ct)
    {
        var swept = await SweepEmailedRequests(ct);
        swept += await SweepAbandonedRequests(ct);
        return swept;
    }

    private Task<int> SweepEmailedRequests(CancellationToken ct)
    {
        var settings = options.CurrentValue;
        var cutoff = time.GetUtcNow().UtcDateTime.AddDays(-settings.BrokerBlobDays);

        // One statement, and the join is the safety property exactly as the confirmed-push sweep's is:
        // a document whose request has not been emailed cannot enter this set at all. `owner_id` is
        // polymorphic and carries no FK (§4), so the join is written out rather than navigated.
        var due =
            from document in db.Documents
            where document.OwnerKind == DocumentOwnerKinds.BrokerRequest && document.BlobDeletedAt == null
            join request in db.BrokerRequests on document.OwnerId equals request.Id
            where request.EmailedAt != null && request.EmailedAt <= cutoff
            select document;

        return sweeper.Sweep(
            due,
            document => new { document.BlobKey, Reason = "broker_retention", settings.BrokerBlobDays },
            ct);
    }

    /// <summary>
    /// The second branch (slice 7.2): an Option 2 request nobody ever sent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// §7.3 carried this as a ticket, and slice 5.3 turned it from hypothetical into real by giving
    /// the public page an upload route; 6.1 widened it from one file to as many as six — an identity
    /// card and five photographs of an identifiable car, plate included. A customer who photographs
    /// all of that and then never presses Send leaves those bytes in the transit container with
    /// nothing to move them on: sweep 1 needs an outbox row a <c>PushTiming.Never</c> bucket never
    /// has, sweep 2 spares any blob a live row claims, and the branch above needs an
    /// <c>emailed_at</c> that will never be written.
    /// </para>
    /// <para>
    /// **Keyed on the newest token's <c>expires_at</c>, not on a state.** Option 2's `expired` is
    /// computed in B1's projection and never persisted, so there is no column a sweep could test. The
    /// token's expiry is the only durable fact that says the customer cannot come back — the link is
    /// dead, and a broker who wants another one issues a new request with a new token.
    /// </para>
    /// <para>
    /// **Pre-submission states only, and that restriction is the whole safety property.** The first
    /// version of this branch keyed on <c>emailed_at == null</c> alone, and the db-review showed what
    /// that destroys. A <c>ready_to_send</c> request is a *completed* submission — six fields, an
    /// identity document, five photographs of a car — sitting in B1 waiting for a human to press
    /// **Send email**. Nothing expires it, so its token lapses on schedule and the sweep would delete
    /// the attachments while the button was still on screen; <c>BrokerRequestEmail.Attachments</c>
    /// then refuses <c>409 attachments_unavailable</c> for ever, the customer's link is locked, and
    /// there is no second copy of anything. Same for <c>sent</c> with a null <c>emailed_at</c>, which
    /// is §5.3's failed send with **Resend** still offered.
    /// </para>
    /// <para>
    /// So the predicate names the two states where nobody is waiting to act: <c>link_issued</c> and
    /// <c>customer_in_progress</c>. Both mean the customer never finished, and neither can be reached
    /// again once the token has lapsed. A resendable failed send is therefore protected outright
    /// rather than for a window — stronger than the slice card asked for, and the direction the
    /// retention rule should err in.
    /// </para>
    /// <para>
    /// **What that leaves open, deliberately:** a submission the broker never reviews keeps its bytes
    /// indefinitely. Expiring one needs an answer to "how long does a broker have?", which is the same
    /// #4/#22 question as the window itself and not one to invent inside a sweep. Retaining too long
    /// is the safe side; deleting a completed submission is not.
    /// </para>
    /// <para>
    /// A request with no token at all (Option 1) can never enter this set: <c>Max</c> over an empty
    /// sequence is null in SQL, and a null is not <c>&lt;=</c> anything. That is correct — Option 1's
    /// documents are covered by the emailed branch, and one that was never submitted is a broker's own
    /// draft on an authenticated screen, not a member of the public's identity card.
    /// </para>
    /// </remarks>
    private Task<int> SweepAbandonedRequests(CancellationToken ct)
    {
        var settings = options.CurrentValue;
        var cutoff = time.GetUtcNow().UtcDateTime.AddDays(-settings.AbandonedRequestBlobDays);

        var due =
            from document in db.Documents
            where document.OwnerKind == DocumentOwnerKinds.BrokerRequest && document.BlobDeletedAt == null
            join request in db.BrokerRequests on document.OwnerId equals request.Id
            where request.EmailedAt == null
                && (request.State == BrokerRequestState.LinkIssued
                    || request.State == BrokerRequestState.CustomerInProgress)
                && db.PublicLinkTokens
                    .Where(token => token.BrokerRequestId == request.Id)
                    .Max(token => (DateTime?)token.ExpiresAt) <= cutoff
            select document;

        return sweeper.Sweep(
            due,
            document => new
            {
                document.BlobKey,
                Reason = "abandoned_request",
                settings.AbandonedRequestBlobDays,
            },
            ct);
    }
}
