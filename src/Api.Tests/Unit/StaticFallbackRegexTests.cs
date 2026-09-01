using System.Text.RegularExpressions;
using Api.Infrastructure;

namespace Api.Tests.Unit;

/// <summary>
/// The SPA fallback's route constraint (slice 7.6, design.md §10) — the card's own "Test focus"
/// line: "the fallback regex... pinned — it must never eat /api/auth/public." Tested as a pure
/// <see cref="Regex"/> here, not through routing, because no test host has a `wwwroot` to route a
/// real request through (the same accepted gap as the static-serving positive path itself, covered
/// instead by the deploy smoke). This pins the exact pattern `Program.cs` uses
/// (<see cref="StaticFallbackRoute.ExcludedPrefixesPattern"/>), not a hand-copied twin of it.
/// </summary>
public class StaticFallbackRegexTests
{
    private static readonly Regex Pattern = new(StaticFallbackRoute.ExcludedPrefixesPattern);

    [Theory]
    [InlineData("api")]
    [InlineData("api/expert/assignments")]
    [InlineData("auth")]
    [InlineData("auth/otp/request")]
    [InlineData("public")]
    [InlineData("public/PLACEHOLDER-token")]
    public void ApiAuthAndPublicPrefixes_NeverMatch(string path)
    {
        Assert.DoesNotMatch(Pattern, path);
    }

    [Theory]
    [InlineData("")]
    [InlineData("p/PLACEHOLDER-token")]
    [InlineData("expert")]
    [InlineData("expert/history")]
    [InlineData("apiary")] // a real trap: must not match on "api" as a bare prefix of another word
    [InlineData("authentic")]
    [InlineData("publication")]
    public void EverythingElse_Matches(string path)
    {
        Assert.Matches(Pattern, path);
    }
}
