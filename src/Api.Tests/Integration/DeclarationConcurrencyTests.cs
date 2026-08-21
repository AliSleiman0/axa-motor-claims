using System.Net;
using Api.Modules.Declarations;
using Api.Modules.Media;
using Api.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Integration;

/// <summary>
/// CLAUDE.md's first recurring bug class, sixth outing: <em>"this may only happen once" belongs in the
/// schema or the WHERE, never in an `if`</em>.
///
/// <c>declaration.state</c> is the EF concurrency token, so every UPDATE carries
/// <c>AND state = &lt;what we read&gt;</c> and four simultaneous requests cannot all win. **Remove
/// <c>IsConcurrencyToken()</c> from <c>DeclarationConfiguration</c> and every test in this class goes
/// red** — verified by doing it, not by assuming it.
///
/// The parallel approve is also this slice's real atomicity proof. <c>WithNext3Down</c> cannot fail a
/// request part way through (one <c>FakeBehavior</c> serves NEXT3 and all three senders, so it fails
/// the visa check instead), and there is no other seam over HTTP. The losing approvals are that seam:
/// each one stages a comment row, three document flips and four outbox rows, and every one of those
/// has to vanish when its <c>SaveChanges</c> loses the race.
/// </summary>
[Collection("api")]
public sealed class DeclarationConcurrencyTests(ApiFixture fixture)
{
    private const int Racers = 4;

    [Fact]
    public async Task ConcurrentSubmits_ProduceExactlyOneTransition()
    {
        using var garage = await fixture.CreateGarage();
        using var officer = await fixture.CreateOfficer();
        var id = await garage.CreateDraft();
        (await garage.UploadCarPhoto(id)).EnsureSuccessStatusCode();

        var responses = await Task.WhenAll(
            Enumerable.Range(0, Racers).Select(_ => garage.Submit(id)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(
            Racers - 1,
            responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        var row = await fixture.DeclarationRow(id);
        Assert.Equal(DeclarationState.Submitted, row.State);
        Assert.NotNull(row.SubmittedAt);

        // One transition, one audit row. A second `declaration_submitted` would mean two officers'
        // worth of notifications for one submission, and a trail that disagrees with the row.
        Assert.Equal(1, await AuditCount(id, "declaration_submitted"));
    }

    [Fact]
    public async Task ConcurrentApprovals_ProduceExactlyOneTransitionAndOneSetOfSideEffects()
    {
        using var garage = await fixture.CreateGarage();
        using var officer = await fixture.CreateOfficer();
        var visa = fixture.SeedClaim();

        var id = await garage.CreateDraft();
        (await garage.UploadSurvey(id)).EnsureSuccessStatusCode();
        (await garage.UploadCarPhoto(id)).EnsureSuccessStatusCode();
        (await garage.Submit(id)).EnsureSuccessStatusCode();
        (await officer.UploadApprovalImage(id)).EnsureSuccessStatusCode();

        var documents = await fixture.DocumentRows(id);
        Assert.Equal(3, documents.Count);

        var responses = await Task.WhenAll(
            Enumerable.Range(0, Racers).Select(_ => officer.Approve(id, visa, "PLACEHOLDER approved")));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(Racers - 1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        var row = await fixture.DeclarationRow(id);
        Assert.Equal(DeclarationState.Approved, row.State);
        Assert.Equal(visa, row.VisaNo);
        Assert.Equal(officer.User.Id, row.OfficerUserId);

        // The atomicity assertions. Three losing approvals each staged a comment, three flips and
        // four outbox rows before their SaveChanges was rejected; if any of that survived, this
        // declaration would have four comments the officer wrote once, and NEXT3 would receive every
        // document four times over.
        Assert.Equal(1, await fixture.CommentCount(id));
        Assert.Equal(1, await AuditCount(id, "declaration_approved"));

        var after = await fixture.DocumentRows(id);
        Assert.All(after, d =>
        {
            Assert.Equal(DocumentPushStatuses.Queued, d.PushStatus);
            Assert.NotNull(d.OutboxMessageId);
        });

        // Exactly one outbox row per document, and no orphans left behind by the losers.
        var messageIds = after.Select(d => d.OutboxMessageId!.Value).ToList();
        Assert.Equal(3, messageIds.Distinct().Count());

        await using var db = fixture.CreateDbContext();
        var queued = await db.Set<Next3OutboxMessage>().AsNoTracking()
            .Where(m => m.VisaNo == visa)
            .ToListAsync();

        Assert.Equal(3, queued.Count);
        Assert.All(queued, m => Assert.Contains(m.Id, messageIds));

        // §5.2: the approval PNG is a distinct operation, so A2 can tell "the approval never reached
        // NEXT3" from "a photo never reached NEXT3". Read off the bucket rule, not a name comparison.
        Assert.Single(queued, m => m.Operation == Next3OutboxOperations.PushApproval);
        Assert.Equal(2, queued.Count(m => m.Operation == Next3OutboxOperations.UploadDocument));
    }

    [Fact]
    public async Task ConcurrentDecisions_CannotBothApproveAndReject()
    {
        // The nastiest race of the three, because the two paths write different columns: an approval
        // that landed after a rejection would give a rejected declaration a visa and queue its
        // documents to NEXT3 — pushing a claim AXA had decided to disregard.
        using var garage = await fixture.CreateGarage();
        using var approving = await fixture.CreateOfficer();
        using var rejecting = await fixture.CreateOfficer();
        var visa = fixture.SeedClaim();

        var id = await garage.SubmittedDeclaration();
        (await approving.UploadApprovalImage(id)).EnsureSuccessStatusCode();

        var responses = await Task.WhenAll(
            approving.Approve(id, visa, "PLACEHOLDER approve"),
            rejecting.Reject(id, "PLACEHOLDER reject"));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);

        var row = await fixture.DeclarationRow(id);
        var approved = row.State == DeclarationState.Approved;

        Assert.True(approved || row.State == DeclarationState.Rejected);
        Assert.Equal(approved ? visa : null, row.VisaNo);
        Assert.Equal(1, await fixture.CommentCount(id));

        // Whichever won, the documents match it: queued if approved, still deferred if rejected.
        var expected = approved ? DocumentPushStatuses.Queued : DocumentPushStatuses.Deferred;
        Assert.All(await fixture.DocumentRows(id), d => Assert.Equal(expected, d.PushStatus));
    }

    [Fact]
    public async Task ConcurrentStartRepairs_ProduceExactlyOneTransition()
    {
        using var garage = await fixture.CreateGarage();
        using var officer = await fixture.CreateOfficer();
        var visa = fixture.SeedClaim();

        var id = await garage.SubmittedDeclaration();
        (await officer.UploadApprovalImage(id)).EnsureSuccessStatusCode();
        (await officer.Approve(id, visa)).EnsureSuccessStatusCode();

        var responses = await Task.WhenAll(
            Enumerable.Range(0, Racers).Select(_ => garage.StartRepairs(id)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(Racers - 1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(DeclarationState.RepairsInProgress, (await fixture.DeclarationRow(id)).State);
    }

    private async Task<int> AuditCount(Guid declarationId, string action)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Set<Api.Modules.Audit.AuditLog>()
            .CountAsync(a => a.EntityId == declarationId && a.Action == action);
    }
}
