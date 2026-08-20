using Api.Modules.Media;

namespace Api.Tests.Unit;

/// <summary>
/// Slice 3.1. Without this, every voice note Chrome records is refused with
/// <c>content_type_not_allowed</c>: <c>MediaRecorder</c> stamps <c>audio/webm;codecs=opus</c> on the
/// blob and the upload carries that string verbatim into the allow-list compare.
/// </summary>
public class MediaContentTypeTests
{
    [Theory]
    [InlineData("audio/webm;codecs=opus", "audio/webm")]
    [InlineData("audio/webm; codecs=\"opus\"", "audio/webm")]
    [InlineData("audio/mp4;codecs=mp4a.40.2", "audio/mp4")]
    [InlineData("  audio/ogg ; codecs=opus  ", "audio/ogg")]
    [InlineData("text/plain; charset=utf-8", "text/plain")]
    public void ParametersAreStripped(string declared, string expected)
    {
        Assert.Equal(expected, MediaContentType.Normalise(declared));
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("application/pdf")]
    public void ATypeWithNoParametersIsUnchanged(string declared)
    {
        Assert.Equal(declared, MediaContentType.Normalise(declared));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NothingNormalisesToNothing(string? declared)
    {
        Assert.Null(MediaContentType.Normalise(declared));
    }

    [Fact]
    public void AnUnparseableValueIsHandedBackForTheAllowListToRefuse()
    {
        // Not silently blanked: refusing an unrecognised type is the allow-list's job, and a null
        // here would make the rejection `content_type_not_allowed` for a different reason than the
        // one that happened.
        Assert.Equal("not-a-media-type", MediaContentType.Normalise("  not-a-media-type  "));
    }
}
