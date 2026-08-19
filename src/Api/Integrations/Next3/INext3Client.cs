namespace Api.Integrations.Next3;

// Signature is design.md §6.2 verbatim.
public interface INext3Client
{
    Task<ClaimDetail?> GetClaim(string visaNo, CancellationToken ct);

    Task<IReadOnlyList<ClaimSummary>> SearchClaims(string? plateNo, string? visaNo, CancellationToken ct);

    Task RecordArrival(string visaNo, ArrivalInfo info, string clientRef, CancellationToken ct);

    Task UploadDocument(string visaNo, DocumentPush doc, string clientRef, CancellationToken ct);

    Task<IReadOnlyList<Next3Expert>> GetExperts(CancellationToken ct);
}

/// <summary>Claim fields NEXT3 owns (design.md §6.1 "Claim details"; cached in `claim` per §4).</summary>
public sealed record ClaimDetail(
    string VisaNo,
    string PolicyNo,
    string PlateNo,
    string InsuredName,
    string InsuredPhone,
    string CarMakeModel,
    string City,
    DateOnly AccidentDate);

/// <summary>Search-result shape (§6.1 "Claim search") — enough to pick a claim, not the full detail.</summary>
public sealed record ClaimSummary(
    string VisaNo,
    string PlateNo,
    string InsuredName,
    string CarMakeModel,
    DateOnly AccidentDate);

/// <summary>
/// The three values §6.1 requires for arrival: date, time, GPS. Kept as separate values rather than
/// a single timestamp because NEXT3's field names and formats are #6 — the real client maps them.
/// </summary>
public sealed record ArrivalInfo(DateOnly Date, TimeOnly Time, double Latitude, double Longitude);

/// <summary>
/// A document push (§6.1 "Upload document"). Carries the blob key, not bytes: the outbox payload
/// stores blob keys (§4), and the pushing client reads the blob when it sends.
/// </summary>
public sealed record DocumentPush(
    string Folder,
    string DocType,
    string FileName,
    string ContentType,
    string BlobKey);

/// <summary>Expert master data (§6.1 "Expert list"); seeds `expert_profile` per §4.</summary>
public sealed record Next3Expert(string Next3Id, string Name, string Mobile, bool Active);

/// <summary>
/// The two NEXT3 folder names the BRD itself names (design.md §5.1, §5.2). Not client-configurable
/// data — the BRD states them — so they are constants here rather than placeholder config.
/// </summary>
public static class Next3Folders
{
    public const string ExpertDocuments = "Expert documents";
    public const string Survey = "Survey";
}
