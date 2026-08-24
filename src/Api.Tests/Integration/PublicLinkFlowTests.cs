using System.Net;
using System.Net.Http.Json;
using Api.Modules.Audit;
using Api.Modules.Broker;
using Api.Modules.PublicSurface;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Integration;

/// <summary>
/// design.md §9.1's token lifecycle over HTTP: issue → open (reusable) → submit (locks) → dead.
/// </summary>
[Collection("api")]
public sealed class PublicLinkFlowTests(ApiFixture fixture)
{
    [Fact]
    public async Task IssuedLink_StoresOnlyTheHash_AndStartsAtLinkIssued()
    {
        var link = await fixture.IssueLink();

        await using var db = fixture.CreateDbContext();
        var token = await db.PublicLinkTokens.AsNoTracking()
            .SingleAsync(t => t.BrokerRequestId == link.RequestId);

        // The raw token must be nowhere in the table, and the stored value must be its hash.
        Assert.NotEqual(link.Token, token.TokenHash);
        Assert.Equal(Api.Infrastructure.TokenHashing.Hash(link.Token), token.TokenHash);
        Assert.Equal(64, token.TokenHash.Length);
        Assert.Null(token.LockedAt);
        Assert.False(await db.PublicLinkTokens.AnyAsync(t => t.TokenHash == link.Token));

        var request = await db.BrokerRequests.AsNoTracking().SingleAsync(r => r.Id == link.RequestId);
        Assert.Equal(BrokerRequestState.LinkIssued, request.State);
        Assert.Equal(2, request.Option);
    }

    [Fact]
    public async Task Expiry_ComesFromTheConfiguredValidityDays()
    {
        var link = await fixture.IssueLink();

        var expected = fixture.Time.GetUtcNow().UtcDateTime
            .AddDays(fixture.PublicLink.CurrentValue.ValidityDays);
        Assert.Equal(expected, link.ExpiresAt);
    }

    [Fact]
    public async Task FirstOpen_MovesToCustomerInProgress_AndIsReusable()
    {
        var link = await fixture.IssueLink();
        using var customer = fixture.CreatePublicClient();

        var first = await customer.GetAsync($"/public/{link.Token}");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var view = await first.Content.ReadFromJsonAsync<PublicLinkViewDto>();
        Assert.Equal(BrokerRequestStates.CustomerInProgress, view!.State);
        Assert.Equal(fixture.PublicLink.CurrentValue.MaxFiles, view.MaxFiles);

        // §9.1: reusable until locked — the customer may leave and come back.
        var second = await customer.GetAsync($"/public/{link.Token}");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(
            BrokerRequestStates.CustomerInProgress,
            (await second.Content.ReadFromJsonAsync<PublicLinkViewDto>())!.State);
    }

    [Fact]
    public async Task ReOpening_DoesNotRewriteTheOpenedAudit()
    {
        var link = await fixture.IssueLink();
        using var customer = fixture.CreatePublicClient();

        await customer.GetAsync($"/public/{link.Token}");
        await customer.GetAsync($"/public/{link.Token}");

        await using var db = fixture.CreateDbContext();
        var tokenId = (await db.PublicLinkTokens.AsNoTracking()
            .SingleAsync(t => t.BrokerRequestId == link.RequestId)).Id;
        var opens = await db.Set<AuditLog>().AsNoTracking()
            .CountAsync(a => a.Action == AuditActions.PublicLinkOpened && a.EntityId == tokenId);

        Assert.Equal(1, opens);
    }

    [Fact]
    public async Task Submit_LocksTheToken_MovesToReadyToSend_AndKillsTheLink()
    {
        var link = await fixture.IssueLink();
        using var customer = fixture.CreatePublicClient();

        // Since slice 5.3 a submission carries at least one supporting document (§5.3), so the
        // journey opens the link and attaches one before pressing Send.
        await PublicLinkFlows.OpenAndAttach(customer, link.Token);

        var submit = await customer.PostAsJsonAsync(
            $"/public/{link.Token}/submit", PublicLinkFlows.CompleteSubmission());
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);

        await using (var db = fixture.CreateDbContext())
        {
            var token = await db.PublicLinkTokens.AsNoTracking()
                .SingleAsync(t => t.BrokerRequestId == link.RequestId);
            Assert.Equal(fixture.Time.GetUtcNow().UtcDateTime, token.LockedAt);

            var request = await db.BrokerRequests.AsNoTracking().SingleAsync(r => r.Id == link.RequestId);
            Assert.Equal(BrokerRequestState.ReadyToSend, request.State);
            Assert.Equal("PLACEHOLDER Insured", request.InsuredName);
            Assert.Equal(25000m, request.CarValue);
            Assert.Equal(750m, request.EstimatedPremium);
            Assert.NotNull(request.SubmittedAt);
        }

