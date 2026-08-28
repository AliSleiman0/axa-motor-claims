using System.Net.Http.Json;
using Api.Modules.Audit;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Integration;

/// <summary>
/// design.md §7.3's broker branch — the third sweep (slice 5.2).
///
/// Broker media is **structurally invisible** to the other two: the confirmed-push sweep joins on an
/// outbox row that a `PushTiming.Never` document does not have, and the orphan sweep spares any blob a
/// live row still claims. So until this task existed a broker's documents were kept for ever, and §9's
/// "the app is deliberately not a long-term PII store" was not true of the one surface that collects
/// identity documents.
///
/// The rule is keyed on <c>emailed_at</c> rather than on a state, and the second and third tests are
/// why: Option 1's terminal state is `submitted`, not `sent`, so a state test would sweep nothing this
/// slice produces — and a request whose send *failed* must keep its bytes, because Resend needs them.
/// </summary>
[Collection("api")]
public sealed class BrokerRetentionTests(ApiFixture fixture)
{
    [Fact]
    public async Task AnEmailedRequestsBlobsAreSweptOnceTheWindowHasPassed()
    {
        using var broker = await fixture.CreateBroker();
        var id = await broker.CreateRequestDraft();
        (await broker.UploadBrokerDocument(id)).EnsureSuccessStatusCode();
        (await broker.SubmitRequest(id)).EnsureSuccessStatusCode();

        var document = Assert.Single(await fixture.BrokerDocumentRows(id));
        Assert.True(await fixture.BlobExists(document.BlobKey));

        // Inside the window first: the sweep must not be early, and a test that only asserted the
        // delete would pass for a task that deleted everything on sight.
        await fixture.Sweep();
        Assert.True(await fixture.BlobExists(document.BlobKey));

        await fixture.BackdateEmailedAt(id, days: 31);
        await fixture.Sweep();

        Assert.False(await fixture.BlobExists(document.BlobKey));

        // The metadata row outlives its bytes by design — §9's "who uploaded which document, when" is
        // ours to keep, and the audit row says which rule removed them.
        var swept = Assert.Single(await fixture.BrokerDocumentRows(id));
        Assert.NotNull(swept.BlobDeletedAt);

        await using var db = fixture.CreateDbContext();
        var detail = await db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.EntityKind == AuditEntityKinds.Document
                && a.EntityId == document.Id
                && a.Action == AuditActions.DocumentBlobDeleted)
            .Select(a => a.Detail)
            .SingleAsync();

