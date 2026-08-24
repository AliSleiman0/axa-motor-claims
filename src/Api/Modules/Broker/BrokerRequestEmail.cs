using System.Globalization;
using Api.Infrastructure;
using Api.Integrations.Blob;
using Api.Integrations.Email;
using Api.Modules.Media;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Broker;

/// <summary>Why an email cannot be built or sent, or null when it can (slice 5.2).</summary>
public sealed record BrokerEmailRefusal(int StatusCode, string ErrorCode);

/// <summary>
/// design.md §5.3's terminal act: the six fields routed to the AXA recipient for their insurance type,
/// with every attached document.
///
/// **Recipients and types are placeholder config (#13, #14), never literals** — the routing table is
/// read per send through <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/>, so a
/// corrected recipient reaches the next send without a restart.
///
/// The attachments are read from **blob storage**, not from anything the upload held on to: the upload
/// is a different request, minutes or days earlier, and the bytes AXA receives must be the bytes that
/// were stored. That also means a swept blob (§7.3) fails the send loudly instead of producing an
/// email that is quietly missing a file.
/// </summary>
public sealed class BrokerRequestEmail(AppDbContext db, IBlobStore blobs, TimeProvider time)
{
    public async Task<(IReadOnlyList<EmailAttachment> Attachments, BrokerEmailRefusal? Refusal)> Attachments(
        Guid requestId, CancellationToken ct)
    {
        var documents = await db.Documents.AsNoTracking()
            .Where(d => d.OwnerKind == DocumentOwnerKinds.BrokerRequest && d.OwnerId == requestId)
            .OrderBy(d => d.CreatedAt)
            .Select(d => new { d.FileName, d.ContentType, d.SizeBytes, d.BlobKey, d.BlobDeletedAt, d.Id })
            .ToListAsync(ct);

        // Checked before a single byte is read, and before the state transition commits: an email
        // AXA receives without the documents it was told about is worse than one that never left,
        // because only the second is visible. §7.3 keeps a broker document's bytes until
        // `BrokerBlobDays` after the send, so this can only bite a Resend of an already-delivered
        // request — which is exactly the case where refusing is right.
        if (documents.Any(d => d.BlobDeletedAt is not null))
        {
            return ([], new BrokerEmailRefusal(
                StatusCodes.Status409Conflict, "attachments_unavailable"));
        }

        var attachments = documents
            .Select(d => new EmailAttachment(
                d.FileName ?? $"{d.Id:N}",
                d.ContentType,
                d.SizeBytes,
                token => blobs.Open(d.BlobKey, token)))
            .ToList();

        return (attachments, null);
    }

    public string Subject(BrokerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return $"Motor quotation request — {Or(request.InsuredName)}";
    }

    /// <summary>
    /// The six fields, plus who filed it and when. Plain text: nothing renders this but a mail client,
    /// and the fake writes it to a log an operator reads.
    /// </summary>
    public string Body(BrokerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var lines = new List<string>
        {
            $"Insured name:      {Or(request.InsuredName)}",
            $"Insurance type:    {Or(request.InsuranceType)}",
            $"Address:           {Or(request.InsuredAddress)}",

            // No currency symbol: none is specified anywhere in the BRD, and inventing one would be a
            // client literal outside the placeholder file. Raised as #47.
            $"Car value:         {Amount(request.CarValue)}",
            $"Estimated premium: {Amount(request.EstimatedPremium)}",
            $"Effective date:    {Date(request.EffectiveDate)}",
            string.Empty,
            $"Submitted by:      {Or(request.BrokerDisplayName)}",
            $"Reference:         {request.Id}",
            $"Prepared at:       {time.GetUtcNow().UtcDateTime:yyyy-MM-dd HH:mm} UTC",
        };

        return string.Join("\n", lines);
    }

    private static string Or(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;

    private static string Amount(decimal? value) =>
        value is { } amount ? amount.ToString("0.00", CultureInfo.InvariantCulture) : "—";

    private static string Date(DateOnly? value) =>
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "—";
}
