namespace Api.Modules.PublicSurface;

/// <summary>Bound from the "PublicLink" section; values are placeholders per design.md Appendix A (#24).</summary>
public sealed class PublicLinkOptions
{
    public const string SectionName = "PublicLink";

    /// <summary>§9.1: how long a link stays usable. #24b decides the real number.</summary>
    public int ValidityDays { get; set; }

    /// <summary>copy | sms — #24a. Until answered the broker copies the link (§5.3 B3).</summary>
    public string DeliveryChannel { get; set; } = "copy";

    /// <summary>§9.1 hard cap on files per submission.</summary>
    public int MaxFiles { get; set; }

    /// <summary>§9.1 hard cap on the size of any one file, in megabytes.</summary>
    public int MaxFileMb { get; set; }

    public PublicRateLimitOptions RateLimit { get; set; } = new();

    /// <summary>The <see cref="MaxFileMb"/> cap in bytes — the unit every check actually needs.</summary>
    public long MaxFileBytes => (long)MaxFileMb * 1024 * 1024;
}

/// <summary>
/// §9.1's "per-IP and per-token rate limits". The section names no numbers and no open question
/// covers them, so they are engineering knobs — named config rather than literals in code, so a
/// pen-test finding (#21) or a real traffic shape can retune them without a rebuild.
/// </summary>
public sealed class PublicRateLimitOptions
{
    public int PerIpPermitsPerMinute { get; set; }

    public int PerTokenPermitsPerMinute { get; set; }
}
