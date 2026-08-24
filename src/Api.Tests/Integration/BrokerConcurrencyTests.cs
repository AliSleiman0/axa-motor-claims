using System.Net;
using Api.Modules.Audit;
using Api.Modules.Broker;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Integration;

/// <summary>
/// CLAUDE.md's first recurring bug class, seventh outing: <em>"this may only happen once" belongs in
/// the schema or the WHERE, never in an `if`</em>.
///
/// Two once-only rules here, and they need **two different guards**, which is the point of the file.
///
/// A **submit** changes the state, so `broker_request.state` is the EF concurrency token and every
/// UPDATE carries <c>AND state = 'draft'</c>. Remove <c>IsConcurrencyToken()</c> from
/// <c>BrokerRequestConfiguration</c> and the first test goes red — verified by doing it.
///
/// A **Resend** changes no state at all, so that token cannot arbitrate it: both racers would carry
/// the same value and both UPDATEs would match. It is claimed on <c>emailed_at</c> instead, in the
/// `WHERE` of a conditional update issued before the sender is called. Remove that claim — send first
/// and write `emailed_at` after — and the second test goes red with two emails at AXA's desk for one
/// quotation request. Found by the db-reviewer, which is worth recording: the token was in, the
/// comment claimed it covered this, and it did not.
/// </summary>
[Collection("api")]
public sealed class BrokerConcurrencyTests(ApiFixture fixture)
{
    private const int Racers = 4;

    [Fact]
    public async Task ConcurrentSubmits_ProduceOneTransitionAndOneEmail()
    {
        using var broker = await fixture.CreateBroker();
        var id = await broker.CreateRequestDraft();
        (await broker.UploadBrokerDocument(id)).EnsureSuccessStatusCode();

        var recipient = fixture.RecipientFor();
        var before = fixture.Email.AllTo(recipient).Count;

        var responses = await Task.WhenAll(
            Enumerable.Range(0, Racers).Select(_ => broker.SubmitRequest(id)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(Racers - 1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        var row = await fixture.RequestRow(id);
        Assert.Equal(BrokerRequestState.Submitted, row.State);
        Assert.NotNull(row.EmailedAt);

        // The one that matters: four presses, one email. The send is deliberately *after* the commit,
        // so a guard that only stopped the second transition and not the second send would still let
        // AXA's desk receive the request four times.
        Assert.Equal(before + 1, fixture.Email.AllTo(recipient).Count);
        Assert.Equal(1, await AuditCount(id, AuditActions.BrokerRequestSubmitted));
        Assert.Equal(1, await AuditCount(id, AuditActions.BrokerRequestEmailed));
    }

    [Fact]
    public async Task ConcurrentResends_SendExactlyOnce()
    {
        using var broker = await fixture.CreateBroker();
        var id = await broker.CreateRequestDraft();
        var recipient = fixture.RecipientFor();

        // Submit with the sender down, so the request lands `submitted` with `emailed_at` null — the
        // only state Resend is offered from, and the one §5.3's ordering exists to produce.
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

        Assert.Null((await fixture.RequestRow(id)).EmailedAt);
        var before = fixture.Email.AllTo(recipient).Count;

        var responses = await Task.WhenAll(
            Enumerable.Range(0, Racers).Select(_ => broker.ResendRequest(id)));

        // Every racer is answered — a Resend that lost the claim is not an error the broker can act
        // on, and the request is now genuinely sent — but only one email exists.
        Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode || r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(before + 1, fixture.Email.AllTo(recipient).Count);

        var row = await fixture.RequestRow(id);
        Assert.NotNull(row.EmailedAt);
        Assert.Equal(recipient, row.EmailRecipient);
        Assert.Equal(1, await AuditCount(id, AuditActions.BrokerRequestEmailed));
    }

    private async Task<int> AuditCount(Guid requestId, string action)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Set<AuditLog>().AsNoTracking()
            .CountAsync(a => a.EntityKind == AuditEntityKinds.BrokerRequest
                && a.EntityId == requestId
                && a.Action == action);
    }
}
