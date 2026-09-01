using Api.Integrations.Next3;
using Api.Modules.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Tests.Integration;

/// <summary>
/// design.md §6.1/#7/#8, slice 7.5 — the NEXT3 master-data sync reconciling `expert_profile`/
/// `garage_profile` against the client's supplier list. Runs `fixture.MasterDataSync.Run` directly
/// (the same shape `fixture.Cleanup`/`fixture.OutboxProcessor` already use), driving the fake NEXT3
/// client through <see cref="FakeNext3Client.Seed(Next3Expert)"/>/<see cref="FakeNext3Client.RemoveExpert"/>
/// rather than the real one — no live Oracle/NEXT3 connection anywhere in this suite.
/// </summary>
[Collection("api")]
public sealed class MasterDataSyncTaskTests
{
    private readonly ApiFixture fixture;

    /// <summary>
    /// <see cref="MasterDataSyncTask"/> is a process-wide singleton (its own doc comment explains
    /// why), so its self-throttle state outlives any one test in the shared, serialized "api"
    /// collection — without resetting it here, only the first test in the whole run to call
    /// <c>Run</c> would ever see a real sync pass.
    /// </summary>
    public MasterDataSyncTaskTests(ApiFixture fixture)
    {
        this.fixture = fixture;
        fixture.MasterDataSync.ResetThrottleForTests();
    }

