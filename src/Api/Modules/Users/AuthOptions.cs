namespace Api.Modules.Users;

/// <summary>Bound from the "Auth" section; values are placeholders per design.md Appendix A.</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public int OtpTtlMinutes { get; set; }
    public int OtpMaxAttempts { get; set; }
    public int OtpResendSeconds { get; set; }
    public int InviteValidityDays { get; set; }
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
