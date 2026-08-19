using System.Security.Cryptography;
using Api.Infrastructure;
using Api.Integrations.Sms;
using Api.Modules.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Api.Modules.Users;

public enum InviteAcceptOutcome
{
    NotFound,
    Throttled,
    OtpSent,
}

public sealed record InviteAcceptResult(InviteAcceptOutcome Outcome, int RetryAfterSeconds);

public enum InviteVerifyOutcome
{
    InviteNotFound,
    CodeRejected,
    Activated,
}

public sealed record InviteVerifyResult(InviteVerifyOutcome Outcome, TokenPair? Tokens);

public sealed class InviteService(
    AppDbContext db,
    OtpService otp,
    TokenService tokens,
    ISmsSender sms,
    AuditWriter audit,
    IOptions<AuthOptions> options,
    TimeProvider time)
{
    public async Task<string> Issue(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId, ct);
        var raw = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

        db.Invites.Add(new Invite
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            TokenHash = HashToken(raw),
            ExpiresAt = Now().AddDays(options.Value.InviteValidityDays),
        });
        await db.SaveChangesAsync(ct);

        await sms.Send(user.Phone, $"Your registration invite token is {raw}", ct);
        return raw;
    }

    public async Task<InviteAcceptResult> Accept(string rawToken, CancellationToken ct)
    {
        var found = await FindValid(rawToken, ct);
        if (found is null)
        {
            return new InviteAcceptResult(InviteAcceptOutcome.NotFound, 0);
        }

        var result = await otp.RequestInviteCode(found.Value.User.Phone, ct);
        return result.IsThrottled
            ? new InviteAcceptResult(InviteAcceptOutcome.Throttled, result.RetryAfterSeconds)
            : new InviteAcceptResult(InviteAcceptOutcome.OtpSent, 0);
    }

    public async Task<InviteVerifyResult> VerifyAndActivate(string rawToken, string code, CancellationToken ct)
    {
        var found = await FindValid(rawToken, ct);
        if (found is null)
        {
            return new InviteVerifyResult(InviteVerifyOutcome.InviteNotFound, null);
        }

        var (invite, user) = found.Value;
        if (!await otp.VerifyCode(user.Phone, code, ct))
        {
            return new InviteVerifyResult(InviteVerifyOutcome.CodeRejected, null);
        }

        user.Status = UserStatus.Active;
        invite.UsedAt = Now();
        // Activation issues tokens, so it is a login — audited per §9 (recorded in scope-decisions.md).
        audit.Append(user.Id, AuditActions.UserActivated, AuditEntityKinds.AppUser, user.Id);
        // IssueTokens saves — the activation, used_at, audit row, and refresh row commit together.
        var pair = await tokens.IssueTokens(user, ct);
        return new InviteVerifyResult(InviteVerifyOutcome.Activated, pair);
    }

    private async Task<(Invite Invite, AppUser User)?> FindValid(string rawToken, CancellationToken ct)
    {
        var hash = HashToken(rawToken);
        var row = await db.Invites
            .Where(i => i.TokenHash == hash)
            .Join(db.Users, i => i.UserId, u => u.Id, (invite, user) => new { invite, user })
            .SingleOrDefaultAsync(ct);

        if (row is null
            || row.invite.UsedAt is not null
            || row.invite.ExpiresAt <= Now()
            || row.user.Status != UserStatus.Invited)
        {
            return null;
        }

        return (row.invite, row.user);
    }

    private static string HashToken(string raw) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)));

    private DateTime Now() => time.GetUtcNow().UtcDateTime;
}
