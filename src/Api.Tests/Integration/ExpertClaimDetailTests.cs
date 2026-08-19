using System.Net;
using System.Net.Http.Json;
using Api.Modules.Audit;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Integration;

/// <summary>
/// E2 of design.md §5.1 plus §4's cache rule — "re-fetch on open; if NEXT3 is down, serve stale with
/// a staleness banner".
/// </summary>
[Collection("api")]
public sealed class ExpertClaimDetailTests(ApiFixture fixture)
{
    [Fact]
    public async Task Detail_ReturnsTheClaim_AndMarksItFresh()
    {
        var (expert, assignmentId, visa) = await Assigned();

        var detail = await expert.Client.GetFromJsonAsync<AssignmentDetailDto>(Url(assignmentId));

        Assert.Equal("fresh", detail!.ClaimStatus);
        Assert.Equal(visa, detail.Claim!.VisaNo);
        Assert.Equal("PLACEHOLDER-POL-T01", detail.Claim.PolicyNo);
        Assert.Equal(fixture.Time.GetUtcNow().UtcDateTime, detail.ClaimFetchedAt);
        expert.Dispose();
    }

    [Fact]
    public async Task Detail_RefetchesOnEveryOpen()
    {
        // §4: the cache is refreshed on open, not served blind. NEXT3 is the truth.
        var (expert, assignmentId, visa) = await Assigned();
        await expert.Client.GetAsync(Url(assignmentId));

        fixture.SeedClaim(visa, insuredName: "PLACEHOLDER Insured Renamed");
        // Under Auth.Jwt.AccessTokenMinutes (15) with ClockSkew zero — travelling further would
        // expire this client's access token and the assertion would fail as a 401.
        fixture.Time.Advance(TimeSpan.FromMinutes(5));

        var detail = await expert.Client.GetFromJsonAsync<AssignmentDetailDto>(Url(assignmentId));

        Assert.Equal("PLACEHOLDER Insured Renamed", detail!.Claim!.InsuredName);
        Assert.Equal(fixture.Time.GetUtcNow().UtcDateTime, detail.ClaimFetchedAt);
        expert.Dispose();
    }

    [Fact]
    public async Task WhenNext3IsDown_ACachedClaimIsServedAsStale()
    {
        var (expert, assignmentId, _) = await Assigned();
        var fresh = await expert.Client.GetFromJsonAsync<AssignmentDetailDto>(Url(assignmentId));

        fixture.Time.Advance(TimeSpan.FromMinutes(5)); // stays inside the access token's lifetime
        var stale = await fixture.WithNext3Down(() =>
            expert.Client.GetFromJsonAsync<AssignmentDetailDto>(Url(assignmentId)));

        Assert.Equal("stale", stale!.ClaimStatus);
        Assert.NotNull(stale.Claim);
        // The banner is only honest if the age is the age of the data, not of the request.
        Assert.Equal(fresh!.ClaimFetchedAt, stale.ClaimFetchedAt);
        expert.Dispose();
    }

    [Fact]
    public async Task WhenNext3IsDown_AndNothingIsCached_TheDetailIs503()
    {
        using var expert = await fixture.CreateMappedExpert();
        var visa = ExpertFlows.NextVisa();
        var assignmentRef = ExpertFlows.NextRef();

        var response = await fixture.WithNext3Down(async () =>
        {
            await fixture.Inject(visa, expert.Next3Id, assignmentRef);
            var id = await AssignmentId(assignmentRef);
            return await expert.Client.GetAsync(Url(id));
        });

        // Nothing to serve and nothing cached — an outage, not a missing claim.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("next3_unavailable", body!["error"]);
    }

    [Fact]
    public async Task WhenNext3DoesNotKnowTheVisa_TheClaimIsNotFound_ButTheAssignmentIsStillReturned()
    {
        using var expert = await fixture.CreateMappedExpert();
        var visa = ExpertFlows.NextVisa(); // never seeded into the fake
        var assignmentRef = ExpertFlows.NextRef();
        await fixture.Inject(visa, expert.Next3Id, assignmentRef);

        var detail = await expert.Client.GetFromJsonAsync<AssignmentDetailDto>(
            Url(await AssignmentId(assignmentRef)));

        Assert.Equal("not_found", detail!.ClaimStatus);
        Assert.Null(detail.Claim);
        Assert.Equal(visa, detail.VisaNo);
    }

    [Fact]
    public async Task FirstOpen_SetsOpenedAtAndAudits_SecondOpenChangesNeither()
    {
        var (expert, assignmentId, _) = await Assigned();

        var first = await expert.Client.GetFromJsonAsync<AssignmentDetailDto>(Url(assignmentId));
        var openedAt = first!.OpenedAt;
        Assert.Equal(fixture.Time.GetUtcNow().UtcDateTime, openedAt);

        fixture.Time.Advance(TimeSpan.FromMinutes(10));
        var second = await expert.Client.GetFromJsonAsync<AssignmentDetailDto>(Url(assignmentId));

        Assert.Equal(openedAt, second!.OpenedAt);

        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.Set<AuditLog>().CountAsync(a =>
            a.Action == AuditActions.AssignmentOpened && a.EntityId == assignmentId));
        expert.Dispose();
    }

    [Fact]
    public async Task OpenedAt_IsRecordedEvenWhenTheClaimRefreshFails()
    {
        // The expert opened it. Whether NEXT3 answered is a separate fact.
        using var expert = await fixture.CreateMappedExpert();
        var visa = ExpertFlows.NextVisa();
        var assignmentRef = ExpertFlows.NextRef();

        await fixture.WithNext3Down(async () =>
        {
            await fixture.Inject(visa, expert.Next3Id, assignmentRef);
            var id = await AssignmentId(assignmentRef);
            return await expert.Client.GetAsync(Url(id));
        });

        await using var db = fixture.CreateDbContext();
        var assignment = await db.ExpertAssignments.AsNoTracking()
            .SingleAsync(a => a.Next3AssignmentRef == assignmentRef);
        Assert.NotNull(assignment.OpenedAt);
    }

    [Fact]
    public async Task AnotherExpertsAssignment_Is404_NotForbidden()
    {
        // 403 would confirm the id exists; 404 tells a prober nothing.
        var (owner, assignmentId, _) = await Assigned();
        using var other = await fixture.CreateMappedExpert();

        var response = await other.Client.GetAsync(Url(assignmentId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        owner.Dispose();
    }

    [Fact]
    public async Task UnknownAssignmentId_Is404()
    {
        using var expert = await fixture.CreateMappedExpert();

        var response = await expert.Client.GetAsync(Url(Guid.CreateVersion7()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_IsUnauthorized()
    {
        using var client = fixture.CreateClient();

        var response = await client.GetAsync(Url(Guid.CreateVersion7()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static string Url(Guid assignmentId) => $"/api/expert/assignments/{assignmentId}";

    private async Task<Guid> AssignmentId(string assignmentRef)
    {
        await using var db = fixture.CreateDbContext();
        return (await db.ExpertAssignments.AsNoTracking()
            .SingleAsync(a => a.Next3AssignmentRef == assignmentRef)).Id;
    }

    /// <summary>An expert with one assignment whose claim is seeded in the fake.</summary>
    private async Task<(MappedExpert Expert, Guid AssignmentId, string VisaNo)> Assigned()
    {
        var expert = await fixture.CreateMappedExpert();
        var visa = fixture.SeedClaim();
        var assignmentRef = ExpertFlows.NextRef();
        await fixture.Inject(visa, expert.Next3Id, assignmentRef);
        return (expert, await AssignmentId(assignmentRef), visa);
    }
}
