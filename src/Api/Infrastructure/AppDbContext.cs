using Api.Modules.Broker;
using Api.Modules.Claims;
using Api.Modules.Declarations;
using Api.Modules.Expert;
using Api.Modules.Media;
using Api.Modules.PublicSurface;
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

    public DbSet<BrokerRequest> BrokerRequests => Set<BrokerRequest>();

    public DbSet<PublicLinkToken> PublicLinkTokens => Set<PublicLinkToken>();

    /// <summary>The one NEXT3 cache (§4) — disposable, never edited locally.</summary>
    public DbSet<CachedClaim> CachedClaims => Set<CachedClaim>();

    public DbSet<ExpertAssignment> ExpertAssignments => Set<ExpertAssignment>();

    /// <summary>§5.2's state machine row, and the comments an officer attaches to a decision.</summary>
    public DbSet<Declaration> Declarations => Set<Declaration>();

    public DbSet<DeclarationComment> DeclarationComments => Set<DeclarationComment>();

    /// <summary>
    /// §4's media metadata. Unlike audit_log, notification and next3_outbox this does have a DbSet:
    /// feature code genuinely reads it (E1's counts, the per-assignment list), and the write path is
    /// constrained by MediaUploadService owning the blob-then-rows ordering rather than by hiding the
    /// set. The binary is transit-only — a row outlives its blob (§7.3).
    /// </summary>
    public DbSet<Document> Documents => Set<Document>();

    // Deliberately no DbSet<AuditLog>: the only sanctioned write path is AuditWriter
    // (tests read via Set<AuditLog>()); the DB trigger enforces append-only.

    // Same for Notification: the only write path is NotificationLog, called by the senders
    // (tests read via Set<Notification>()). Nothing in a feature flow reads it back.

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
