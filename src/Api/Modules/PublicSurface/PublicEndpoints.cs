using Api.Infrastructure;
using Api.Modules.Audit;
using Api.Modules.Broker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Modules.PublicSurface;

/// <summary>What the customer's device may fill in (§5.3 P1). All optional until submit validates them.</summary>
public sealed record PublicSubmissionDto(
    string? InsuredName,
    string? InsuranceType,
    string? InsuredAddress,
    decimal? CarValue,
    decimal? EstimatedPremium,
    DateOnly? EffectiveDate);

/// <summary>What the page is told before submission — deliberately almost nothing (§9.1).</summary>
public sealed record PublicLinkView(
    string State, DateTime ExpiresAt, int MaxFiles, int MaxFileMb);

/// <summary>
/// The only unauthenticated surface in the system (design.md §3). The boundary is structural: this
/// namespace cannot reference <c>INext3Client</c> or any user/profile type, and architecture rule 2
/// fails the build if that ever changes.
/// </summary>
/// <remarks>
/// Every failure mode — unknown token, malformed token, expired, already submitted — returns the
/// same bare 404. There are no distinguishable states to enumerate, so a scraper holding a bad link
/// learns nothing, not even whether the link ever existed.
/// </remarks>
public static class PublicEndpoints
{
    public static IEndpointRouteBuilder MapPublicEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(PublicRateLimiting.PathPrefix);

        group.MapGet("/{token}", async (
            string token, PublicLinkTokenService links, AppDbContext db, AuditWriter audit,
            IOptionsMonitor<PublicLinkOptions> options, CancellationToken ct) =>
        {
            var (_, link) = await links.Resolve(token, ct);
            if (link is null)
            {
                return NotFound();
            }

            if (links.MarkOpened(link))
            {
                // Actor is null: a member of the public, not a user (§9). The token id identifies
                // the session without putting the credential itself in the log.
                audit.Append(
                    null, AuditActions.PublicLinkOpened, AuditEntityKinds.PublicLinkToken, link.Token.Id,
                    new { BrokerRequestId = link.Request.Id });

                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Slice 5.2 made `broker_request.state` a concurrency token for the submit path,
                    // and this write is the one place where losing that race means **nothing at all**:
                    // two simultaneous opens of the same link — a double tap on an SMS, a prefetch, a
                    // pull-to-refresh — both move `link_issued` to `customer_in_progress`, and the
                    // loser's row already says what it was trying to say. Swallowed rather than
                    // rethrown because there is no global exception handler and §9.1 requires this
                    // surface to answer uniformly; a 500 here would be a new way to distinguish one
                    // token's state from another's.
                    db.ChangeTracker.Clear();
                }
            }

            var current = options.CurrentValue;
            return Results.Ok(new PublicLinkView(
                link.Request.State.ToDbValue(), link.Token.ExpiresAt, current.MaxFiles, current.MaxFileMb));
        });

        group.MapPost("/{token}/submit", async (
            string token, PublicSubmissionDto dto, PublicLinkTokenService links, AppDbContext db,
            AuditWriter audit, CancellationToken ct) =>
        {
            var (_, link) = await links.Resolve(token, ct);
            if (link is null)
            {
                return NotFound();
            }

            if (!IsComplete(dto))
            {
                // A 400 here is safe: the caller already proved it holds a live token, so this
                // reveals nothing a valid holder does not already know.
                return Results.BadRequest(new { error = "incomplete_submission" });
            }

            Apply(dto, link);
            links.Lock(link);
            audit.Append(
                null, AuditActions.PublicLinkSubmitted, AuditEntityKinds.PublicLinkToken, link.Token.Id,
                new { BrokerRequestId = link.Request.Id });

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another submission locked the token between this request's read and its write
                // (§9.1: exactly one submission per link). The loser is holding a token that is now
                // locked, and a locked token is a 404 — so it leaves through the same door as every
                // other dead token, and the field values and audit row roll back with the batch.
                return NotFound();
            }

            return Results.Ok();
        });

        return app;
    }

    /// <summary>
    /// The uniform 404 of §9.1. One helper, one shape — invalid, expired and locked must be
    /// byte-identical, and they stay that way only if there is exactly one place that writes them.
    /// </summary>
    private static IResult NotFound() => Results.NotFound();

    /// <summary>
    /// The six fields of §5.3, including the customer-entered premium (§1's recorded decision, #24c).
    /// Required documents and the five mandatory car shots arrive with slices 5.3 and 6.1 — they
    /// cannot be enforced before there is anywhere to put a file.
    /// </summary>
    private static bool IsComplete(PublicSubmissionDto dto) =>
        !string.IsNullOrWhiteSpace(dto.InsuredName)
        && !string.IsNullOrWhiteSpace(dto.InsuranceType)
        && !string.IsNullOrWhiteSpace(dto.InsuredAddress)
        && dto.CarValue is > 0
        && dto.EstimatedPremium is > 0
        && dto.EffectiveDate is not null;

    private static void Apply(PublicSubmissionDto dto, ResolvedPublicLink link)
    {
        link.Request.InsuredName = dto.InsuredName;
        link.Request.InsuranceType = dto.InsuranceType;
        link.Request.InsuredAddress = dto.InsuredAddress;
        link.Request.CarValue = dto.CarValue;
        link.Request.EstimatedPremium = dto.EstimatedPremium;
        link.Request.EffectiveDate = dto.EffectiveDate;
    }
}
