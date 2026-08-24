namespace Api.Integrations.Email;

/// <summary>
/// One file on an outgoing email (slice 5.2). design.md §5.3's Option 1 terminal act is "an email
/// with all information", and the documents are part of the information.
/// </summary>
/// <param name="FileName">The stored name (§4's <c>document.file_name</c>), already sanitised.</param>
/// <param name="SizeBytes">
/// The stored size, so a sender can log or cap without opening anything. Taken from the
/// <c>document</c> row rather than measured, which is also what makes the fake's log cheap.
/// </param>
/// <param name="Open">
/// Opens the bytes — a factory over <c>IBlobStore.Open</c>, deliberately **not** a
/// <see cref="byte[]"/>. A broker request has no document cap, so buffering every attachment at
/// <c>Media:MaxFileMb</c> apiece would put an unbounded amount of a request's memory behind one send.
/// Returns null when the blob is gone, which the caller must treat as an error rather than as an
/// email quietly missing a file.
/// </param>
public sealed record EmailAttachment(
    string FileName,
    string ContentType,
    long SizeBytes,
    Func<CancellationToken, Task<Stream?>> Open);
