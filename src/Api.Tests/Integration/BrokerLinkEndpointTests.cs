using System.Net;
using System.Net.Http.Json;
using Api.Modules.Audit;
using Api.Modules.Users;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Integration;

/// <summary>B3 of design.md §5.3 — only a broker may create an Option 2 link.</summary>
[Collection("api")]
public sealed class BrokerLinkEndpointTests(ApiFixture fixture)
{
    [Theory]
    [InlineData(UserRole.Expert)]
    [InlineData(UserRole.Garage)]
    [InlineData(UserRole.ClaimOfficer)]
    [InlineData(UserRole.Admin)]
    public async Task NonBrokerRoles_AreForbidden(UserRole role)
    {
        var user = await fixture.CreateUser(role, UserStatus.Active);
        using var client = fixture.CreateClient();
        client.WithBearer((await fixture.Login(client, user.Phone)).AccessToken);

        var response = await client.PostAsJsonAsync("/api/broker/link-requests", new { customerMobile = (string?)null });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_IsUnauthorized()
    {
        using var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync("/api/broker/link-requests", new { customerMobile = (string?)null });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Broker_GetsTheRawTokenExactlyOnce_AndItIsAUsableUrl()
    {
        var link = await fixture.IssueLink();

        Assert.False(string.IsNullOrWhiteSpace(link.Token));
        Assert.Equal($"/public/{link.Token}", link.Url);

        // There is no read-back endpoint: the raw token exists only in this response.
        using var customer = fixture.CreatePublicClient();
        Assert.Equal(HttpStatusCode.OK, (await customer.GetAsync(link.Url)).StatusCode);
    }

    [Fact]
    public async Task Issue_IsAuditedAgainstTheBroker()
    {
        var broker = await fixture.CreateUser(UserRole.Broker, UserStatus.Active);
        using var client = fixture.CreateClient();
        client.WithBearer((await fixture.Login(client, broker.Phone)).AccessToken);

        var link = await fixture.IssueLink(client);

        await using var db = fixture.CreateDbContext();
        var tokenId = (await db.PublicLinkTokens.AsNoTracking()
            .SingleAsync(t => t.BrokerRequestId == link.RequestId)).Id;
        var row = await db.Set<AuditLog>().AsNoTracking()
            .SingleAsync(a => a.Action == AuditActions.PublicLinkIssued && a.EntityId == tokenId);

        Assert.Equal(broker.Id, row.ActorUserId);
        Assert.Equal(AuditEntityKinds.PublicLinkToken, row.EntityKind);
    }

    [Fact]
    public async Task TwoLinks_AreDistinct_AndEachOpensItsOwnRequest()
    {
        using var broker = await fixture.CreateBrokerClient();

        var first = await fixture.IssueLink(broker);
        var second = await fixture.IssueLink(broker);

        Assert.NotEqual(first.Token, second.Token);
        Assert.NotEqual(first.RequestId, second.RequestId);
    }

    [Fact]
    public async Task InvalidCustomerMobile_IsRejected()
    {
        using var broker = await fixture.CreateBrokerClient();

        var response = await broker.PostAsJsonAsync(
            "/api/broker/link-requests", new { customerMobile = "not-a-phone" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CustomerMobile_IsOptional_UntilTheDeliveryChannelIsAnswered()
    {
        // #24a: until AXA says SMS, the broker copies the link, so a mobile is not required.
        using var broker = await fixture.CreateBrokerClient();

        var response = await broker.PostAsJsonAsync(
            "/api/broker/link-requests", new { customerMobile = (string?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
