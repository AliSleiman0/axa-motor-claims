using System.Net;
using System.Net.Http.Json;
using Api.Modules.Users;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Integration;

/// <summary>
/// The demo/UAT trigger of design.md §6.2 — admin-only, and the same handler the real source feeds.
/// </summary>
[Collection("api")]
public sealed class DevAssignmentEndpointTests(ApiFixture fixture)
{
    [Fact]
    public async Task Admin_CanInjectAnAssignment()
    {
        using var expert = await fixture.CreateMappedExpert();
        using var admin = await fixture.CreateAdminClient();
        var visa = fixture.SeedClaim();
        var assignmentRef = ExpertFlows.NextRef();

        var response = await ExpertFlows.PostInjection(admin, visa, expert.Next3Id, assignmentRef);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<Dictionary<string, bool>>())!["created"]);

        await using var db = fixture.CreateDbContext();
        Assert.True(await db.ExpertAssignments.AnyAsync(a => a.Next3AssignmentRef == assignmentRef));
    }

    [Fact]
    public async Task Replay_ReportsCreatedFalse()
    {
        using var expert = await fixture.CreateMappedExpert();
        using var admin = await fixture.CreateAdminClient();
        var visa = fixture.SeedClaim();
        var assignmentRef = ExpertFlows.NextRef();

        await ExpertFlows.PostInjection(admin, visa, expert.Next3Id, assignmentRef);
        var replay = await ExpertFlows.PostInjection(admin, visa, expert.Next3Id, assignmentRef);

        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.False((await replay.Content.ReadFromJsonAsync<Dictionary<string, bool>>())!["created"]);
    }

    [Fact]
    public async Task UnmappedExpert_Is422()
    {
        // Well-formed and unfixable by the caller: the NEXT3 id maps to no profile yet (#8).
        using var admin = await fixture.CreateAdminClient();
        var visa = fixture.SeedClaim();

        var response = await ExpertFlows.PostInjection(
            admin, visa, ExpertFlows.NextExpertNext3Id(), ExpertFlows.NextRef());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task IncompletePayload_Is400()
    {
        using var admin = await fixture.CreateAdminClient();

        var response = await ExpertFlows.PostInjection(admin, null, null, null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(UserRole.Expert)]
    [InlineData(UserRole.Garage)]
    [InlineData(UserRole.ClaimOfficer)]
    [InlineData(UserRole.Broker)]
    public async Task NonAdminRoles_AreForbidden(UserRole role)
    {
        var user = await fixture.CreateUser(role, UserStatus.Active);
        using var client = fixture.CreateClient();
        client.WithBearer((await fixture.Login(client, user.Phone)).AccessToken);

        var response = await ExpertFlows.PostInjection(
            client, "PLACEHOLDER-VISA-0001", "PLACEHOLDER-EXP-01", ExpertFlows.NextRef());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_IsUnauthorized()
    {
        using var client = fixture.CreateClient();

        var response = await ExpertFlows.PostInjection(
            client, "PLACEHOLDER-VISA-0001", "PLACEHOLDER-EXP-01", ExpertFlows.NextRef());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
