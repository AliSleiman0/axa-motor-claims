namespace Api.Modules.Media;

/// <summary>
/// design.md Appendix A's `Clarity` section (#9). The blur threshold is the client gate's (slice 2.5,
/// §7.2 item 2); the two dimensions are re-checked on the server per §7.2 item 5, because a client
/// check is user experience and not a control.
/// </summary>
public sealed class ClarityOptions
{
    public const string SectionName = "Clarity";

    public int MinWidth { get; set; } = 1024;

    public int MinHeight { get; set; } = 768;

    /// <summary>Client-side only — there is no server-side Laplacian pass (§7.2 item 5 names three checks, not four).</summary>
    public int BlurVarianceThreshold { get; set; } = 100;

    /// <summary>
    /// The longest edge the client's blur pass runs at, client-side only like the threshold above.
    ///
    /// Laplacian variance scales with resolution, so without a fixed analysis size the same photo
    /// scores differently on a 12 MP and a 48 MP handset and <see cref="BlurVarianceThreshold"/>
    /// would mean a different thing on every device in the expert network. It lives here rather than
    /// as a constant in the web app so the two values move together when #9 is answered — a
    /// threshold is meaningless without the scale it was measured at. The resolution floor above is
    /// still applied to the image's native dimensions, which is what this server re-checks.
    /// </summary>
    public int BlurAnalysisMaxEdge { get; set; } = 512;
}
