namespace Api.Modules.Media;

/// <summary>
/// Recognises the three containers a browser's <c>MediaRecorder</c> produces, from their leading
/// bytes. Pure, span-based, no library — the same trade <see cref="ImageHeader"/> makes and for the
/// same reasons (§7.2 item 5 needs the server to check what a file *is*, not what it claims).
///
/// **Containers, not codecs, and deliberately so.** EBML is the Matroska/WebM magic and <c>ftyp</c>
/// is the ISO base-media magic; both are shared with video, and nothing here decodes a stream, so an
/// audio-only WebM and a video WebM are indistinguishable at this level. Going further would mean
/// parsing tracks — for a rule nobody has stated: audio acceptance is #10 and unanswered, and the
/// document type is what NEXT3 sorts on. Verifying the container is the honest limit, and it still
/// catches the case that matters: bytes that are not what the caller declared.
/// </summary>
public static class AudioHeader
{
    /// <summary>Chrome's <c>MediaRecorder</c> default.</summary>
    public const string Webm = "audio/webm";

    /// <summary>Safari's <c>MediaRecorder</c> default.</summary>
    public const string Mp4 = "audio/mp4";

    public const string Ogg = "audio/ogg";

    // EBML header element id — WebM and Matroska both open with it.
    private static readonly byte[] EbmlSignature = [0x1A, 0x45, 0xDF, 0xA3];

    /// <summary>True when the type names one of the containers this app accepts for audio.</summary>
    public static bool IsAudio(string? contentType) =>
        string.Equals(contentType, Webm, StringComparison.OrdinalIgnoreCase)
        || string.Equals(contentType, Mp4, StringComparison.OrdinalIgnoreCase)
        || string.Equals(contentType, Ogg, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// What these bytes actually are, or null for anything not recognised. Never guesses: an
    /// unrecognised container is refused rather than believed.
    /// </summary>
    public static string? Sniff(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith(EbmlSignature))
        {
            return Webm;
        }

        if (header.StartsWith("OggS"u8))
        {
            return Ogg;
        }

        // An ISO base-media file opens with a box: a 4-byte big-endian size, then the type. `ftyp`
        // must be the first box, so it sits at offset 4 and nowhere else — a file with `ftyp` deeper
        // in is not one of these.
        return header.Length >= 12 && header.Slice(4, 4).SequenceEqual("ftyp"u8) ? Mp4 : null;
    }
}
