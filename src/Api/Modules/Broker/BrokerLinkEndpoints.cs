using System.Security.Claims;
using Api.Infrastructure;
using Api.Modules.Audit;
using Api.Modules.PublicSurface;
using Api.Modules.Users;

namespace Api.Modules.Broker;

public sealed record CreateLinkRequestDto(string? CustomerMobile);

/// <summary>
/// The raw link, shown to the broker exactly once (§9.1). It is not stored and cannot be
/// re-displayed; a broker who loses it issues a new one, which creates a new request and token.
/// </summary>
public sealed record CreateLinkResponseDto(Guid RequestId, string Token, string Url, DateTime ExpiresAt);

/// <summary>
/// B3 of design.md §5.3 — the broker creates an Option 2 request and its public link. Delivery is
/// channel-agnostic until #24a is answered: the broker copies the link (<c>PublicLink.DeliveryChannel</c>),
/// and if the answer is SMS this is the one place a send is added.
/// </summary>
public static class BrokerLinkEndpoints
{
    public static IEndpointRouteBuilder MapBrokerLinkEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/broker/link-requests").RequireAuthorization(AuthPolicies.Broker);

        group.MapPost("/", async (
            CreateLinkRequestDto dto, ClaimsPrincipal principal, AppDbContext db,
            PublicLinkTokenService links, AuditWriter audit, TimeProvider time, CancellationToken ct) =>
        {
            var brokerUserId = principal.GetUserId();
            if (brokerUserId is null)
            {
                return Results.Unauthorized();
            }

            if (dto.CustomerMobile is not null && !Phone.IsValidE164(dto.CustomerMobile))
            {
                return Results.BadRequest(new { error = "invalid_phone" });
            }

            var request = new BrokerRequest
            {
                Id = Guid.CreateVersion7(),
                BrokerUserId = brokerUserId.Value,
                Option = 2,
                State = BrokerRequestState.LinkIssued,
                CustomerMobile = dto.CustomerMobile,
                CreatedAt = time.GetUtcNow().UtcDateTime,
            };
            db.BrokerRequests.Add(request);

            var issued = links.Issue(request);
            audit.Append(
                brokerUserId, AuditActions.PublicLinkIssued, AuditEntityKinds.PublicLinkToken, issued.TokenId,
                new { BrokerRequestId = request.Id });
            // Request row, token row and audit row commit together: a token without its request is
            // an unusable link, and a request without its token is a dead end for the customer.
            await db.SaveChangesAsync(ct);

            return Results.Ok(new CreateLinkResponseDto(
                request.Id, issued.RawToken, $"{PublicRateLimiting.PathPrefix}/{issued.RawToken}", issued.ExpiresAt));
        });

        return app;
    }
}
