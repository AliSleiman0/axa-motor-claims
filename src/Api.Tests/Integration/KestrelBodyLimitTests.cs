using Api.Modules.Media;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Api.Tests.Integration;

/// <summary>
/// The global request-body ceiling added in slice 7.2.
/// </summary>
/// <remarks>
/// <para>
/// Before it, Kestrel's 30 MB default was in force everywhere outside <c>/public/*</c>: an
/// authenticated caller could stream 29 MB through TLS and the whole pipeline before
/// <c>Media:MaxFileMb</c> refused it inside the multipart reader.
/// </para>
/// <para>
/// **This asserts the option, not the refusal, and that is the honest claim available here.**
/// <c>TestServer</c> does not run Kestrel — it dispatches in process and enforces no server limit —
/// so a test that posted 20 MB would prove nothing about deployment either way. What the booted host
/// *does* have is the configured <see cref="KestrelServerOptions"/>, which is the thing the wiring
/// produces; the refusal itself belongs to the container smoke, like §10's readiness 503.
/// </para>
/// <para>
/// The expected value is worked out by hand — 16 MiB for a 15 MB per-file cap — rather than recomputed
/// from <c>MaxFileMb</c>. Recomputing it would be a test asserting the code's own arithmetic back to
/// itself, which can never go red (2.4's lesson, and 2.5's). Raising <c>Media:MaxFileMb</c> is
/// therefore meant to turn this red: the ceiling is read once at startup, so the two numbers move
/// together deliberately or not at all.
/// </para>
/// </remarks>
[Collection("api")]
public sealed class KestrelBodyLimitTests(ApiFixture fixture)
{
    [Fact]
    public void TheGlobalBodyCap_TracksTheMediaCap()
    {
        var kestrel = fixture.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value;

        Assert.Equal(16L * 1024 * 1024, kestrel.Limits.MaxRequestBodySize);
    }

    /// <summary>
    /// The two are not the same number, and the gap is the multipart envelope: boundaries, the two
    /// metadata parts and a <c>Content-Disposition</c> header per part. Without headroom a legal
    /// 15 MB file would be refused by the server before the pipeline saw it, with a bare 413 instead
    /// of <c>file_too_large</c> — the wrong error, from the wrong layer, for a file that is allowed.
    /// </summary>
    [Fact]
    public void TheCeilingLeavesRoomAboveThePerFileCap()
    {
        var kestrel = fixture.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value;
        var perFile = fixture.Services.GetRequiredService<IOptions<MediaOptions>>().Value.MaxFileBytes;

        Assert.True(kestrel.Limits.MaxRequestBodySize > perFile);
    }

    /// <summary>
    /// §9.1's much tighter per-request cap is unaffected. <c>PublicBodySizeMiddleware</c> lowers
    /// <c>IHttpMaxRequestBodySizeFeature</c> per request, and that feature can only lower the
    /// effective limit — so the global ceiling is a backstop for the authenticated surface and never
    /// a loosening of the public one.
    /// </summary>
    [Fact]
    public void ThePublicCapIsStillTheStricterOne()
    {
        var kestrel = fixture.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value;

        Assert.True(fixture.PublicLink.CurrentValue.MaxFileBytes < kestrel.Limits.MaxRequestBodySize);
    }
}
