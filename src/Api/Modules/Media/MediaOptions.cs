namespace Api.Modules.Media;

/// <summary>
/// design.md Appendix A's `Media` section — the server-side half of §7.2 item 5 ("the client check is
/// UX, not security").
///
/// The allow-lists have no built-in defaults on purpose: an empty list refuses everything, so a
/// misconfigured deployment rejects uploads loudly instead of quietly accepting whatever arrives.
/// </summary>
public sealed class MediaOptions
{
    public const string SectionName = "Media";

    public int MaxFileMb { get; set; } = 15;

    public long MaxFileBytes => MaxFileMb * 1024L * 1024L;

    /// <summary>Accepted for <see cref="MediaKind.Image"/> buckets — the car-photo buckets of §7.1.</summary>
    public IList<string> ImageContentTypes { get; } = [];

    /// <summary>Accepted for <see cref="MediaKind.Document"/> buckets: images plus documents.</summary>
    public IList<string> DocumentContentTypes { get; } = [];

    /// <summary>
    /// Accepted for <see cref="MediaKind.Audio"/> buckets — the voice note (slice 3.1).
    ///
    /// The list is here rather than in the browser because the recorder picks its format from it:
    /// `MediaRecorder` produces `audio/webm` on Chrome and `audio/mp4` on Safari, and which of those
    /// NEXT3 will take is #10 and unanswered — so it is a placeholder key that moves when the answer
    /// lands, not a constant in TypeScript.
    /// </summary>
    public IList<string> AudioContentTypes { get; } = [];

    public IList<string> AllowedFor(MediaKind kind) => kind switch
    {
        MediaKind.Image => ImageContentTypes,
        MediaKind.Audio => AudioContentTypes,
        _ => DocumentContentTypes,
    };
}