        Assert.Contains("broker_retention", detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADraftsBlobsAreNeverSwept()
    {
        using var broker = await fixture.CreateBroker();
        var id = await broker.CreateRequestDraft();
        (await broker.UploadBrokerDocument(id)).EnsureSuccessStatusCode();

        var document = Assert.Single(await fixture.BrokerDocumentRows(id));

        // Well past any window. A draft has never been emailed, so there is no clock to run out —
        // the join, not an arithmetic comparison, is what excludes it.
        await fixture.WithRetention(r => r.BrokerBlobDays = 0, () => fixture.Sweep());

        Assert.True(await fixture.BlobExists(document.BlobKey));
        Assert.Null(Assert.Single(await fixture.BrokerDocumentRows(id)).BlobDeletedAt);
    }

    [Fact]
    public async Task AFailedSendsBlobsAreKeptSoResendStillHasThemToSend()
    {
        using var broker = await fixture.CreateBroker();
        var id = await broker.CreateRequestDraft();
        (await broker.UploadBrokerDocument(id)).EnsureSuccessStatusCode();

        var original = fixture.Fake.CurrentValue;
        try
        {
            fixture.Fake.CurrentValue = new Api.Integrations.FakeOptions { FailureRate = 1.0 };
            (await broker.SubmitRequest(id)).EnsureSuccessStatusCode();
        }
        finally
        {
            fixture.Fake.CurrentValue = original;
        }

        var document = Assert.Single(await fixture.BrokerDocumentRows(id));

        await fixture.WithRetention(r => r.BrokerBlobDays = 0, () => fixture.Sweep());

        // `emailed_at` is null because the send failed, so nothing is due — which is the property that
        // makes committing the state before the send safe. A sweep keyed on the *state* would have
        // deleted the attachments of a request that never went out.
        Assert.True(await fixture.BlobExists(document.BlobKey));

        (await broker.ResendRequest(id)).EnsureSuccessStatusCode();
        Assert.Single(fixture.Email.LastTo(fixture.RecipientFor())!.Attachments);
    }

    [Fact]
    public async Task ARequestWhoseBytesAreGone_RefusesAResendRatherThanMailingLessThanItSays()
    {
        using var broker = await fixture.CreateBroker();
        var id = await broker.CreateRequestDraft();
        (await broker.UploadBrokerDocument(id)).EnsureSuccessStatusCode();

        var original = fixture.Fake.CurrentValue;
        try
        {
            fixture.Fake.CurrentValue = new Api.Integrations.FakeOptions { FailureRate = 1.0 };
            (await broker.SubmitRequest(id)).EnsureSuccessStatusCode();
        }
        finally
        {
            fixture.Fake.CurrentValue = original;
        }

        // Not reachable through retention — that is the point of the previous test — so it is forced
        // here. An email that silently drops a document is the failure mode this project exists to
        // remove, so the refusal is loud.
        await fixture.MarkBlobDeleted(id);

        var response = await broker.ResendRequest(id);

        Assert.Equal(System.Net.HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "attachments_unavailable",
            (await response.Content.ReadFromJsonAsync<ErrorBody>())?.Error);
    }

    // ---- slice 7.2: the abandoned Option 2 request (§7.3's second carried ticket) ----

    /// <summary>
    /// A customer who photographs their identity card and five sides of their car and then never
    /// presses Send. <c>emailed_at</c> stays null for ever, so the branch above never matches, and
    /// nothing else in §7.3 can see the rows: sweep 1 needs an outbox row a <c>PushTiming.Never</c>
    /// bucket does not have, and sweep 2 spares any blob a live document row claims.
    /// </summary>
    [Fact]
    public async Task AnAbandonedOptionTwoRequestsBlobsAreSweptOnceItsLinkHasLongExpired()
    {
        var link = await fixture.IssueLink();
        using var customer = fixture.CreatePublicClient();

        (await PublicLinkFlows.UploadPublicDocument(customer, link.Token)).EnsureSuccessStatusCode();

        var document = Assert.Single(await fixture.BrokerDocumentRows(link.RequestId));
        Assert.True(await fixture.BlobExists(document.BlobKey));

        // A live link is not an abandonment — the customer may still be filling the form in.
        await fixture.Sweep();
        Assert.True(await fixture.BlobExists(document.BlobKey));

        // Expired, but only just: still inside the retention window, which is what stops the sweep
        // acting the moment a link lapses.
        await fixture.ExpireLink(link.RequestId);
        await fixture.Sweep();
        Assert.True(await fixture.BlobExists(document.BlobKey));

        await fixture.WithRetention(r => r.AbandonedRequestBlobDays = 0, () => fixture.Sweep());

        Assert.False(await fixture.BlobExists(document.BlobKey));

        var swept = Assert.Single(await fixture.BrokerDocumentRows(link.RequestId));
        Assert.NotNull(swept.BlobDeletedAt);

        var audit = await fixture.AuditRow(AuditActions.DocumentBlobDeleted, document.Id);
        Assert.Contains("abandoned_request", audit.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// **The one the db-review caught, and the reason this sweep names states rather than keying on
    /// <c>emailed_at</c> alone.** A <c>ready_to_send</c> request is a *completed* submission sitting
    /// in B1 waiting for a human to press **Send email**. Nothing expires it, so its token lapses on
    /// schedule — and a sweep that looked only at <c>emailed_at</c> would delete the attachments
    /// while the button was still on screen. <c>Send</c> would then refuse
    /// <c>409 attachments_unavailable</c> for ever, the customer's link is locked, and there is no
    /// second copy of anything.
    /// </summary>
    [Fact]
    public async Task ASubmissionWaitingForTheBrokerIsNeverSwept()
    {
        using var broker = await fixture.CreateBroker();
        var (requestId, _) = await broker.ReadyToSendRequest(fixture);

        var documents = await fixture.BrokerDocumentRows(requestId);
        Assert.NotEmpty(documents);

        await fixture.ExpireLink(requestId);
        await fixture.WithRetention(
            r =>
            {
                r.AbandonedRequestBlobDays = 0;
                r.BrokerBlobDays = 0;
            },
            () => fixture.Sweep());

        foreach (var document in documents)
        {
            Assert.True(await fixture.BlobExists(document.BlobKey));
        }

        // And the proof that it still matters: the broker can send, with everything attached.
        (await broker.SendRequest(requestId)).EnsureSuccessStatusCode();
        Assert.NotEmpty(fixture.Email.LastTo(fixture.RecipientFor())!.Attachments);
    }

    private sealed record ErrorBody(string Error);
}
