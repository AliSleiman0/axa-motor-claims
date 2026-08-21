namespace Api.Modules.Claims;

/// <summary>
/// The claim as NEXT3 owns it (§6.1), served from the §4 cache.
///
/// It lives beside <see cref="CachedClaim"/> rather than beside one module's endpoints (moved from
/// <c>Api.Modules.Expert</c> in slice 4.1): E2 shows these fields to an expert and §5.2's approved G3
/// shows the same ones to a garage, and the alternative was a declaration module that references the
/// expert module to describe a claim.
/// </summary>
public sealed record ClaimDto(
    string VisaNo,
    string PolicyNo,
    string PlateNo,
    string InsuredName,
    string InsuredPhone,
    string CarMakeModel,
    string City,
    DateOnly AccidentDate)
{
    public static ClaimDto From(CachedClaim claim)
    {
        ArgumentNullException.ThrowIfNull(claim);

        return new ClaimDto(
            claim.VisaNo,
            claim.PolicyNo,
            claim.PlateNo,
            claim.InsuredName,
            claim.InsuredPhone,
            claim.CarMakeModel,
            claim.City,
            claim.AccidentDate);
    }
}

public static class ClaimLookupStatusExtensions
{
    /// <summary>
    /// The wire value for a lookup's freshness. §4's rule is "if NEXT3 is down, serve stale with a
    /// staleness banner", so the screen has to be able to tell the three apart — and `Unavailable`
    /// never reaches a client, because there is nothing to render and the endpoint answers 503.
    /// </summary>
    public static string Describe(this ClaimLookupStatus status) => status switch
    {
        ClaimLookupStatus.Fresh => "fresh",
        ClaimLookupStatus.Stale => "stale",
        _ => "not_found",
    };
}
