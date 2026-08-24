using System.Security.Claims;
using Api.Infrastructure;
using Api.Modules.Audit;
using Api.Modules.PublicSurface;
using Api.Modules.Users;
using Microsoft.Extensions.Options;

namespace Api.Modules.Broker;

/// <param name="InsuranceType">
/// Optional preset (B3, slice 5.2). "Let the customer choose" is the default and the artboard's own
/// wording; when the broker does know, it is pre-filled on the public page rather than asked twice.
/// Validated against <c>Broker:InsuranceTypes</c> (#14) so a preset cannot be a value the routing
/// table has no recipient for.
/// </param>
public sealed record CreateLinkRequestDto(string? CustomerMobile, string? InsuranceType);

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
            PublicLinkTokenService links, AuditWriter audit, IOptionsMonitor<BrokerOptions> broker,
            TimeProvider time, CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(dto);

            var brokerUserId = principal.GetUserId();
            if (brokerUserId is null)
            {
                return Results.Unauthorized();
            }

            if (dto.CustomerMobile is not null && !Phone.IsValidE164(dto.CustomerMobile))
            {
                return Results.BadRequest(new { error = "invalid_phone" });
            }

            if (dto.InsuranceType is not null
                && !broker.CurrentValue.InsuranceTypes.Contains(dto.InsuranceType, StringComparer.Ordinal))
            {
                return Results.BadRequest(new { error = "unknown_insurance_type" });
            }

            // The display name is a snapshot, written here so §5.3's public page can name the broker
            // without `Api.Modules.PublicSurface` reading `Users` — which architecture rule 2 forbids.
            // Written in 5.2 rather than 5.3 so `broker_request` takes one migration, not two.
            var request = BrokerRequest.NewLink(
                brokerUserId.Value,
                await BrokerRequestEndpoints.DisplayName(db, brokerUserId.Value, ct),
                dto.InsuranceType,
                dto.CustomerMobile,
                time.GetUtcNow().UtcDateTime);
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
