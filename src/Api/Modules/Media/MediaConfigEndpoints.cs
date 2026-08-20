using Microsoft.Extensions.Options;

namespace Api.Modules.Media;

/// <summary>design.md Appendix A's `Clarity` section (#9), as the browser's gate needs it.</summary>
public sealed record ClarityConfigDto(
    int MinWidth,
    int MinHeight,
    int BlurVarianceThreshold,
    int BlurAnalysisMaxEdge);

/// <summary>
/// One row of §7.1's matrix as the capture UI needs it: whether to offer a file picker at all, and
/// which content types to put in the input's <c>accept</c> attribute.
/// </summary>
public sealed record BucketConfigDto(
    string Bucket,
    bool AllowUpload,
    IReadOnlyList<string> ContentTypes);

public sealed record MediaConfigDto(
    ClarityConfigDto Clarity,
    int MaxFileMb,
    IReadOnlyList<BucketConfigDto> Buckets);

/// <summary>
/// The one place the browser learns §7.2's thresholds and §7.1's bucket rules.
///
/// It exists because CLAUDE.md's placeholder rule names *thresholds* explicitly: a client-specific
/// value may live only in the placeholder config, so hardcoding 1024/768/100 in TypeScript would be
/// a bug, and a browser can only reach `appsettings.Placeholders.json` over the wire. Reading through
/// <see cref="IOptionsMonitor{TOptions}"/> means an edit to that file (loaded with
/// <c>reloadOnChange</c>) reaches the next page load without a rebuild or a restart — the same
/// property the week-4 demo relies on for `Fake:*`.
///
/// **Anonymous by design.** Slice 5.3's Option 2 public page runs this identical gate with no token,
/// so requiring auth here would only mean building a second endpoint for it later. What it discloses
/// is the validation rules a client must satisfy — dimensions, a size cap, content types — not client
/// data; today every value is a placeholder. Note it sits outside <c>/public/*</c>, so §9.1's rate
/// limiters do not cover it: it is a static projection of configuration that touches no database and
/// no user, which is why that is acceptable and recorded rather than assumed.
/// </summary>
public static class MediaConfigEndpoints
{
    public static IEndpointRouteBuilder MapMediaConfigEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/config/media", (
            IOptionsMonitor<ClarityOptions> clarityOptions,
            IOptionsMonitor<MediaOptions> mediaOptions) =>
        {
            var clarity = clarityOptions.CurrentValue;
            var media = mediaOptions.CurrentValue;

            var buckets = MediaBuckets.AllRules
                .Select(rule => new BucketConfigDto(
                    rule.Bucket,
                    rule.AllowUpload,
                    [.. media.AllowedFor(rule.Kind)]))
                .ToList();

            return Results.Ok(new MediaConfigDto(
                new ClarityConfigDto(
                    clarity.MinWidth,
                    clarity.MinHeight,
                    clarity.BlurVarianceThreshold,
                    clarity.BlurAnalysisMaxEdge),
                media.MaxFileMb,
                buckets));
        });

        return app;
    }
}
