using System.Text;
using Api.Infrastructure;
using Api.Infrastructure.Cleanup;
using Api.Integrations;
using Api.Integrations.Blob;
using Api.Integrations.Email;
using Api.Integrations.Next3;
using Api.Integrations.Push;
using Api.Integrations.Sms;
using Api.Modules.Audit;
using Api.Modules.Claims;
using Api.Modules.Expert;
using Api.Modules.Media;
using Api.Modules.Notifications;
using Api.Modules.PublicSurface;
using Api.Modules.Users;
using Api.Outbox;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace Api.Composition;

public static class ServiceRegistration
{
    public static IServiceCollection AddAxaMotorClaims(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
        services.AddHostedService<OutboxWorker>();
        services.AddHostedService<CleanupWorker>();

        services.TryAddSingleton(TimeProvider.System);

        AddPorts(services, configuration);

        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));
        services.Configure<PublicLinkOptions>(configuration.GetSection(PublicLinkOptions.SectionName));
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));
        services.Configure<MediaOptions>(configuration.GetSection(MediaOptions.SectionName));
        services.Configure<ClarityOptions>(configuration.GetSection(ClarityOptions.SectionName));
        services.Configure<RetentionOptions>(configuration.GetSection(RetentionOptions.SectionName));
        services.Configure<Next3Options>(configuration.GetSection(Next3Options.SectionName));

        // The §6.3 queue. The writer is scoped because it joins the caller's transaction; the
        // processor is a singleton that opens its own scope per pass (NotificationLog's shape).
        services.AddScoped<OutboxWriter>();
        services.AddScoped<OutboxDequeue>();
        services.AddScoped<OutboxSentQuery>();
        services.AddSingleton<OutboxProcessor>();

        // §7's media pipeline. The upload service is scoped so its document row, outbox row and audit
        // row all join one SaveChanges; the cleanup runner is a singleton opening its own scope, like
        // the outbox processor, and each sweep is registered by the module that owns its data.
        services.AddScoped<MediaUploadService>();
        services.AddSingleton<CleanupRunner>();
        services.AddScoped<ICleanupTask, MediaBlobCleanupTask>();
        services.AddScoped<ICleanupTask, OtpChallengeCleanupTask>();
        services.AddPublicRateLimiting();
        services.AddScoped<PublicLinkTokenService>();
        services.AddScoped<ClaimCache>();
        services.AddScoped<AssignmentHandler>();
        // Subscribes the single idempotent handler to whichever assignment source is configured
        // (§6.2). Fails at startup rather than at first delivery if the source is unimplemented.
        services.AddHostedService<AssignmentIngestion>();
        services.AddScoped<OtpService>();
        services.AddScoped<TokenService>();
        services.AddScoped<InviteService>();
        services.AddScoped<AuditWriter>();
        services.AddScoped<IAuthorizationHandler, ActiveUserHandler>();

        var jwt = configuration.GetSection("Auth:Jwt").Get<JwtOptions>()
            ?? throw new InvalidOperationException("Configuration section 'Auth:Jwt' is missing.");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Keep the raw "sub"/"role" claim names — the default inbound mapping renames
                // them and silently breaks RequireRole. ClockSkew 0 so short TTLs mean what they say.
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    RoleClaimType = "role",
                    NameClaimType = "sub",
                    ClockSkew = TimeSpan.Zero,
                };
            });

        // Lifetime is checked against the app's TimeProvider so tests can travel time.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<TimeProvider>((options, timeProvider) =>
                options.TokenValidationParameters.LifetimeValidator = (notBefore, expires, _, _) =>
                {
                    var now = timeProvider.GetUtcNow().UtcDateTime;
                    return (notBefore is null || notBefore <= now) && expires is not null && now < expires;
                });

        var activeUser = new ActiveUserRequirement();
        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.Expert, p => p.RequireRole(UserRoles.Expert).AddRequirements(activeUser))
            .AddPolicy(AuthPolicies.Garage, p => p.RequireRole(UserRoles.Garage).AddRequirements(activeUser))
            .AddPolicy(AuthPolicies.ClaimOfficer, p => p.RequireRole(UserRoles.ClaimOfficer).AddRequirements(activeUser))
            .AddPolicy(AuthPolicies.Broker, p => p.RequireRole(UserRoles.Broker).AddRequirements(activeUser))
            .AddPolicy(AuthPolicies.Admin, p => p.RequireRole(UserRoles.Admin).AddRequirements(activeUser))
            .AddPolicy(AuthPolicies.ActiveUser, p => p.RequireAuthenticatedUser().AddRequirements(activeUser));

        return services;
    }

    /// <summary>
    /// The five ports of design.md §6.2/§3 and their implementations. This method is the only place
    /// in the codebase allowed to name a concrete NEXT3 implementation (arch rule 1) — everything
    /// else depends on the interface and cannot tell which one is live.
    /// </summary>
    private static void AddPorts(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FakeOptions>(configuration.GetSection(FakeOptions.SectionName));
        services.AddSingleton<FakeBehavior>();
        services.AddSingleton<NotificationLog>();

        // The sixth port (§3's Azure Blob, §7.3's transit buffer). Same shape as Next3:Mode: the fake
        // is the default so nothing — tests included — needs a storage emulator to be running.
        services.Configure<BlobOptions>(configuration.GetSection(BlobOptions.SectionName));
        services.AddSingleton<InMemoryBlobStore>();
        var blobMode = configuration[$"{BlobOptions.SectionName}:Mode"] ?? "fake";
        services.AddSingleton<IBlobStore>(sp => blobMode switch
        {
            "fake" => sp.GetRequiredService<InMemoryBlobStore>(),
            "azure" => ActivatorUtilities.CreateInstance<AzureBlobStore>(sp),
            _ => throw new InvalidOperationException(
                $"Unknown Blob:Mode '{blobMode}'. Expected 'fake' or 'azure' (see CLAUDE.md)."),
        });

        // Senders are singletons and there are no real implementations yet (#7/#40 for SMS, and the
        // email/push providers are equally unanswered). Each fake logs to `notification` (§8).
        services.AddSingleton<ISmsSender, FakeSmsSender>();
        services.AddSingleton<IEmailSender, FakeEmailSender>();
        services.AddSingleton<IPushSender, FakePushSender>();

        // Singleton: the fake's seeded claims and its clientRef sent-log are state that must outlive
        // a request and be shared with the outbox worker.
        services.AddSingleton<FakeNext3Client>();

        var mode = configuration["Next3:Mode"] ?? "fake";
        services.AddSingleton<INext3Client>(sp => mode switch
        {
            "fake" => sp.GetRequiredService<FakeNext3Client>(),
            "real" => ActivatorUtilities.CreateInstance<RealNext3Client>(sp),
            _ => throw new InvalidOperationException(
                $"Unknown Next3:Mode '{mode}'. Expected 'fake' or 'real' (design.md §6.2)."),
        });

        var assignmentSource = configuration["Next3:AssignmentSource"] ?? "fake";
        services.AddSingleton<FakeAssignmentSource>();
        services.AddSingleton<IAssignmentSource>(sp => assignmentSource switch
        {
            "fake" => sp.GetRequiredService<FakeAssignmentSource>(),
            // Both are designed (§6.2) but unbuilt: which one is real is #34, and the answer is a
            // config flip plus one adapter. Failing loudly beats silently delivering no assignments.
            "webhook" or "poll" => throw new InvalidOperationException(
                $"Next3:AssignmentSource '{assignmentSource}' is not implemented yet (#34)."),
            _ => throw new InvalidOperationException(
                $"Unknown Next3:AssignmentSource '{assignmentSource}'. Expected 'webhook', 'poll' or 'fake'."),
        });
    }
}
