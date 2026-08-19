namespace Api.Integrations.Next3;

// Signature is design.md §6.2 verbatim. Payload records are empty placeholders until slice 1.4.
public interface INext3Client
{
    Task<ClaimDetail?> GetClaim(string visaNo, CancellationToken ct);

    Task<IReadOnlyList<ClaimSummary>> SearchClaims(string? plateNo, string? visaNo, CancellationToken ct);

    Task RecordArrival(string visaNo, ArrivalInfo info, string clientRef, CancellationToken ct);

    Task UploadDocument(string visaNo, DocumentPush doc, string clientRef, CancellationToken ct);

    Task<IReadOnlyList<Next3Expert>> GetExperts(CancellationToken ct);
}

public sealed record ClaimDetail;

public sealed record ClaimSummary;

public sealed record ArrivalInfo;

public sealed record DocumentPush;

public sealed record Next3Expert;