    [Fact]
    public async Task NewActiveExpert_CreatesProfileAndIssuesInvite()
    {
        var next3Id = NextId();
        var phone = TestPhones.Next();
        Seed(new Next3Expert(next3Id, "PLACEHOLDER Sync Expert", phone, "sync-expert@example.invalid", true));

        await fixture.MasterDataSync.Run(CancellationToken.None);

        await using var db = fixture.CreateDbContext();
        var profile = await db.ExpertProfiles.AsNoTracking().SingleAsync(p => p.Next3Id == next3Id);
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == profile.UserId);
        Assert.Equal(UserStatus.Invited, user.Status);
        Assert.True(profile.Active);
        Assert.True(await db.Invites.AnyAsync(i => i.UserId == user.Id));
        Assert.True(fixture.Sms.CountFor(phone) > 0);
    }

    [Fact]
    public async Task NewActiveGarage_CreatesProfileAndIssuesInvite()
    {
        var next3Id = NextId();
        var phone = TestPhones.Next();
        SeedGarage(new Next3Garage(next3Id, "PLACEHOLDER Sync Garage", phone, "sync-garage@example.invalid", true));

        await fixture.MasterDataSync.Run(CancellationToken.None);

        await using var db = fixture.CreateDbContext();
        var profile = await db.GarageProfiles.AsNoTracking().SingleAsync(p => p.Next3Id == next3Id);
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == profile.UserId);
        Assert.Equal(UserStatus.Invited, user.Status);
        Assert.True(profile.Active);
        Assert.Equal("PLACEHOLDER Sync Garage", profile.ContactName);
    }

    [Fact]
    public async Task DroppedSupplier_BlocksLoginWithoutDeletingTheProfile()
    {
        var (next3Id, user) = await CreateMappedActiveExpert();
        await SeedRefreshToken(user.Id);

        FakeClient().RemoveExpert(next3Id);
        await fixture.MasterDataSync.Run(CancellationToken.None);

        await using var db = fixture.CreateDbContext();
        var refreshedUser = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        var profile = await db.ExpertProfiles.AsNoTracking().SingleAsync(p => p.UserId == user.Id);
        Assert.Equal(UserStatus.SyncBlocked, refreshedUser.Status);
        Assert.NotNull(refreshedUser.InactivatedAt);
        Assert.False(profile.Active);
        Assert.NotNull(profile.InactivatedAt);
        Assert.True(await db.RefreshTokens.Where(t => t.UserId == user.Id).AllAsync(t => t.RevokedAt != null));
    }

    [Fact]
    public async Task NextInactiveSupplier_AlsoBlocksLogin()
    {
        // NEXT3 still lists the supplier (in network) but marks it inactive — the other half of
        // "should be active", distinct from dropping out of the result set entirely.
        var (next3Id, user) = await CreateMappedActiveExpert();

        Seed(new Next3Expert(next3Id, "PLACEHOLDER Sync Expert", "+999000009999", "x@example.invalid", false));
        await fixture.MasterDataSync.Run(CancellationToken.None);

        await using var db = fixture.CreateDbContext();
        var refreshedUser = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        Assert.Equal(UserStatus.SyncBlocked, refreshedUser.Status);
    }

    [Fact]
    public async Task ReappearingSupplier_UnblocksLogin()
    {
        var (next3Id, user) = await CreateMappedActiveExpert();
        FakeClient().RemoveExpert(next3Id);
        await fixture.MasterDataSync.Run(CancellationToken.None);

        // A second Run within this test is a second sync cycle, not a throttle test — that has its
        // own tests below — so the throttle is reset between calls.
        fixture.MasterDataSync.ResetThrottleForTests();
        Seed(new Next3Expert(next3Id, "PLACEHOLDER Sync Expert", "+999000009998", "x@example.invalid", true));
        await fixture.MasterDataSync.Run(CancellationToken.None);

        await using var db = fixture.CreateDbContext();
        var refreshedUser = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        var profile = await db.ExpertProfiles.AsNoTracking().SingleAsync(p => p.UserId == user.Id);
        Assert.Equal(UserStatus.Active, refreshedUser.Status);
        Assert.Null(refreshedUser.InactivatedAt);
        Assert.True(profile.Active);
        Assert.Null(profile.InactivatedAt);
    }

    [Fact]
    public async Task AdminDeactivatedSupplier_IsNeverTouchedBySync()
    {
        // The whole point of a distinct SyncBlocked status (design.md §4): the sync task must never
        // silently undo an admin's own deliberate deactivation, however NEXT3's list reads.
        var (next3Id, user) = await CreateMappedActiveExpert();

        await using (var db = fixture.CreateDbContext())
        {
            var tracked = await db.Users.SingleAsync(u => u.Id == user.Id);
            tracked.Status = UserStatus.Inactive;
            tracked.InactivatedAt = fixture.Time.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync();
        }

        FakeClient().RemoveExpert(next3Id); // dropped
        await fixture.MasterDataSync.Run(CancellationToken.None);
        fixture.MasterDataSync.ResetThrottleForTests(); // a second sync cycle, not a throttle test
        Seed(new Next3Expert(next3Id, "PLACEHOLDER Sync Expert", "+999000009997", "x@example.invalid", true)); // reappears
        await fixture.MasterDataSync.Run(CancellationToken.None);

        await using var check = fixture.CreateDbContext();
        var refreshedUser = await check.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        Assert.Equal(UserStatus.Inactive, refreshedUser.Status);
    }

    [Fact]
    public async Task ClaimOfficersAndBrokers_AreUntouchedBySync()
    {
        var officer = await fixture.CreateUser(UserRole.ClaimOfficer, UserStatus.Active);
        var broker = await fixture.CreateUser(UserRole.Broker, UserStatus.Active);
        await using (var db = fixture.CreateDbContext())
        {
            db.ClaimOfficerProfiles.Add(new ClaimOfficerProfile
            {
                UserId = officer.Id,
                Next3User = "PLACEHOLDER-CO-01",
                Email = "co@example.invalid",
            });
            db.BrokerProfiles.Add(new BrokerProfile
            {
                UserId = broker.Id,
                IrisCode = "PLACEHOLDER-IRIS",
                Email = "broker@example.invalid",
            });
            await db.SaveChangesAsync();
        }

        await fixture.MasterDataSync.Run(CancellationToken.None);

        await using var check = fixture.CreateDbContext();
        Assert.Equal(UserStatus.Active, (await check.Users.AsNoTracking().SingleAsync(u => u.Id == officer.Id)).Status);
        Assert.Equal(UserStatus.Active, (await check.Users.AsNoTracking().SingleAsync(u => u.Id == broker.Id)).Status);
    }

    [Fact]
    public async Task WithinTheInterval_SecondRunIsANoOp()
    {
        await fixture.MasterDataSync.Run(CancellationToken.None);

        var next3Id = NextId();
        Seed(new Next3Expert(next3Id, "PLACEHOLDER Late Expert", TestPhones.Next(), "late@example.invalid", true));
        var touched = await fixture.MasterDataSync.Run(CancellationToken.None);

        Assert.Equal(0, touched);
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.ExpertProfiles.AnyAsync(p => p.Next3Id == next3Id));
    }

    [Fact]
    public async Task AfterTheInterval_RunsAgain()
    {
        await fixture.MasterDataSync.Run(CancellationToken.None);

        var next3Id = NextId();
        Seed(new Next3Expert(next3Id, "PLACEHOLDER Late Expert", TestPhones.Next(), "late@example.invalid", true));
        fixture.Time.Advance(TimeSpan.FromHours(25));
        var touched = await fixture.MasterDataSync.Run(CancellationToken.None);

        Assert.True(touched > 0);
        await using var db = fixture.CreateDbContext();
        Assert.True(await db.ExpertProfiles.AnyAsync(p => p.Next3Id == next3Id));
    }

    private static int _counter;

    private static string NextId() => $"PLACEHOLDER-SYNC-T{Interlocked.Increment(ref _counter):D5}";

    private FakeNext3Client FakeClient() => fixture.Services.GetRequiredService<FakeNext3Client>();

    private void Seed(Next3Expert expert) => FakeClient().Seed(expert);

    private void SeedGarage(Next3Garage garage) => FakeClient().Seed(garage);

    private async Task<(string Next3Id, AppUser User)> CreateMappedActiveExpert()
    {
        var next3Id = NextId();
        var user = await fixture.CreateUser(UserRole.Expert, UserStatus.Active);
        await using (var db = fixture.CreateDbContext())
        {
            db.ExpertProfiles.Add(new ExpertProfile
            {
                UserId = user.Id,
                Next3Id = next3Id,
                Email = "mapped@example.invalid",
                Active = true,
            });
            await db.SaveChangesAsync();
        }

        Seed(new Next3Expert(next3Id, "PLACEHOLDER Sync Expert", TestPhones.Next(), "mapped@example.invalid", true));
        return (next3Id, user);
    }

    private async Task SeedRefreshToken(Guid userId)
    {
        await using var db = fixture.CreateDbContext();
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            TokenHash = "PLACEHOLDER-HASH",
            ExpiresAt = fixture.Time.GetUtcNow().UtcDateTime.AddDays(14),
            CreatedAt = fixture.Time.GetUtcNow().UtcDateTime,
        });
        await db.SaveChangesAsync();
    }
}