        // The link is dead in both directions from here.
        Assert.Equal(HttpStatusCode.NotFound, (await customer.GetAsync($"/public/{link.Token}")).StatusCode);
        var resubmit = await customer.PostAsJsonAsync(
            $"/public/{link.Token}/submit", PublicLinkFlows.CompleteSubmission());
        Assert.Equal(HttpStatusCode.NotFound, resubmit.StatusCode);
    }

    [Fact]
    public async Task ConcurrentSubmits_OnlyOneIsAccepted()
    {
        var link = await fixture.IssueLink();
        using var customer = fixture.CreatePublicClient();
        await PublicLinkFlows.OpenAndAttach(customer, link.Token);

        // §9.1: "each link accepts exactly one submission". Checking LockedAt and then writing it is
        // two steps, so simultaneous submissions can both pass the check — the rowversion on
        // public_link_token is what actually decides. Asserted on the outcome, not the mechanism:
        // a loser rejected by the concurrency token and one rejected by the ordinary locked-token
        // path are both correct, and both must be the same 404.
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            customer.PostAsJsonAsync($"/public/{link.Token}/submit", PublicLinkFlows.CompleteSubmission())));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(
            responses.Where(r => r.StatusCode != HttpStatusCode.OK),
            r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));

        await using var db = fixture.CreateDbContext();
        var token = await db.PublicLinkTokens.AsNoTracking()
            .SingleAsync(t => t.BrokerRequestId == link.RequestId);
        Assert.NotNull(token.LockedAt);

        // The losers' audit rows roll back with their batches, so the trail shows one submission.
        var submissions = await db.Set<AuditLog>().AsNoTracking()
            .CountAsync(a => a.Action == AuditActions.PublicLinkSubmitted && a.EntityId == token.Id);
        Assert.Equal(1, submissions);

        var request = await db.BrokerRequests.AsNoTracking().SingleAsync(r => r.Id == link.RequestId);
        Assert.Equal(BrokerRequestState.ReadyToSend, request.State);
    }

    [Fact]
    public async Task Submit_WithMissingFields_IsRejected_AndLeavesTheTokenUsable()
    {
        var link = await fixture.IssueLink();
        using var customer = fixture.CreatePublicClient();
        await PublicLinkFlows.OpenAndAttach(customer, link.Token);

        var incomplete = await customer.PostAsJsonAsync(
            $"/public/{link.Token}/submit", new { insuredName = "PLACEHOLDER Insured" });
        Assert.Equal(HttpStatusCode.BadRequest, incomplete.StatusCode);

        await using (var db = fixture.CreateDbContext())
        {
            var token = await db.PublicLinkTokens.AsNoTracking()
                .SingleAsync(t => t.BrokerRequestId == link.RequestId);
            Assert.Null(token.LockedAt);
        }

        // A failed attempt must not burn the link — the customer fixes the form and retries.
        var retry = await customer.PostAsJsonAsync(
            $"/public/{link.Token}/submit", PublicLinkFlows.CompleteSubmission());
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    [Fact]
    public async Task AfterExpiry_BothOpenAndSubmitAre404()
    {
        var link = await fixture.IssueLink();
        using var customer = fixture.CreatePublicClient();

        fixture.Time.Advance(TimeSpan.FromDays(fixture.PublicLink.CurrentValue.ValidityDays + 1));

        Assert.Equal(HttpStatusCode.NotFound, (await customer.GetAsync($"/public/{link.Token}")).StatusCode);
        var submit = await customer.PostAsJsonAsync(
            $"/public/{link.Token}/submit", PublicLinkFlows.CompleteSubmission());
        Assert.Equal(HttpStatusCode.NotFound, submit.StatusCode);
    }

    [Fact]
    public async Task PublicActions_AreAuditedWithNoActor_AndTheTokenId()
    {
        var link = await fixture.IssueLink();
        using var customer = fixture.CreatePublicClient();
        await PublicLinkFlows.OpenAndAttach(customer, link.Token);
        await customer.PostAsJsonAsync($"/public/{link.Token}/submit", PublicLinkFlows.CompleteSubmission());

        await using var db = fixture.CreateDbContext();
        var tokenId = (await db.PublicLinkTokens.AsNoTracking()
            .SingleAsync(t => t.BrokerRequestId == link.RequestId)).Id;

        foreach (var action in new[] { AuditActions.PublicLinkOpened, AuditActions.PublicLinkSubmitted })
        {
            var row = await db.Set<AuditLog>().AsNoTracking()
                .SingleAsync(a => a.Action == action && a.EntityId == tokenId);
            // §9: a member of the public is not a user — actor is null and the token identifies them.
            Assert.Null(row.ActorUserId);
            Assert.Equal(AuditEntityKinds.PublicLinkToken, row.EntityKind);
            Assert.Contains(link.RequestId.ToString(), row.Detail, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// **Amended deliberately in slice 5.3, and the rename is the point of the diff.** Slice 1.5 wrote
    /// this as `PublicSurface_NeverRevealsTheBrokerOrTheCustomerMobile` and asserted that the string
    /// "broker" appeared nowhere in the response, because the answer then was "the page exposes
    /// nothing" and the broker's name lived on `app_user`, out of the public module's reach.
    ///
    /// The answer changed, on purpose. design.md §9.1's "what the page exposes" always named the
    /// broker's display name, and pass-2 review decision 3 is the argument: **an anonymous page asking
    /// a member of the public to photograph their identity card is the shape of a phishing page.** A
    /// customer who cannot tell whose form this is has nothing to judge it by. So the name is revealed
    /// — from slice 5.2's snapshot column, never from `Users`, which is what keeps architecture rule 2
    /// green — and the mobile number the broker typed is still not.
    ///
    /// The half that did not move is the half that matters most: the customer's own number is still
    /// absent, so a scraper holding a stolen link learns nothing about who it was meant for.
    /// </summary>
    [Fact]
    public async Task PublicSurface_RevealsTheBrokersDisplayNameAndNeverTheCustomerMobile()
    {
        var mobile = TestPhones.Next();
        using var broker = await fixture.CreateBrokerClient();
        var link = await fixture.IssueLink(broker, mobile);

        using var customer = fixture.CreatePublicClient();
        var response = await customer.GetAsync($"/public/{link.Token}");
        var body = await response.Content.ReadAsStringAsync();
        var view = await response.Content.ReadFromJsonAsync<PublicLinkViewDto>();
        Assert.NotNull(view);

        // The broker who issued this link, by name, from `broker_request.broker_display_name`.
        string? displayName;
        await using (var db = fixture.CreateDbContext())
        {
            displayName = (await db.BrokerRequests.AsNoTracking()
                .SingleAsync(r => r.Id == link.RequestId)).BrokerDisplayName;
        }

        Assert.False(string.IsNullOrWhiteSpace(displayName));
        Assert.Equal(displayName, view.BrokerDisplayName);

        // And nothing else identifying. Asserted over the raw body rather than the DTO, so a field
        // added later without thought is caught by this test rather than by a customer.
        Assert.DoesNotContain(mobile, body, StringComparison.Ordinal);
    }

    /// <summary>
    /// A link issued before slice 5.2 has no snapshot to read, and the page must cope rather than
    /// invent a name. Simulated by clearing the column, which is exactly the state those rows are in.
    /// </summary>
    [Fact]
    public async Task ALinkWithNoSnapshottedName_ReturnsNull_RatherThanAPlaceholder()
    {
        var link = await fixture.IssueLink();

        await using (var db = fixture.CreateDbContext())
        {
            var request = await db.BrokerRequests.SingleAsync(r => r.Id == link.RequestId);
            request.BrokerDisplayName = null;
            await db.SaveChangesAsync();
        }

        using var customer = fixture.CreatePublicClient();
        var view = await (await customer.GetAsync($"/public/{link.Token}"))
            .Content.ReadFromJsonAsync<PublicLinkViewDto>();

        Assert.NotNull(view);
        Assert.Null(view.BrokerDisplayName);
    }

    /// <summary>
    /// P1's insurance-type select has to offer the list the server validates against, or a customer
    /// picks a value their own submission is then refused for. #14's list is placeholder config, so it
    /// cannot be written into TypeScript — it rides on the view instead of on `/api/broker/config`,
    /// which sits behind the broker policy that P1 has no session for.
    /// </summary>
    [Fact]
    public async Task TheView_CarriesTheInsuranceTypesTheSubmitValidatesAgainst()
    {
        var link = await fixture.IssueLink();
        using var customer = fixture.CreatePublicClient();

        var view = await (await customer.GetAsync($"/public/{link.Token}"))
            .Content.ReadFromJsonAsync<PublicLinkViewDto>();

        Assert.NotNull(view);
        Assert.NotEmpty(view.InsuranceTypes);
        Assert.Equal(fixture.Broker.CurrentValue.InsuranceTypes, view.InsuranceTypes);
    }
}
