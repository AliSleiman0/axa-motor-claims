using System.Net;

namespace Api.Tests.Integration;

/// <summary>
/// design.md §9.1's <c>PublicLink.ValidityDays</c> boundary, at the HTTP surface (slice 7.1).
/// </summary>
/// <remarks>
/// <c>PublicLinkLifecycleTests</c> already pins the rule itself — <c>Evaluate</c> compares with
/// <c>&lt;=</c>, so the instant of expiry is expired — and it pins it exhaustively against a supplied
/// clock. What no test covers is the **wiring**: that the endpoint reads the same clock the token was
/// issued against, hands the verdict to the same uniform 404, and does not, say, re-derive validity
/// from <c>options.ValidityDays</c> at read time. A pure rule and a correct endpoint are two claims,
/// and a link that outlived its window by a day would look exactly like this from the outside.
/// </remarks>
[Collection("api")]
public sealed class PublicLinkExpiryEndpointTests(ApiFixture fixture)
{
    /// <summary>
    /// The clock lands **exactly** on <c>ExpiresAt</c> — the fake provider makes that an instant
    /// rather than a race, which is the only reason a boundary this sharp is testable over HTTP.
    /// </summary>
    [Fact]
    public async Task AtTheInstantOfExpiry_TheEndpointAnswersTheUniform404()
    {
        var link = await IssueLinkBeforeMovingTheClock();

        fixture.Time.Advance(Validity());

        using var customer = fixture.CreatePublicClient();
        var response = await customer.GetAsync($"/public/{link.Token}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OneSecondBeforeExpiry_TheEndpointStillAnswers200()
    {
        var link = await IssueLinkBeforeMovingTheClock();

        fixture.Time.Advance(Validity() - TimeSpan.FromSeconds(1));

        using var customer = fixture.CreatePublicClient();
        var response = await customer.GetAsync($"/public/{link.Token}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private TimeSpan Validity() => TimeSpan.FromDays(fixture.PublicLink.CurrentValue.ValidityDays);

    /// <summary>
    /// The order is not incidental. Issuing needs an authenticated broker, the clock is shared by the
    /// whole serialized collection, and an access token is good for <c>Auth:Jwt:AccessTokenMinutes</c>
    /// — so the sign-in and the issue both have to happen on this side of the advance (the 2.1 lesson,
    /// recorded on <c>MediaFlows.BackdateSentAt</c>). Nothing after it needs a session.
    /// </summary>
    private Task<CreateLinkResponse> IssueLinkBeforeMovingTheClock() => fixture.IssueLink();
}
