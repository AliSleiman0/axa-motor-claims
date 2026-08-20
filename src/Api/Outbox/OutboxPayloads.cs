using System.Text.Json;
using Api.Integrations.Next3;

namespace Api.Outbox;

/// <summary>
/// What §4 calls the outbox payload: "JSON: blob keys, field values". One record per operation,
/// each carrying the <c>ClientRef</c> that makes the push idempotent (§6.3).
///
/// The clientRef lives in the payload rather than in a column because it is stable *by construction*
/// — the producer sets it to the id of the row being pushed (§5.1: "clientRef = document id"), so it
/// survives every retry of that outbox row unchanged. #32 asks whether NEXT3 dedupes on it; until it
/// answers, RealNext3Client will keep its own sent-log check on top (slice 3.3).
/// </summary>
public static class OutboxPayloads
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Serialize<T>(T payload)
        where T : notnull =>
        JsonSerializer.Serialize(payload, Json);

    public static T Deserialize<T>(string payload)
        where T : notnull =>
        JsonSerializer.Deserialize<T>(payload, Json)
        ?? throw new InvalidOperationException($"Outbox payload deserialized to null for {typeof(T).Name}.");
}

/// <summary>
/// The `update_arrival` payload (§5.1's Arrived row, §6.1's "Record arrival"). Date, time and GPS are
/// kept as the three separate values §6.1 requires; NEXT3's field names and formats are #6, mapped by
/// the real client, not here.
/// </summary>
public sealed record ArrivalOutboxPayload(
    string ClientRef,
    DateOnly Date,
    TimeOnly Time,
    double Latitude,
    double Longitude)
{
    public ArrivalInfo ToArrivalInfo() => new(Date, Time, Latitude, Longitude);
}

/// <summary>
/// The `upload_document` / `push_approval` payload. Carries the blob key, never bytes — §7.3 keeps
/// the binary in the transit container until the push is confirmed `sent`, and **no blob is deleted
/// before then** (the cleanup job in slice 2.3 joins on that status structurally).
/// </summary>
public sealed record DocumentOutboxPayload(
    string ClientRef,
    string Folder,
    string DocType,
    string FileName,
    string ContentType,
    string BlobKey)
{
    public DocumentPush ToDocumentPush() => new(Folder, DocType, FileName, ContentType, BlobKey);
}
