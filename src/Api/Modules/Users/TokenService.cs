using System.Security.Cryptography;
using System.Text;
using Api.Infrastructure;
using Api.Modules.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Api.Modules.Users;

public sealed record TokenPair(string AccessToken, string RefreshToken);

public sealed class TokenService(
    AppDbContext db, AuditWriter audit, IOptions<AuthOptions> options, TimeProvider time)
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

            // **The app's only automatic "somebody may be replaying a stolen credential" signal, and
            // it was silent until slice 7.2** — the user simply found themselves signed out of every
            // device with nothing anywhere saying why. §9's trail exists for exactly this question.
            //
            // The actor is the token's owner rather than the caller, because the caller is by
            // definition unauthenticated: presenting a revoked refresh token is what got them here,
            // and it is the only identity in the request. Nothing about the presented token is
            // recorded — the raw value is a live-until-a-moment-ago credential and §9 goes to the
            // trouble of storing only its hash.
            //
            // Appended before the save that revokes the family, so the two commit together: a
            // revocation without its record is the state a support call cannot explain.
            audit.Append(
                token.UserId, AuditActions.RefreshTokenFamilyRevoked, AuditEntityKinds.AppUser,
                token.UserId, new { Reason = "reuse_detected" });

            await db.SaveChangesAsync(ct);
            return null;
        }

        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == token.UserId, ct);
        if (token.ExpiresAt <= Now() || user is null || user.Status != UserStatus.Active)
        {
            return null;
        }

        // **The claim is in the WHERE, not in the read above (slice 7.2, db-review).** Written as a
        // tracked `token.RevokedAt = Now()`, two simultaneous presentations of the same refresh token
        // both saw a null and both rotated — two live families from one token, and the reuse branch
        // above (with its new audit row) never fires for either. CLAUDE.md's first recurring bug
        // class, on the one credential path where it matters most.
        //
        // Its own statement, so the revocation lands before the new pair is issued: if the insert
        // then fails, the caller is signed out rather than holding two live tokens. The tracked
        // entity is deliberately left alone — it is not written again, and reloading it would buy
        // nothing this method still needs.
        var claimed = await db.RefreshTokens
            .Where(t => t.Id == token.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, Now()), ct);

        if (claimed == 0)
        {
            return null;
        }

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
