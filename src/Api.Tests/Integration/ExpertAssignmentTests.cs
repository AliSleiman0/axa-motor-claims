using System.Net;
using System.Net.Http.Json;
using Api.Modules.Audit;
using Api.Modules.Notifications;
using Api.Modules.Users;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Integration;

/// <summary>
/// design.md §5.1 and §6.2 — an assignment arrives from NEXT3, is recorded exactly once, caches its
/// claim, notifies the expert, and is visible to nobody else.
/// </summary>
[Collection("api")]
public sealed class ExpertAssignmentTests(ApiFixture fixture)
{
    [Fact]
    public async Task Injecting_RecordsTheAssignment_CachesTheClaim_NotifiesTheExpert_AndAudits()
    {
        using var expert = await fixture.CreateMappedExpert();
        var visa = fixture.SeedClaim();
        var assignmentRef = ExpertFlows.NextRef();

        await fixture.Inject(visa, expert.Next3Id, assignmentRef);

        await using var db = fixture.CreateDbContext();
        var assignment = await db.ExpertAssignments.AsNoTracking()
            .SingleAsync(a => a.Next3AssignmentRef == assignmentRef);
        Assert.Equal(expert.User.Id, assignment.ExpertUserId);
        Assert.Equal(visa, assignment.VisaNo);
        Assert.Equal(fixture.Time.GetUtcNow().UtcDateTime, assignment.ReceivedAt);
        Assert.NotNull(assignment.NotifiedAt);
        Assert.Null(assignment.OpenedAt);

        // The §4 cache was filled by the same delivery.
        Assert.True(await db.CachedClaims.AsNoTracking().AnyAsync(c => c.VisaNo == visa));

        // The BRD's popup (§8), logged like every other send.
        var push = await db.Set<Notification>().AsNoTracking().SingleAsync(n =>
            n.RecipientUserId == expert.User.Id && n.Channel == NotificationChannels.Push);
        Assert.Equal(NotificationStatuses.Sent, push.Status);
        Assert.Equal(NotificationTemplates.AssignmentReceived, push.Template);

        var audit = await db.Set<AuditLog>().AsNoTracking()
            .SingleAsync(a => a.Action == AuditActions.AssignmentReceived && a.EntityId == assignment.Id);
        Assert.Null(audit.ActorUserId); // NEXT3 sent it, not a user
        Assert.Equal(AuditEntityKinds.ExpertAssignment, audit.EntityKind);
    }

