using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Api.Infrastructure;
using Api.Integrations.Sms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Modules.Users;

public sealed record OtpRequestResult(bool IsThrottled, int RetryAfterSeconds)
{
    public static readonly OtpRequestResult Accepted = new(false, 0);
}

public sealed class OtpService(AppDbContext db, ISmsSender sms, IOptions<AuthOptions> options, TimeProvider time)
{
    private readonly AuthOptions _options = options.Value;

    /// <summary>Login path: silently does nothing unless the phone belongs to an active user (no enumeration).</summary>
    public async Task<OtpRequestResult> RequestLoginCode(string phone, CancellationToken ct)
    {
        var throttled = await CheckThrottle(phone, ct);
        if (throttled is not null)
        {
            return throttled;
        }

        var userExists = await db.Users.AsNoTracking()
            .AnyAsync(u => u.Phone == phone && u.Status == UserStatus.Active, ct);
        if (userExists)
        {
            await CreateAndSendChallenge(phone, ct);
        }

        return OtpRequestResult.Accepted;
    }

    /// <summary>Invite path (S1): the caller has already validated the invite; the user is still 'invited'.</summary>
    public async Task<OtpRequestResult> RequestInviteCode(string phone, CancellationToken ct)
    {
        var throttled = await CheckThrottle(phone, ct);
        if (throttled is not null)
        {
            return throttled;
        }

        await CreateAndSendChallenge(phone, ct);
        return OtpRequestResult.Accepted;
    }

    public async Task<bool> VerifyCode(string phone, string code, CancellationToken ct)
    {
        var now = Now();
        var challenge = await db.OtpChallenges
            .Where(c => c.Phone == phone)
            .OrderByDescending(c => c.CreatedAt)
            .ThenByDescending(c => c.Id)
            .FirstOrDefaultAsync(ct);

        if (challenge is null
            || challenge.ConsumedAt is not null
            || challenge.ExpiresAt <= now
            || challenge.Attempts >= _options.OtpMaxAttempts)
        {
            return false;
        }

        var expected = Encoding.UTF8.GetBytes(challenge.CodeHash);
        var actual = Encoding.UTF8.GetBytes(ComputeCodeHash(challenge.Id, code));
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            challenge.Attempts++;
            await db.SaveChangesAsync(ct);
            return false;
        }

        challenge.ConsumedAt = now;
        await db.SaveChangesAsync(ct);
        return true;
    }

    // The challenge id doubles as the salt — §4's column list holds with no salt column.
    internal static string ComputeCodeHash(Guid challengeId, string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{challengeId:N}:{code}")));

    private async Task<OtpRequestResult?> CheckThrottle(string phone, CancellationToken ct)
    {
        var latestCreatedAt = await db.OtpChallenges.AsNoTracking()
            .Where(c => c.Phone == phone)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => (DateTime?)c.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (latestCreatedAt is null)
        {
            return null;
        }

        var window = TimeSpan.FromSeconds(_options.OtpResendSeconds);
        var elapsed = Now() - latestCreatedAt.Value;
        if (elapsed >= window)
        {
            return null;
        }

        return new OtpRequestResult(true, (int)Math.Ceiling((window - elapsed).TotalSeconds));
    }

    private async Task CreateAndSendChallenge(string phone, CancellationToken ct)
    {
        var now = Now();
        var id = Guid.CreateVersion7();
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);

        db.OtpChallenges.Add(new OtpChallenge
        {
            Id = id,
            Phone = phone,
            CodeHash = ComputeCodeHash(id, code),
            ExpiresAt = now.AddMinutes(_options.OtpTtlMinutes),
            Attempts = 0,
            CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);

        await sms.Send(phone, $"Your verification code is {code}", ct);
    }

    private DateTime Now() => time.GetUtcNow().UtcDateTime;
}
