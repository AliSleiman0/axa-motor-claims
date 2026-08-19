using System.Security.Cryptography;
using System.Text;
using Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Api.Modules.Users;

public sealed record TokenPair(string AccessToken, string RefreshToken);

public sealed class TokenService(AppDbContext db, IOptions<AuthOptions> options, TimeProvider time)
{
    private static readonly JsonWebTokenHandler TokenHandler = new();

    private readonly JwtOptions _jwt = options.Value.Jwt;

    public async Task<TokenPair> IssueTokens(AppUser user, CancellationToken ct)
    {
        var access = CreateAccessToken(user);
        var refresh = await CreateRefreshToken(user.Id, ct);
        return new TokenPair(access, refresh);
    }

    public async Task<TokenPair?> Rotate(string rawRefreshToken, CancellationToken ct)
    {
        var hash = HashToken(rawRefreshToken);
        var token = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null)
        {
            return null;
        }

        if (token.RevokedAt is not null)
        {
            // Reuse of an already-rotated token: kill the whole family (design.md §4).
            await RevokeAll(token.UserId, ct);
            await db.SaveChangesAsync(ct);
            return null;
        }

        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == token.UserId, ct);
        if (token.ExpiresAt <= Now() || user is null || user.Status != UserStatus.Active)
        {
            return null;
        }

        token.RevokedAt = Now();
        var access = CreateAccessToken(user);
        var refresh = await CreateRefreshToken(user.Id, ct);
        return new TokenPair(access, refresh);
    }

    /// <summary>Marks all live refresh tokens revoked. Does not save — the caller's SaveChanges keeps it in one transaction.</summary>
    public async Task RevokeAll(Guid userId, CancellationToken ct)
    {
        var now = Now();
        var live = await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var token in live)
        {
            token.RevokedAt = now;
        }
    }

    private string CreateAccessToken(AppUser user)
    {
        var now = Now();
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _jwt.Issuer,
            Audience = _jwt.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(_jwt.AccessTokenMinutes),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
                ["role"] = user.Role.ToDbValue(),
                [JwtRegisteredClaimNames.Jti] = Guid.CreateVersion7().ToString(),
            },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey)),
                SecurityAlgorithms.HmacSha256),
        };
        return TokenHandler.CreateToken(descriptor);
    }

    private async Task<string> CreateRefreshToken(Guid userId, CancellationToken ct)
    {
        var now = Now();
        var raw = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            TokenHash = HashToken(raw),
            ExpiresAt = now.AddDays(_jwt.RefreshTokenDays),
            CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);
        return raw;
    }

    private static string HashToken(string raw) => TokenHashing.Hash(raw);

    private DateTime Now() => time.GetUtcNow().UtcDateTime;
}
