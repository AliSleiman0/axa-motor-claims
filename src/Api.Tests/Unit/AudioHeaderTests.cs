using Api.Modules.Media;
using Api.Tests.Media;

namespace Api.Tests.Unit;

/// <summary>
/// The audio half of §7.2 item 5's "what are these bytes actually?" check (slice 3.1).
///
/// Slice 5.3 reuses this path against an unauthenticated caller, so every case here eventually has
/// a hostile client on the other end — the declared content type is a claim, and this is what tests
/// it.
/// </summary>
public class AudioHeaderTests
{
    [Fact]
    public void AWebmOpensWithAnEbmlHeader()
    {
        Assert.Equal(AudioHeader.Webm, AudioHeader.Sniff(TestAudio.Webm()));
    }

    [Fact]
    public void AnMp4OpensWithAnFtypBox()
    {
        Assert.Equal(AudioHeader.Mp4, AudioHeader.Sniff(TestAudio.Mp4()));
    }

    [Fact]
    public void AnOggOpensWithItsCapturePattern()
    {
        Assert.Equal(AudioHeader.Ogg, AudioHeader.Sniff(TestAudio.Ogg()));
    }

    [Fact]
    public void AnFtypBoxThatIsNotTheFirstBoxIsNotAccepted()
    {
        // `ftyp` must be the *first* box in an ISO base-media file. Matching it anywhere would let a
        // file that merely mentions the string pass as audio.
        Assert.Null(AudioHeader.Sniff(TestAudio.Mp4WithFtypOutOfPlace()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(11)]
    public void ATruncatedHeaderIsNotGuessedAt(int length)
    {
        // 11 bytes is one short of the shortest `ftyp` box this recognises — nothing is inferred
        // from a prefix, because a wrong guess is stored as fact on the row.
        Assert.Null(AudioHeader.Sniff(TestAudio.Mp4().AsSpan(0, length)));
    }

    [Fact]
    public void ImagesAndDocumentsAreNotAudio()
    {
        Assert.Null(AudioHeader.Sniff(TestImages.Jpeg(1600, 1200)));
        Assert.Null(AudioHeader.Sniff(TestImages.Png(1600, 1200)));
        Assert.Null(AudioHeader.Sniff(TestImages.Pdf()));
    }

    [Theory]
    [InlineData("audio/webm", true)]
    [InlineData("AUDIO/MP4", true)]
    [InlineData("audio/ogg", true)]
    [InlineData("audio/mpeg", false)]
    [InlineData("image/jpeg", false)]
    [InlineData(null, false)]
    public void OnlyTheThreeAcceptedContainersCountAsAudio(string? contentType, bool expected)
    {
        // `audio/mpeg` is deliberately absent: no browser MediaRecorder produces it, and adding a
        // format nothing writes would be inventing an answer to #10.
        Assert.Equal(expected, AudioHeader.IsAudio(contentType));
    }
}
