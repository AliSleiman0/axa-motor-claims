using System.Text;
using Api.Infrastructure;
using Api.Integrations;
using Api.Integrations.Email;
using Api.Integrations.Next3;
using Api.Integrations.Push;
using Api.Integrations.Sms;
using Api.Modules.Audit;
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

        services.TryAddSingleton(TimeProvider.System);

        AddPorts(services, configuration);

        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));
        services.Configure<PublicLinkOptions>(configuration.GetSection(PublicLinkOptions.SectionName));
        services.AddPublicRateLimiting();
        services.AddScoped<PublicLinkTokenService>();
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
