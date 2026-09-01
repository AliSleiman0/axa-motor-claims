namespace Api.Infrastructure;

/// <summary>
/// The SPA fallback's route constraint (slice 7.6, design.md §10) — pulled out of `Program.cs` into
/// a constant so <c>StaticFallbackRegexTests</c> pins the exact pattern that runs rather than a
/// hand-copied twin of it that could silently drift.
/// </summary>
public static class StaticFallbackRoute
{
    /// <summary>
    /// Excludes `/api`, `/auth` and `/public` (with or without a trailing segment) from the SPA
    /// fallback — §9.1's uniform surface must keep answering those with its own 404s regardless of
    /// whether a `wwwroot` exists. Everything else (`/p/:token`, every SPA route, `/`) matches.
    /// </summary>
    public const string ExcludedPrefixesPattern = @"^(?!api($|/)|auth($|/)|public($|/)).*$";

    public const string RouteTemplate = $"{{*path:regex({ExcludedPrefixesPattern})}}";
}
