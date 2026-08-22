namespace Api.Modules.Users;

/// <summary>Bound from the "Auth" section; values are placeholders per design.md Appendix A.</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public int OtpTtlMinutes { get; set; }
    public int OtpMaxAttempts { get; set; }
    public int OtpResendSeconds { get; set; }
    public int InviteValidityDays { get; set; }

    /// <summary>
    /// Where the web app is served from, used to build the invitation link in the onboarding SMS
    /// (design.md §5.4's S1; pass-2 decision 2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Configuration, never the request host.</b> Building this from <c>Request.Host</c> would be
    /// shorter and is wrong: the header is attacker-controlled, and this URL carries a live
    /// credential into an SMS sent in AXA's name — poisoning it would send new users' activation
    /// codes to somebody else's domain.
    /// </para>
    /// <para>
    /// A PLACEHOLDER by default like every other deployment value (Appendix A); overridden per
    /// environment by <c>Auth__AppBaseUrl</c>. Trailing slashes are trimmed where it is used, so
    /// either spelling works.
    /// </para>
    /// </remarks>
    public string AppBaseUrl { get; set; } = string.Empty;
    public JwtOptions Jwt { get; set; } = new();
    public SeedAdminOptions SeedAdmin { get; set; } = new();
}

public sealed class JwtOptions
{
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; }
    public int RefreshTokenDays { get; set; }
}

public sealed class SeedAdminOptions
{
    public string Phone { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}
