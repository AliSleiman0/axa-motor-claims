using Microsoft.Net.Http.Headers;

namespace Api.Modules.Media;

/// <summary>
/// One job: reduce a declared content type to its media type, dropping parameters.
///
/// **Why this exists.** A browser's <c>MediaRecorder</c> declares
/// <c>audio/webm;codecs=opus</c> — the codec is part of the value it stamps on the blob, and the
/// upload carries it verbatim. Compared raw against the allow-list, every voice note is refused with
/// <c>content_type_not_allowed</c>; and the raw string would otherwise be what lands in
/// <c>document.content_type</c>, on the blob, and in the NEXT3 push payload — three places where a
/// codec parameter is noise at best and a mismatch at worst.
///
/// Stripping happens at the head of <see cref="MediaValidation.CheckContent"/> and the normalised
/// value is handed back to the caller, so **what was validated is what gets stored**. Two
/// normalisation points would be two answers that can disagree, which is the shape of bug §7.3 keeps
/// teaching.
/// </summary>
public static class MediaContentType
{
    /// <summary>
    /// <c>audio/webm;codecs=opus</c> → <c>audio/webm</c>. Returns null for null or blank, and the
    /// trimmed input unchanged when it cannot be parsed as a media type — an unparseable value is
    /// not an allow-list member either way, so refusing it is the allow-list's job, not this one's.
    /// </summary>
    public static string? Normalise(string? declaredContentType)
    {
        if (string.IsNullOrWhiteSpace(declaredContentType))
        {
            return null;
        }

        var trimmed = declaredContentType.Trim();

        return MediaTypeHeaderValue.TryParse(trimmed, out var parsed) && parsed.MediaType.HasValue
            ? parsed.MediaType.Value
            : trimmed;
    }
}
