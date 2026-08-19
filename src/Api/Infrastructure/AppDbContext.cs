using Api.Modules.Users;
using Microsoft.EntityFrameworkCore;

namespace Api.Infrastructure;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();

    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();

    public DbSet<Invite> Invites => Set<Invite>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<ExpertProfile> ExpertProfiles => Set<ExpertProfile>();

    public DbSet<GarageProfile> GarageProfiles => Set<GarageProfile>();

    public DbSet<ClaimOfficerProfile> ClaimOfficerProfiles => Set<ClaimOfficerProfile>();

    public DbSet<BrokerProfile> BrokerProfiles => Set<BrokerProfile>();

    // Deliberately no DbSet<AuditLog>: the only sanctioned write path is AuditWriter
    // (tests read via Set<AuditLog>()); the DB trigger enforces append-only.

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
