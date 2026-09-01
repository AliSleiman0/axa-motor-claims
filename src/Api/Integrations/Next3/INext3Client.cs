namespace Api.Integrations.Next3;

// Signature is design.md §6.2 verbatim.
public interface INext3Client
{
    Task<ClaimDetail?> GetClaim(string visaNo, CancellationToken ct);

    Task<IReadOnlyList<ClaimSummary>> SearchClaims(string? plateNo, string? visaNo, CancellationToken ct);

    Task RecordArrival(string visaNo, ArrivalInfo info, string clientRef, CancellationToken ct);

    Task UploadDocument(string visaNo, DocumentPush doc, string clientRef, CancellationToken ct);

    Task<IReadOnlyList<Next3Expert>> GetExperts(CancellationToken ct);

    Task<IReadOnlyList<Next3Garage>> GetGarages(CancellationToken ct);
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
/// §6.1's arrival data: when the expert got there, and where.
/// </summary>
/// <param name="OccurredAt">
/// **An instant, not a date and a time.** §6.1 asks for "date, time, GPS" and this record carried
/// exactly that until slice 2.4 tried to fill it: splitting the moment into a
/// <c>DateOnly</c> + <c>TimeOnly</c> pair throws the offset away at the point of capture, and an
/// expert who arrives at 01:30 in GST is then reported to NEXT3 as arriving on the previous calendar
/// day. Arrival time is the field a claims dispute turns on (§9), and the outbox row is durable — a
/// push queued with the wrong day cannot be repaired later, because the payload no longer contains
/// the information needed to correct it. So the zone conversion happens at the far edge, in
/// <c>RealNext3Client</c>, from <c>Next3:ArrivalTimeZone</c> (#6) — which is where every other NEXT3
/// formatting decision already lives.
/// </param>
public sealed record ArrivalInfo(DateTimeOffset OccurredAt, double Latitude, double Longitude);

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

/// <summary>
/// Expert master data (§6.1 "Expert list"); seeds `expert_profile` per §4. `Email` added slice 7.5
/// (Q7's answer supplies `CRT_EMAIL`) — `expert_profile.Email` is required, and the sync job's
/// create branch has no other source for it.
/// </summary>
public sealed record Next3Expert(string Next3Id, string Name, string Mobile, string Email, bool Active);

/// <summary>
/// Garage master data (§6.1 "Garage list"; #7, resolved 2026-08-31, slice 7.5). Same shape as
/// <see cref="Next3Expert"/> deliberately — the client's own reference query
/// (`docs/client-answers-2026-08-31/suppliers-list.sql`) returns identical columns for both
/// branches, tagged only by an `'EXPERT'`/`'GARAGE'` literal — but kept as its own type rather than
/// reused, because "an expert" and "a garage" reusing one DTO would read as a modelling mistake the
/// moment the two master-data shapes diverge (CLAUDE.md: three similar lines beat a premature
/// abstraction, and a type used for two conceptually different things is the DTO-level version of
/// premature).
/// </summary>
public sealed record Next3Garage(string Next3Id, string Name, string Mobile, string Email, bool Active);

/// <summary>
/// The two NEXT3 folder names the BRD itself names (design.md §5.1, §5.2). Not client-configurable
/// data — the BRD states them — so they are constants here rather than placeholder config.
/// </summary>
public static class Next3Folders
{
    public const string ExpertDocuments = "Expert documents";
    public const string Survey = "Survey";
}
