namespace Api.Integrations.Next3;

// Placeholder until the sandbox exists (design.md #1); implemented in slice 3.3.
// Only Api.Composition may reference this type (arch rule 1).
public sealed class RealNext3Client : INext3Client
{
    public Task<ClaimDetail?> GetClaim(string visaNo, CancellationToken ct) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<ClaimSummary>> SearchClaims(string? plateNo, string? visaNo, CancellationToken ct) =>
        throw new NotImplementedException();

    // Slice 3.3 splits info.OccurredAt into §6.1's "date, time" here, using Next3:ArrivalTimeZone —
    // the conversion belongs at this edge and nowhere earlier, because everything upstream of it is
    // durable (see ArrivalInfo).
    public Task RecordArrival(string visaNo, ArrivalInfo info, string clientRef, CancellationToken ct) =>
        throw new NotImplementedException();

    public Task UploadDocument(string visaNo, DocumentPush doc, string clientRef, CancellationToken ct) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<Next3Expert>> GetExperts(CancellationToken ct) =>
        throw new NotImplementedException();
}