    [Fact]
    public async Task ReplayedAssignment_IsANoOp_AndDoesNotPushTwice()
    {
        using var expert = await fixture.CreateMappedExpert();
        var visa = fixture.SeedClaim();
        var assignmentRef = ExpertFlows.NextRef();

        await fixture.Inject(visa, expert.Next3Id, assignmentRef);
        await fixture.Inject(visa, expert.Next3Id, assignmentRef);

        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.ExpertAssignments.CountAsync(a => a.Next3AssignmentRef == assignmentRef));
        Assert.Equal(1, await db.Set<AuditLog>()
            .CountAsync(a => a.Action == AuditActions.AssignmentReceived && a.Detail!.Contains(assignmentRef)));
        // A second popup for the same claim is the failure this dedupe exists to prevent.
        Assert.Equal(1, await db.Set<Notification>()
            .CountAsync(n => n.RecipientUserId == expert.User.Id && n.Channel == NotificationChannels.Push));
    }

    [Fact]
    public async Task ConcurrentDeliveriesOfTheSameRef_ProduceExactlyOneRow()
    {
        // A replayed webhook overlapping a poll: both pass any read-then-write check in application
        // code. The unique index is the guard (the 1.5 lesson); drop it and this test goes red.
        using var expert = await fixture.CreateMappedExpert();
        var visa = fixture.SeedClaim();
        var assignmentRef = ExpertFlows.NextRef();

        await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => fixture.Inject(visa, expert.Next3Id, assignmentRef)));

        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.ExpertAssignments.CountAsync(a => a.Next3AssignmentRef == assignmentRef));
    }

    [Fact]
    public async Task UnmappedExpert_RecordsNoAssignment_ButIsAudited()
    {
        var visa = fixture.SeedClaim();
        var assignmentRef = ExpertFlows.NextRef();
        var unmapped = ExpertFlows.NextExpertNext3Id();

        await fixture.Inject(visa, unmapped, assignmentRef);

        await using var db = fixture.CreateDbContext();
        Assert.False(await db.ExpertAssignments.AnyAsync(a => a.Next3AssignmentRef == assignmentRef));

        var audit = await db.Set<AuditLog>().AsNoTracking().SingleAsync(a =>
            a.Action == AuditActions.AssignmentUnmappedExpert && a.Detail!.Contains(assignmentRef));
        Assert.Null(audit.ActorUserId);
        Assert.Null(audit.EntityId); // no row was created, so there is no entity to point at
    }

    [Fact]
    public async Task InactiveExpertProfile_StillReceivesTheAssignment()
    {
        // NEXT3 owns assignment truth; the app does not second-guess who it picked.
        using var expert = await fixture.CreateMappedExpert(profileActive: false);
        var visa = fixture.SeedClaim();
        var assignmentRef = ExpertFlows.NextRef();

        await fixture.Inject(visa, expert.Next3Id, assignmentRef);

        await using var db = fixture.CreateDbContext();
        Assert.True(await db.ExpertAssignments.AnyAsync(a => a.Next3AssignmentRef == assignmentRef));
    }

    [Fact]
    public async Task WhenNext3IsDown_TheAssignmentIsStillRecorded_WithoutACacheRow()
    {
        // The whole point of the ordering in §5.1: an outage must not cost an expert the job.
        using var expert = await fixture.CreateMappedExpert();
        var visa = ExpertFlows.NextVisa();
        var assignmentRef = ExpertFlows.NextRef();

        await fixture.WithNext3Down(async () =>
        {
            await fixture.Inject(visa, expert.Next3Id, assignmentRef);
            return true;
        });

        await using var db = fixture.CreateDbContext();
        Assert.True(await db.ExpertAssignments.AnyAsync(a => a.Next3AssignmentRef == assignmentRef));
        Assert.False(await db.CachedClaims.AnyAsync(c => c.VisaNo == visa));
    }

    [Fact]
    public async Task WhenThePushFails_TheAssignmentSurvives_AndNotifiedAtStaysNull()
    {
        using var expert = await fixture.CreateMappedExpert();
        var visa = ExpertFlows.NextVisa();
        var assignmentRef = ExpertFlows.NextRef();

        // FailureRate hits every fake at once, so this breaks the push as well as NEXT3.
        await fixture.WithNext3Down(async () =>
        {
            await fixture.Inject(visa, expert.Next3Id, assignmentRef);
            return true;
        });

        await using var db = fixture.CreateDbContext();
        var assignment = await db.ExpertAssignments.AsNoTracking()
            .SingleAsync(a => a.Next3AssignmentRef == assignmentRef);
        Assert.Null(assignment.NotifiedAt);

        var push = await db.Set<Notification>().AsNoTracking().SingleAsync(n =>
            n.RecipientUserId == expert.User.Id && n.Channel == NotificationChannels.Push);
        Assert.Equal(NotificationStatuses.Failed, push.Status);
        Assert.Null(push.SentAt);
    }

    [Fact]
    public async Task AssignmentsAreVisibleOnlyToTheirOwnExpert()
    {
        using var mine = await fixture.CreateMappedExpert();
        using var theirs = await fixture.CreateMappedExpert();
        var myVisa = fixture.SeedClaim();
        var theirVisa = fixture.SeedClaim();

        await fixture.Inject(myVisa, mine.Next3Id, ExpertFlows.NextRef());
        await fixture.Inject(theirVisa, theirs.Next3Id, ExpertFlows.NextRef());

        var list = await mine.Client.GetFromJsonAsync<List<AssignmentListItemDto>>("/api/expert/assignments");

        Assert.Equal(myVisa, Assert.Single(list!).VisaNo);
    }

    [Fact]
    public async Task List_IsNewestFirst_AndCarriesTheCachedClaimFields()
    {
        using var expert = await fixture.CreateMappedExpert();
        var older = fixture.SeedClaim();
        await fixture.Inject(older, expert.Next3Id, ExpertFlows.NextRef());

        fixture.Time.Advance(TimeSpan.FromMinutes(5));
        var newer = fixture.SeedClaim(insuredName: "PLACEHOLDER Insured Newer");
        await fixture.Inject(newer, expert.Next3Id, ExpertFlows.NextRef());

        var list = (await expert.Client.GetFromJsonAsync<List<AssignmentListItemDto>>("/api/expert/assignments"))!;

        Assert.Equal([newer, older], list.Select(i => i.VisaNo));
        Assert.Equal("PLACEHOLDER Insured Newer", list[0].InsuredName);
        Assert.Equal("PLC-TEST-T1", list[0].PlateNo);
    }

    [Fact]
    public async Task ListedAssignment_WithNoCachedClaim_HasNullClaimFields()
    {
        using var expert = await fixture.CreateMappedExpert();
        var visa = ExpertFlows.NextVisa();

        await fixture.WithNext3Down(async () =>
        {
            await fixture.Inject(visa, expert.Next3Id, ExpertFlows.NextRef());
            return true;
        });

        var list = (await expert.Client.GetFromJsonAsync<List<AssignmentListItemDto>>("/api/expert/assignments"))!;

        var item = Assert.Single(list);
        Assert.Equal(visa, item.VisaNo);
        Assert.Null(item.PlateNo);
        Assert.Null(item.AccidentDate);
    }

    [Theory]
    [InlineData(UserRole.Garage)]
    [InlineData(UserRole.ClaimOfficer)]
    [InlineData(UserRole.Broker)]
    [InlineData(UserRole.Admin)]
    public async Task NonExpertRoles_AreForbidden(UserRole role)
    {
        var user = await fixture.CreateUser(role, UserStatus.Active);
        using var client = fixture.CreateClient();
        client.WithBearer((await fixture.Login(client, user.Phone)).AccessToken);

        var response = await client.GetAsync("/api/expert/assignments");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_IsUnauthorized()
    {
        using var client = fixture.CreateClient();

        var response = await client.GetAsync("/api/expert/assignments");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
