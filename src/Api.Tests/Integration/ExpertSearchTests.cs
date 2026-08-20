using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Integration;

/// <summary>
/// design.md §5.1's search row, as corrected in slice 3.2 — a filter over the caller's own
/// assignments, not a NEXT3 lookup.
///
/// The correction is the point of most of these tests. §5.1 originally routed E1's search through
/// <c>INext3Client.SearchClaims</c>, which returns claims from the whole of NEXT3; the screen that
/// search feeds has a capture panel on it, so a hit the expert was never assigned is an invitation
/// to attach photos to a stranger's visa — the exact failure this application exists to remove.
/// </summary>
[Collection("api")]
public sealed class ExpertSearchTests(ApiFixture fixture)
{
    private const string Endpoint = "/api/expert/assignments";

    [Fact]
    public async Task SearchByVisa_ReturnsOnlyTheMatchingAssignment()
    {
        using var expert = await fixture.CreateMappedExpert();
        var wanted = fixture.SeedClaim();
        var other = fixture.SeedClaim();
        await fixture.Inject(wanted, expert.Next3Id, ExpertFlows.NextRef());
        await fixture.Inject(other, expert.Next3Id, ExpertFlows.NextRef());

        // A fragment, not the whole value: the expert is typing, not pasting.
        var list = await Search(expert, wanted[^8..]);

        Assert.Equal(wanted, Assert.Single(list).VisaNo);
    }

    [Fact]
    public async Task SearchByPlate_MatchesThroughTheClaimCache()
    {
        // The plate lives on `claim`, not on `expert_assignment`, so this only works because the
        // filter is composed after the left join — and it is the search an expert standing in front
        // of a car actually performs.
        using var expert = await fixture.CreateMappedExpert();
        var wanted = fixture.SeedClaim(plateNo: "PLC-TEST-T32A");
        var other = fixture.SeedClaim(plateNo: "PLC-TEST-T32B");
        await fixture.Inject(wanted, expert.Next3Id, ExpertFlows.NextRef());
        await fixture.Inject(other, expert.Next3Id, ExpertFlows.NextRef());

        var list = await Search(expert, "T32A");

        var item = Assert.Single(list);
        Assert.Equal(wanted, item.VisaNo);
        Assert.Equal("PLC-TEST-T32A", item.PlateNo);
    }

    [Fact]
    public async Task SearchByPlate_NeverCrossesToAnotherExpert()
    {
        // Both claims carry the *same* plate, which is what makes this discriminating: the query
        // has to be scoped to the caller, not merely returning the only row that matches.
        using var mine = await fixture.CreateMappedExpert();
        using var theirs = await fixture.CreateMappedExpert();
        const string Shared = "PLC-TEST-T32SHARED";
        var myVisa = fixture.SeedClaim(plateNo: Shared);
        var theirVisa = fixture.SeedClaim(plateNo: Shared);
        await fixture.Inject(myVisa, mine.Next3Id, ExpertFlows.NextRef());
        await fixture.Inject(theirVisa, theirs.Next3Id, ExpertFlows.NextRef());

        var list = await Search(mine, Shared);

        Assert.Equal(myVisa, Assert.Single(list).VisaNo);
    }

    [Fact]
    public async Task SearchByVisa_NeverCrossesToAnotherExpert()
    {
        using var mine = await fixture.CreateMappedExpert();
        using var theirs = await fixture.CreateMappedExpert();
        var myVisa = fixture.SeedClaim();
        var theirVisa = fixture.SeedClaim();
        await fixture.Inject(myVisa, mine.Next3Id, ExpertFlows.NextRef());
        await fixture.Inject(theirVisa, theirs.Next3Id, ExpertFlows.NextRef());

        // Searching the other expert's visa by its exact number finds nothing at all — not a 403,
        // not an empty claim: it simply is not in this expert's world.
        Assert.Empty(await Search(mine, theirVisa));
    }

    [Fact]
    public async Task AColdCacheAssignment_IsStillFoundByVisa()
    {
        // An assignment that arrived while NEXT3 was down has no cached claim, so the left join
        // yields nulls — the visa number is still on the assignment row and must still match.
        using var expert = await fixture.CreateMappedExpert();
        var visa = ExpertFlows.NextVisa();
        await fixture.WithNext3Down(async () =>
        {
            await fixture.Inject(visa, expert.Next3Id, ExpertFlows.NextRef());
            return true;
        });

        var item = Assert.Single(await Search(expert, visa));

        Assert.Equal(visa, item.VisaNo);
        Assert.Null(item.PlateNo);
    }

    [Fact]
    public async Task AColdCacheAssignment_CannotBeFoundByItsPlate()
    {
        // Recorded rather than left to be discovered: there is no plate to match until NEXT3 has
        // answered for the visa. This is the limitation E1's cold-cache hint exists to explain, and
        // the alternative — calling NEXT3 from the search — is what §5.1 was corrected away from.
        using var expert = await fixture.CreateMappedExpert();
        var visa = ExpertFlows.NextVisa();
        await fixture.WithNext3Down(async () =>
        {
            await fixture.Inject(visa, expert.Next3Id, ExpertFlows.NextRef());
            return true;
        });

        Assert.Empty(await Search(expert, "PLC-TEST-T1"));
    }

