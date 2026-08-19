using Api.Infrastructure;
using Api.Integrations.Next3;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Claims;

/// <summary>How a lookup ended — the four outcomes §4's refresh rule can produce.</summary>
public enum ClaimLookupStatus
{
    /// <summary>NEXT3 answered; the cache now holds its answer.</summary>
    Fresh,

    /// <summary>NEXT3 was unreachable, but a previous answer is cached — §4's staleness banner.</summary>
    Stale,

    /// <summary>NEXT3 answered and does not know this visa.</summary>
    NotFound,

    /// <summary>NEXT3 was unreachable and nothing is cached. There is nothing to show.</summary>
    Unavailable,
}

public sealed record ClaimLookup(ClaimLookupStatus Status, CachedClaim? Claim);

/// <summary>
/// design.md §4's cache rule, in one place: "re-fetch on open; if NEXT3 is down, serve stale with a
/// staleness banner". Every read of a claim goes through here so the rule cannot drift between the
/// expert's detail view and the officer's lookup (slice 4.2).
/// </summary>
/// <remarks>
/// Only NEXT3 *reads* happen here. Architecture rule 3 allows that in feature code and forbids
/// RecordArrival/UploadDocument outside the outbox namespace.
/// </remarks>
public sealed partial class ClaimCache(
    AppDbContext db,
    INext3Client next3,
    TimeProvider time,
    ILogger<ClaimCache> logger)
{
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "NEXT3 claim fetch failed for visa {VisaNo}; serving cache if present.")]
    private static partial void LogFetchFailed(ILogger logger, string visaNo, Exception exception);

    /// <summary>
    /// Re-fetches the claim and updates the cache, falling back to whatever is already stored.
    /// Commits its own changes: the caller gets a cache that is either updated or untouched, never
    /// half-written.
    /// </summary>
    public async Task<ClaimLookup> Open(string visaNo, CancellationToken ct)
    {
        ClaimDetail? detail;
        try
        {
            detail = await next3.GetClaim(visaNo, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Any failure is "NEXT3 is down" from here: the caller's job is to show the expert
            // something, and the difference between a timeout and a 500 does not change that.
            LogFetchFailed(logger, visaNo, ex);
            var cached = await Cached(visaNo, ct);
            return cached is null
                ? new ClaimLookup(ClaimLookupStatus.Unavailable, null)
                : new ClaimLookup(ClaimLookupStatus.Stale, cached);
        }

        if (detail is null)
        {
            // NEXT3 answered and does not know this visa. Any cached row is left alone rather than
            // deleted — the cache is disposable, but discarding data on an answer we did not ask
            // for costs the expert their only copy if NEXT3 is wrong or the visa is re-created.
            return new ClaimLookup(ClaimLookupStatus.NotFound, null);
        }

        return new ClaimLookup(ClaimLookupStatus.Fresh, await Store(detail, ct));
    }

    private async Task<CachedClaim?> Cached(string visaNo, CancellationToken ct) =>
        await db.CachedClaims.AsNoTracking().SingleOrDefaultAsync(c => c.VisaNo == visaNo, ct);

    private async Task<CachedClaim> Store(ClaimDetail detail, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var claim = await db.CachedClaims.SingleOrDefaultAsync(c => c.VisaNo == detail.VisaNo, ct);

        if (claim is null)
        {
            claim = new CachedClaim
            {
                VisaNo = detail.VisaNo,
                PolicyNo = detail.PolicyNo,
                PlateNo = detail.PlateNo,
                InsuredName = detail.InsuredName,
                InsuredPhone = detail.InsuredPhone,
                CarMakeModel = detail.CarMakeModel,
                City = detail.City,
                AccidentDate = detail.AccidentDate,
                FetchedAt = now,
            };
            db.CachedClaims.Add(claim);

            try
            {
                await db.SaveChangesAsync(ct);
                return claim;
            }
            catch (DbUpdateException)
            {
                // Another caller cached the same visa between our read and our write — the ingestion
                // handler and the expert's first open race exactly here. The primary key settled it;
                // both wanted the same row, so take the one that landed rather than failing a read.
                db.Entry(claim).State = EntityState.Detached;
                return await Cached(detail.VisaNo, ct)
                    ?? throw new InvalidOperationException(
                        $"Claim '{detail.VisaNo}' was rejected as a duplicate but is not present.");
            }
        }
        else
        {
            claim.PolicyNo = detail.PolicyNo;
            claim.PlateNo = detail.PlateNo;
            claim.InsuredName = detail.InsuredName;
            claim.InsuredPhone = detail.InsuredPhone;
            claim.CarMakeModel = detail.CarMakeModel;
            claim.City = detail.City;
            claim.AccidentDate = detail.AccidentDate;
            claim.FetchedAt = now;
        }

        await db.SaveChangesAsync(ct);
        return claim;
    }
}
