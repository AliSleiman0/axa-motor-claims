using Api.Modules.Users;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Integration;

/// <summary>
/// design.md §4: `otp_challenge` is "TTL'd, purged by cleanup job". Slice 1.2 recorded the purge as
/// waiting for whichever slice first built cleanup-job infrastructure; that is this one.
/// </summary>
[Collection("api")]
public sealed class OtpChallengeCleanupTests(ApiFixture fixture)
{
    [Fact]
    public async Task ExpiredChallengesArePurged_AndLiveOnesAreNot()
    {
        var now = fixture.Time.GetUtcNow().UtcDateTime;
        var window = fixture.Retention.CurrentValue.OtpChallengeHours;

        var stale = await Insert(now.AddHours(-(window + 1)));
        var justInside = await Insert(now.AddHours(-(window - 1)));
        var live = await Insert(now.AddMinutes(5));

        var results = await fixture.Sweep();

        Assert.True(results["otp_challenges"] >= 1);

        await using var db = fixture.CreateDbContext();
        Assert.Null(await db.OtpChallenges.AsNoTracking().SingleOrDefaultAsync(c => c.Id == stale));

        // The window is orders of magnitude longer than Auth:OtpTtlMinutes, so nothing a user could
        // still be typing is ever in reach of this sweep.
        Assert.NotNull(await db.OtpChallenges.AsNoTracking().SingleOrDefaultAsync(c => c.Id == justInside));
        Assert.NotNull(await db.OtpChallenges.AsNoTracking().SingleOrDefaultAsync(c => c.Id == live));
    }

    private async Task<Guid> Insert(DateTime expiresAt)
    {
        await using var db = fixture.CreateDbContext();
        var challenge = new OtpChallenge
        {
            Id = Guid.CreateVersion7(),
            Phone = TestPhones.Next(),
            CodeHash = "PLACEHOLDER-hash",
            ExpiresAt = expiresAt,
            CreatedAt = expiresAt.AddMinutes(-5),
        };

        db.OtpChallenges.Add(challenge);
        await db.SaveChangesAsync();
        return challenge.Id;
    }
}
