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
}
