using System.Net;
using System.Net.Http.Json;
using Api.Integrations;
using Api.Modules.Audit;
using Api.Modules.Broker;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Integration;

/// <summary>
/// design.md §5.3's B4: the broker reviews what their customer submitted and releases it to AXA.
///
/// Option 2's terminal act, and the mirror of Option 1's submit — same routing table, same attachment
/// composition, same commit-then-send ordering. What differs is who filled the form in, which is why
/// it walks its own edge (`ready_to_send` -> `sent`) rather than widening <c>Submit</c>.
/// </summary>
[Collection("api")]
public sealed class BrokerSendTests(ApiFixture fixture)
{
    private const int Racers = 4;

    /// <summary>
    /// The customer's documents reach AXA **because they share an owner kind with the broker's own**.
    /// `BrokerRequestEmail.Attachments` selects on the owner alone, so `public_document` rows are
    /// already in its set — which is the whole reason slice 5.3 put the public bucket under
    /// `broker_request` instead of inventing a fourth owner kind.
    /// </summary>
    [Fact]
    public async Task Send_EmailsTheRoutedRecipient_WithTheCustomersDocumentsAttached()
    {
        using var broker = await fixture.CreateBroker();
        var (requestId, _) = await broker.ReadyToSendRequest(fixture);

        var recipient = fixture.RecipientFor();
        var before = fixture.Email.AllTo(recipient).Count;

        var response = await broker.SendRequest(requestId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var row = await fixture.RequestRow(requestId);
        Assert.Equal(BrokerRequestState.Sent, row.State);
        Assert.Equal(recipient, row.EmailRecipient);
        Assert.NotNull(row.EmailedAt);

        var sent = fixture.Email.AllTo(recipient);
        Assert.Equal(before + 1, sent.Count);
        Assert.Contains(
            sent[^1].Attachments,
            a => a.FileName == "PLACEHOLDER-car-papers.pdf");
    }

    /// <summary>
    /// CLAUDE.md's first recurring bug class, eighth outing. Four presses of Send email must produce
    /// one transition and **one email at AXA's desk** — the send is deliberately after the commit, so
    /// a guard that stopped only the second transition would still deliver four times.
    ///
    /// Verified by removing <c>IsConcurrencyToken()</c> from <c>BrokerRequestConfiguration</c>: this
    /// test goes red.
    /// </summary>
    [Fact]
    public async Task ConcurrentSends_ProduceOneTransitionAndOneEmail()
    {
        using var broker = await fixture.CreateBroker();
        var (requestId, _) = await broker.ReadyToSendRequest(fixture);

        var recipient = fixture.RecipientFor();
        var before = fixture.Email.AllTo(recipient).Count;

        var responses = await Task.WhenAll(
            Enumerable.Range(0, Racers).Select(_ => broker.SendRequest(requestId)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(Racers - 1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        Assert.Equal(before + 1, fixture.Email.AllTo(recipient).Count);
        Assert.Equal(BrokerRequestState.Sent, (await fixture.RequestRow(requestId)).State);

        await using var db = fixture.CreateDbContext();
        Assert.Equal(
            1,
            await db.Set<AuditLog>().AsNoTracking().CountAsync(
                a => a.EntityId == requestId && a.Action == AuditActions.BrokerRequestSent));
    }

    /// <summary>
    /// B4 is the *only* way an Option 2 request reaches AXA. Resend is refused at `ready_to_send`
    /// precisely so a broker cannot mail a customer's submission straight past the review step — 5.2's
    /// guard, still doing its job after slice 5.3 widened the other arm of it.
    /// </summary>
    [Fact]
    public async Task ResendCannotBypassTheReviewStep()
    {
        using var broker = await fixture.CreateBroker();
        var (requestId, _) = await broker.ReadyToSendRequest(fixture);

        var refused = await broker.ResendRequest(requestId);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("not_submitted", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(BrokerRequestState.ReadyToSend, (await fixture.RequestRow(requestId)).State);
    }

    /// <summary>
    /// The other side of the same rule: `sent` with `emailed_at` null is a send that failed **after**
    /// B4's review committed, so a Resend there is not a bypass — it is the only way to deliver work
    /// the customer has already finished and can no longer redo (their link is locked and they are
    /// gone). Slice 5.3 widened `Resend` for exactly this state, and this test is the widening.
    /// </summary>
    [Fact]
    public async Task AFailedSendLeavesTheRequestSentAndResendable()
    {
        using var broker = await fixture.CreateBroker();
        var (requestId, _) = await broker.ReadyToSendRequest(fixture);
        var recipient = fixture.RecipientFor();

        var original = fixture.Fake.CurrentValue;
        try
        {
            fixture.Fake.CurrentValue = new FakeOptions { FailureRate = 1.0 };
            (await broker.SendRequest(requestId)).EnsureSuccessStatusCode();
        }
        finally
        {
            fixture.Fake.CurrentValue = original;
        }

        // The transition stands and the failure is visible rather than silent — §5.3's ordering, and
        // the reason the state commits first.
        var failed = await fixture.RequestRow(requestId);
        Assert.Equal(BrokerRequestState.Sent, failed.State);
        Assert.Null(failed.EmailedAt);

        var before = fixture.Email.AllTo(recipient).Count;
        Assert.Equal(HttpStatusCode.OK, (await broker.ResendRequest(requestId)).StatusCode);

        Assert.Equal(before + 1, fixture.Email.AllTo(recipient).Count);
        Assert.NotNull((await fixture.RequestRow(requestId)).EmailedAt);

        // And once it has genuinely gone, the artboard's "no resend" applies again.
        var again = await broker.ResendRequest(requestId);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("already_emailed", await again.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendIsRefusedOnAnyStateButReadyToSend()
    {
        using var broker = await fixture.CreateBroker();

        // An Option 1 draft: never reaches `ready_to_send` at all.
        var draft = await broker.CreateRequestDraft();
        var onDraft = await broker.SendRequest(draft);
        Assert.Equal(HttpStatusCode.Conflict, onDraft.StatusCode);
        Assert.Contains("illegal_transition", await onDraft.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        // An Option 2 link the customer has not finished: B4's "nothing to review yet".
        var link = await fixture.IssueLink(broker.Client);
        var onLink = await broker.SendRequest(link.RequestId);
        Assert.Equal(HttpStatusCode.Conflict, onLink.StatusCode);
        Assert.Contains("illegal_transition", await onLink.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Ownership on every route, and a 404 rather than a 403 — a 403 confirms the id exists, and these
    /// ids appear in URLs a broker can share.
    /// </summary>
    [Fact]
    public async Task AnotherBrokersRequestIsNotFound()
    {
        using var owner = await fixture.CreateBroker();
        var (requestId, _) = await owner.ReadyToSendRequest(fixture);

        using var stranger = await fixture.CreateBroker();
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.SendRequest(requestId)).StatusCode);
        Assert.Equal(BrokerRequestState.ReadyToSend, (await fixture.RequestRow(requestId)).State);
    }

    /// <summary>
    /// The detail B4 renders. Read-only by rule (pass 3: editing would put the broker's words in the
    /// customer's submission), so this is the whole of what the screen has to work with.
    /// </summary>
    [Fact]
    public async Task TheDetailCarriesWhatTheCustomerEntered()
    {
        using var broker = await fixture.CreateBroker();
        var (requestId, _) = await broker.ReadyToSendRequest(fixture);

        var detail = await (await broker.Client.GetAsync(BrokerFlows.RequestPath(requestId)))
            .Content.ReadFromJsonAsync<BrokerDetailBodyDto>();

        Assert.NotNull(detail);
        Assert.Equal(BrokerRequestStates.ReadyToSend, detail.State);
        Assert.Equal("PLACEHOLDER Insured", detail.InsuredName);
        Assert.Equal(25000m, detail.CarValue);
        Assert.Equal(750m, detail.EstimatedPremium);
        Assert.NotNull(detail.SubmittedAt);
    }
}
