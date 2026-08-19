namespace Api.Modules.Claims;

/// <summary>
/// The one NEXT3 cache in the system (design.md §4). NEXT3 owns the truth; this copy is disposable,
/// never edited locally, and deletable at any time — which is why nothing holds a foreign key to it.
///
/// Named CachedClaim rather than Claim on purpose: every endpoint that reads it also takes a
/// <see cref="System.Security.Claims.ClaimsPrincipal"/>, and a type called Claim in an imported
/// namespace turns that into a permanent CS0104 fight. The table is still `claim`, which is what §4
/// actually specifies.
/// </summary>
public sealed class CachedClaim
{
    /// <summary>Natural key — §4 keys this table on the visa number, not a surrogate id.</summary>
    public required string VisaNo { get; set; }

    public required string PolicyNo { get; set; }

    public required string PlateNo { get; set; }

    public required string InsuredName { get; set; }

    public required string InsuredPhone { get; set; }

    public required string CarMakeModel { get; set; }

    public required string City { get; set; }

    public DateOnly AccidentDate { get; set; }

    /// <summary>When NEXT3 last answered for this visa — the age behind §4's staleness banner.</summary>
    public DateTime FetchedAt { get; set; }
}
