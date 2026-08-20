using System.Net;
using System.Net.Http.Json;
using Api.Modules.Media;

namespace Api.Tests.Integration;

/// <summary>
/// <c>GET /api/config/media</c> — how the browser learns design.md §7.2's thresholds and §7.1's
/// bucket rules without a client value ever being written into TypeScript (CLAUDE.md's placeholder
/// rule names thresholds explicitly).
/// </summary>
[Collection("api")]
public sealed class MediaConfigEndpointTests(ApiFixture fixture)
{
    private sealed record ClarityBody(
        int MinWidth, int MinHeight, int BlurVarianceThreshold, int BlurAnalysisMaxEdge);

    private sealed record BucketBody(string Bucket, bool AllowUpload, string[] ContentTypes);

    private sealed record ConfigBody(ClarityBody Clarity, int MaxFileMb, BucketBody[] Buckets);

    /// <summary>
    /// The endpoint is anonymous by design: slice 5.3's Option 2 page is an unauthenticated surface
    /// running this same gate, so an authenticated-only endpoint would force a second one. Pinned
    /// here so nobody "tidies up" by adding a policy and silently breaks 5.3.
    /// </summary>
    [Fact]
    public async Task TheConfig_IsReadableWithoutAToken()
    {
        using var client = fixture.CreateClient();

        var response = await client.GetAsync(new Uri("/api/config/media", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TheConfig_ServesThePlaceholderThresholds()
    {
        using var client = fixture.CreateClient();

        var body = await client.GetFromJsonAsync<ConfigBody>(
            new Uri("/api/config/media", UriKind.Relative));

        Assert.NotNull(body);
        var expected = fixture.Clarity.CurrentValue;
        Assert.Equal(expected.MinWidth, body.Clarity.MinWidth);
        Assert.Equal(expected.MinHeight, body.Clarity.MinHeight);
        Assert.Equal(expected.BlurVarianceThreshold, body.Clarity.BlurVarianceThreshold);
        Assert.Equal(expected.BlurAnalysisMaxEdge, body.Clarity.BlurAnalysisMaxEdge);
        Assert.Equal(fixture.Media.CurrentValue.MaxFileMb, body.MaxFileMb);
    }

    /// <summary>
    /// The capture UI decides whether to offer a file picker from <c>allowUpload</c>, so §7.1's
    /// capture-only rule has to survive the wire. This is the client half of the rule whose server
    /// half is <c>ACaptureOnlyBucket_RefusesAnUploadedFile_AndWritesNothing</c>.
    /// </summary>
    [Fact]
    public async Task TheConfig_MarksTheCarPhotoBucketsCaptureOnly()
    {
        using var client = fixture.CreateClient();

        var body = await client.GetFromJsonAsync<ConfigBody>(
            new Uri("/api/config/media", UriKind.Relative));

        Assert.NotNull(body);
        Assert.Equal(MediaBuckets.All.Count, body.Buckets.Length);

        Assert.False(Bucket(body, MediaBuckets.InsuredCarPhoto).AllowUpload);
        Assert.False(Bucket(body, MediaBuckets.TpCarPhoto).AllowUpload);
        Assert.True(Bucket(body, MediaBuckets.InsuredDocuments).AllowUpload);
        Assert.True(Bucket(body, MediaBuckets.TpDocuments).AllowUpload);
        Assert.True(Bucket(body, MediaBuckets.ExpertReport).AllowUpload);

        // Slice 3.1's two: no file picker for either, because neither exists as a file to pick.
        Assert.False(Bucket(body, MediaBuckets.VoiceNote).AllowUpload);
        Assert.False(Bucket(body, MediaBuckets.DamageDiagram).AllowUpload);
    }

    /// <summary>
    /// The browser's recorder picks its format from this list — <c>MediaRecorder</c> produces
    /// <c>audio/webm</c> on Chrome and <c>audio/mp4</c> on Safari, and which of those NEXT3 accepts
    /// is #10. Serving the list means the answer lands as a config edit, not a TypeScript change.
    /// </summary>
    [Fact]
    public async Task TheConfig_ServesTheAudioTypesForTheVoiceBucket()
    {
        using var client = fixture.CreateClient();

        var body = await client.GetFromJsonAsync<ConfigBody>(
            new Uri("/api/config/media", UriKind.Relative));

        Assert.NotNull(body);
        Assert.Equal(fixture.Media.CurrentValue.AudioContentTypes, Bucket(body, MediaBuckets.VoiceNote).ContentTypes);

        // And an audio type is never offered to an image bucket.
        Assert.DoesNotContain(
            Bucket(body, MediaBuckets.DamageDiagram).ContentTypes,
            type => type.StartsWith("audio/", StringComparison.Ordinal));
    }

    /// <summary>
    /// The damage diagram renders at a fixed size (<c>DIAGRAM_RENDER</c> in
    /// <c>src/Web/src/media/diagram/render.ts</c>) and is then judged as an image by §7.2's floor.
    /// If the floor is ever raised past that size, **every diagram in the application is refused**
    /// with `image_too_small` — so the check belongs somewhere that can read the configured value,
    /// and a jsdom test cannot. This is that somewhere: raise `Clarity.MinWidth` and this goes red
    /// before anyone finds out from an expert at a roadside.
    /// </summary>
    [Fact]
    public void TheClarityFloorAdmitsADamageDiagram()
    {
        const int DiagramWidth = 1600;
        const int DiagramHeight = 1200;

        var clarity = fixture.Clarity.CurrentValue;

        Assert.True(
            clarity.MinWidth <= DiagramWidth && clarity.MinHeight <= DiagramHeight,
            $"The resolution floor is {clarity.MinWidth}x{clarity.MinHeight}, which the damage "
            + $"diagram's {DiagramWidth}x{DiagramHeight} render cannot clear. Raise DIAGRAM_RENDER in "
            + "src/Web/src/media/diagram/render.ts to match, or every diagram will be refused.");
    }

    /// <summary>
    /// An image bucket must not advertise PDFs: the input's <c>accept</c> attribute comes from this
    /// list, and offering a type the server will refuse with 415 is a rejection the expert cannot act
    /// on at the roadside.
    /// </summary>
    [Fact]
    public async Task TheConfig_ServesEachBucketsContentTypes()
    {
        using var client = fixture.CreateClient();

        var body = await client.GetFromJsonAsync<ConfigBody>(
            new Uri("/api/config/media", UriKind.Relative));

        Assert.NotNull(body);
        var media = fixture.Media.CurrentValue;
        Assert.Equal(media.ImageContentTypes, Bucket(body, MediaBuckets.InsuredCarPhoto).ContentTypes);
        Assert.Equal(
            media.DocumentContentTypes, Bucket(body, MediaBuckets.InsuredDocuments).ContentTypes);
    }

    /// <summary>
    /// Proves the values are read per request rather than captured at startup — which is what makes a
    /// client answer to #9 a config edit rather than a redeploy, since the placeholder file is loaded
    /// with <c>reloadOnChange</c>.
    /// </summary>
    [Fact]
    public async Task TheConfig_ReflectsAChangedThreshold()
    {
        using var client = fixture.CreateClient();
        var original = fixture.Clarity.CurrentValue;

        fixture.Clarity.CurrentValue = new ClarityOptions
        {
            MinWidth = original.MinWidth,
            MinHeight = original.MinHeight,
            BlurVarianceThreshold = original.BlurVarianceThreshold + 37,
            BlurAnalysisMaxEdge = original.BlurAnalysisMaxEdge,
        };

        try
        {
            var body = await client.GetFromJsonAsync<ConfigBody>(
                new Uri("/api/config/media", UriKind.Relative));

            Assert.NotNull(body);
            Assert.Equal(original.BlurVarianceThreshold + 37, body.Clarity.BlurVarianceThreshold);
        }
        finally
        {
            fixture.Clarity.CurrentValue = original;
        }
    }

    private static BucketBody Bucket(ConfigBody body, string bucket) =>
        body.Buckets.Single(b => b.Bucket == bucket);
}
