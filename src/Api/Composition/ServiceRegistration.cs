using System.Text;
using Api.Infrastructure;
using Api.Integrations.Sms;
using Api.Modules.Audit;
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
        services.AddSingleton<ISmsSender, FakeSmsSender>();

        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));
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
}