    [Fact]
    public async Task SearchIsCaseInsensitive()
    {
        // Honest about what this proves: LocalDB and Azure SQL both default to a case-insensitive
        // collation, so it passes with or without the UPPER() in the query. It pins the *behaviour*;
        // the reason the code does not simply rely on the collation is that a case-sensitive
        // deployment would otherwise change that behaviour with nothing going red.
        using var expert = await fixture.CreateMappedExpert();
        var visa = fixture.SeedClaim(plateNo: "PLC-TEST-T32CASE");
        await fixture.Inject(visa, expert.Next3Id, ExpertFlows.NextRef());

        Assert.Equal(visa, Assert.Single(await Search(expert, "plc-test-t32case")).VisaNo);
        Assert.Equal(visa, Assert.Single(await Search(expert, visa.ToLowerInvariant())).VisaNo);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnEmptyOrWhitespaceTerm_ReturnsTheFullList(string term)
    {
        using var expert = await fixture.CreateMappedExpert();
        var first = fixture.SeedClaim();
        var second = fixture.SeedClaim();
        await fixture.Inject(first, expert.Next3Id, ExpertFlows.NextRef());
        await fixture.Inject(second, expert.Next3Id, ExpertFlows.NextRef());

        // Whitespace only is the same as nothing: the term is trimmed before it is looked at, so a
        // stray space in the box does not empty the expert's list.
        Assert.Equal(2, (await Search(expert, term)).Count);
    }

    [Fact]
    public async Task ATermAtTheCap_IsAccepted()
    {
        using var expert = await fixture.CreateMappedExpert();

        var response = await Get(expert, new string('A', 64));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ATermOverTheCap_IsRejected()
    {
        using var expert = await fixture.CreateMappedExpert();

        var response = await Get(expert, new string('A', 65));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("search_term_too_long", body!["error"]);
    }

    [Fact]
    public async Task TheCapIsAppliedAfterTrimming()
    {
        // 64 characters wrapped in spaces is a 64-character search, not a 66-character one.
        using var expert = await fixture.CreateMappedExpert();

        var response = await Get(expert, $"  {new string('A', 64)}  ");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SearchWorksWhileNext3IsUnreachable()
    {
        // The whole reason §5.1 was corrected, as a test rather than a comment: search is local, so
        // it cannot fail because NEXT3 is down. With the failure rate at 1 every call to the port
        // throws, so a search that still answers is a search that never made one.
        using var expert = await fixture.CreateMappedExpert();
        var visa = fixture.SeedClaim();
        await fixture.Inject(visa, expert.Next3Id, ExpertFlows.NextRef());

        var list = await fixture.WithNext3Down(() => Search(expert, visa));

        Assert.Equal(visa, Assert.Single(list).VisaNo);
    }

    [Fact]
    public async Task SearchKeepsTheMediaCountAndTheNewestFirstOrder()
    {
        // The filter is composed into the same statement as the GroupJoin and the count subquery,
        // so both are things it could plausibly break.
        using var expert = await fixture.CreateMappedExpert();
        const string Plate = "PLC-TEST-T32ORDER";
        var older = fixture.SeedClaim(plateNo: Plate);
        await fixture.Inject(older, expert.Next3Id, ExpertFlows.NextRef());

        fixture.Time.Advance(TimeSpan.FromMinutes(5));
        var newer = fixture.SeedClaim(plateNo: Plate);
        await fixture.Inject(newer, expert.Next3Id, ExpertFlows.NextRef());

        var olderId = await AssignmentId(older);
        (await MediaFlows.UploadCarPhoto(expert.Client, olderId)).EnsureSuccessStatusCode();

        var list = await Search(expert, Plate);

        Assert.Equal([newer, older], list.Select(i => i.VisaNo));
        Assert.Equal(1, list.Single(i => i.VisaNo == older).MediaCount);
        Assert.Equal(0, list.Single(i => i.VisaNo == newer).MediaCount);
    }

    [Fact]
    public async Task ATermMatchingNothing_ReturnsAnEmptyList()
    {
        using var expert = await fixture.CreateMappedExpert();
        var visa = fixture.SeedClaim();
        await fixture.Inject(visa, expert.Next3Id, ExpertFlows.NextRef());

        Assert.Empty(await Search(expert, "PLACEHOLDER-NOTHING-MATCHES-THIS"));
    }

    [Fact]
    public async Task WildcardCharactersAreLiteral()
    {
        // `%` is a wildcard in LIKE but not in CHARINDEX, which is what `Contains` translates to.
        // Were that ever to change, this search would quietly return the expert's whole list.
        using var expert = await fixture.CreateMappedExpert();
        var visa = fixture.SeedClaim();
        await fixture.Inject(visa, expert.Next3Id, ExpertFlows.NextRef());

        Assert.Empty(await Search(expert, "%"));
    }

    private static Task<HttpResponseMessage> Get(MappedExpert expert, string term) =>
        expert.Client.GetAsync(new Uri($"{Endpoint}?q={Uri.EscapeDataString(term)}", UriKind.Relative));

    private static async Task<IReadOnlyList<AssignmentListItemDto>> Search(
        MappedExpert expert, string term)
    {
        var response = await Get(expert, term);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<AssignmentListItemDto>>())!;
    }

    private async Task<Guid> AssignmentId(string visaNo)
    {
        await using var db = fixture.CreateDbContext();
        return await db.ExpertAssignments.AsNoTracking()
            .Where(a => a.VisaNo == visaNo)
            .Select(a => a.Id)
            .SingleAsync();
    }
}
